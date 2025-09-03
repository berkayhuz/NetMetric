// <copyright file="WindowsProcessIoReader.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NetMetric.SystemIO.Abstractions;

namespace NetMetric.SystemIO.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsProcessIoReader : IProcessIoReader
{
    /// <summary>
    /// Attempts to read the current process I/O statistics, including bytes read and written.
    /// </summary>
    /// <returns>
    /// An <see cref="IoSnapshot"/> containing the bytes read and written, or null if the data is unavailable.
    /// </returns>
    public IoSnapshot? TryReadCurrent()
    {
        try
        {
            var handle = GetCurrentProcess();

            if (handle == IntPtr.Zero)
            {
                return null;
            }

            if (!GetProcessIoCounters(handle, out IO_COUNTERS counters))
            {
                return null;
            }

            return new IoSnapshot(counters.ReadTransferCount, counters.WriteTransferCount, DateTime.UtcNow);
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (ExternalException)
        {
            return null;
        }
        catch
        {
            throw;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS : IEquatable<IO_COUNTERS>
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;

        public bool Equals(IO_COUNTERS other) =>
            ReadOperationCount == other.ReadOperationCount &&
            WriteOperationCount == other.WriteOperationCount &&
            OtherOperationCount == other.OtherOperationCount &&
            ReadTransferCount == other.ReadTransferCount &&
            WriteTransferCount == other.WriteTransferCount &&
            OtherTransferCount == other.OtherTransferCount;

        public override bool Equals(object? obj) =>
            obj is IO_COUNTERS other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(ReadOperationCount, WriteOperationCount, OtherOperationCount,
                             ReadTransferCount, WriteTransferCount, OtherTransferCount);

        public static bool operator ==(IO_COUNTERS left, IO_COUNTERS right) => left.Equals(right);
        public static bool operator !=(IO_COUNTERS left, IO_COUNTERS right) => !(left == right);
    }

    /// <summary>
    /// Gets a handle to the current process.
    /// </summary>
    /// <returns>The handle to the current process.</returns>
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// Retrieves the I/O counters for a specified process.
    /// </summary>
    /// <param name="hProcess">The handle of the process to retrieve the I/O counters for.</param>
    /// <param name="lpIoCounters">An output parameter that will contain the process I/O counters.</param>
    /// <returns>True if the operation was successful, otherwise false.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);
}
