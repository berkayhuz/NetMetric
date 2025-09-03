// <copyright file="LinuxSystemIoReader.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using NetMetric.SystemIO.Abstractions;

namespace NetMetric.SystemIO.Linux;

/// <summary>
/// Provides methods to read I/O statistics for processes and system devices on Linux systems.
/// Reads data from <c>/proc/self/io</c> for process I/O and <c>/proc/diskstats</c> for system-level device I/O.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxSystemIoReader : IProcessIoReader, ISystemIoReader
{
    private const string ProcSelfIo = "/proc/self/io";
    private const string ProcDiskstats = "/proc/diskstats";
    private const string SysBlock = "/sys/block";

    private static readonly string[] IoKeys = ["read_bytes", "write_bytes"]; // /proc/self/io keys

    private readonly IFileReader _fs;
    private readonly ConcurrentDictionary<string, int> _sectorSizeCache = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="LinuxSystemIoReader"/> class.</summary>
    public LinuxSystemIoReader(IFileReader? fs = null)
    {
        _fs = fs ?? new RealFileReader();
    }

    /// <summary>Attempts to read the current process I/O statistics, including read and write bytes.</summary>
    public IoSnapshot? TryReadCurrent()
    {
        try
        {
            if (!_fs.Exists(ProcSelfIo)) return null;

            ulong read = 0, write = 0;
            foreach (var line in _fs.ReadLines(ProcSelfIo))
            {
                int colon = line.IndexOf(':', StringComparison.Ordinal);

                if (colon <= 0)
                {
                    continue;
                }

                var key = line.AsSpan(0, colon).Trim();
                var valSpan = line.AsSpan(colon + 1).Trim();

                if (key.SequenceEqual(IoKeys[0]))
                {
                    if (ulong.TryParse(valSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    {
                        read = v;
                    }
                }
                else if (key.SequenceEqual(IoKeys[1]))
                {
                    if (ulong.TryParse(valSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    {
                        write = v;
                    }
                }
            }

            return new IoSnapshot(read, write, DateTime.UtcNow);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Attempts to read the cumulative I/O statistics for all system devices.</summary>
    public (DateTime tsUtc, IReadOnlyList<DeviceIo> devices)? TryReadDevices()
    {
        try
        {
            if (!_fs.Exists(ProcDiskstats))
            {
                return null;
            }

            var list = new List<DeviceIo>(32);

            foreach (var line in _fs.ReadLines(ProcDiskstats))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (parts.Length < 14)
                {
                    continue;
                }

                var dev = parts[2];

                if (IsPartition(dev) || IsNoiseDevice(dev))
                {
                    continue;
                }

                if (!TryParseU64(parts[Math.Min(5, parts.Length - 1)], out var sectorsRead))
                {
                    continue;
                }

                if (!TryParseU64(parts[Math.Min(9, parts.Length - 1)], out var sectorsWritten))
                {
                    continue;
                }

                int sectorSize = GetSectorSize(dev);

                ulong readBytes = checked((ulong)sectorSize) * sectorsRead;
                ulong writeBytes = checked((ulong)sectorSize) * sectorsWritten;

                list.Add(new DeviceIo(dev, readBytes, writeBytes));
            }

            return (DateTime.UtcNow, list);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseU64(string s, out ulong value)
    {
        return ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsPartition(string devName)
    {
        ArgumentNullException.ThrowIfNull(devName);

        return devName.Length > 0 &&
               (char.IsDigit(devName[^1]) ||
                devName.Contains('p', StringComparison.Ordinal));
    }

    private static bool IsNoiseDevice(string dev)
    {
        ArgumentNullException.ThrowIfNull(dev);

        return dev.StartsWith("loop", StringComparison.Ordinal) ||
               dev.StartsWith("ram", StringComparison.Ordinal) ||
               dev.StartsWith("zram", StringComparison.Ordinal) ||
               dev.StartsWith("sr", StringComparison.Ordinal) ||
               dev.StartsWith("fd", StringComparison.Ordinal) ||
               dev.StartsWith("dm-", StringComparison.Ordinal) ||
               dev.StartsWith("md", StringComparison.Ordinal) ||
               dev.StartsWith("mapper/", StringComparison.Ordinal);
    }

    private int GetSectorSize(string device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var root = RootDevice(device);
        if (_sectorSizeCache.TryGetValue(root, out var cached))
            return cached;

        int size = 512;

        try
        {
            var logical = Path.Combine(SysBlock, root, "queue", "logical_block_size");
            var hw = Path.Combine(SysBlock, root, "queue", "hw_sector_size");

            if (_fs.Exists(logical) &&
                int.TryParse(_fs.ReadAllText(logical).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) &&
                l > 0)
            {
                size = l;
            }
            else if (_fs.Exists(hw) &&
                     int.TryParse(_fs.ReadAllText(hw).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) &&
                     h > 0)
            {
                size = h;
            }
        }
        catch (IOException)
        {

        }
        catch (UnauthorizedAccessException)
        {

        }

        _sectorSizeCache[root] = size;
        return size;
    }

    private static string RootDevice(string dev)
    {
        ArgumentNullException.ThrowIfNull(dev);

        if (dev.StartsWith("nvme", StringComparison.Ordinal))
        {
            int pIdx = dev.IndexOf('p', StringComparison.Ordinal);

            return pIdx > 0 ? dev[..pIdx] : dev;
        }

        if (dev.StartsWith("mmcblk", StringComparison.Ordinal))
        {
            int pIdx = dev.IndexOf('p', StringComparison.Ordinal);

            return pIdx > 0 ? dev[..pIdx] : dev;
        }

        int i = dev.Length - 1;

        while (i >= 0 && char.IsDigit(dev[i]))
        {
            i--;
        }

        return i >= 0 ? dev[..(i + 1)] : dev;
    }
}
