// <copyright file="SystemMemoryCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Memory.Collectors;

/// <summary>
/// Collects system memory usage metrics, including information on total memory, 
/// used memory, available memory, and additional memory statistics for different operating systems.
/// Supports Linux (with cgroup v2), Windows, macOS, and provides fallback for unsupported platforms.
/// </summary>
public sealed partial class SystemMemoryCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;
    private readonly MemoryModuleOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemMemoryCollector"/> class.
    /// </summary>
    /// <param name="factory">The factory used to create metrics.</param>
    /// <param name="options">Configuration options for the memory collector.</param>
    /// <exception cref="ArgumentNullException">Thrown if the factory or options are null.</exception>
    public SystemMemoryCollector(IMetricFactory factory, MemoryModuleOptions options)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Asynchronously collects system memory statistics for various platforms, including Linux, 
    /// Windows, and macOS. It includes metrics such as total memory, used memory, 
    /// available memory, and cached memory.
    /// </summary>
    /// <param name="ct">A cancellation token to allow task cancellation.</param>
    /// <returns>A task that represents the asynchronous operation, 
    /// containing the generated metrics as the result.</returns>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        const string id = "mem.system.bytes";
        const string name = "System Memory (bytes)";

        try
        {
            ct.ThrowIfCancellationRequested();

            var mg = _factory.MultiGauge(id, name).WithResetOnGet(true).Build();

            // Optional cgroup v2 (Kubernetes / containers)
            if (_options.EnableCgroup && OperatingSystem.IsLinux() && TryReadCgroupV2(out var limit, out var usage, out var swapLimit, out var swapUsage, out var limUnlimited, out var swapLimUnlimited))
            {
                var baseTags = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["os"] = "Linux",
                    ["scope"] = "cgroup",
                    ["status"] = "ok"
                };

                var tLimit = new Dictionary<string, string>(baseTags) { ["kind"] = "cgroup.limit" };

                if (limUnlimited)
                {
                    tLimit["unlimited"] = "true";
                }

                mg.SetValue(limit, tLimit);

                mg.SetValue(usage, new Dictionary<string, string>(baseTags) { ["kind"] = "cgroup.usage" });

                var tSwapLimit = new Dictionary<string, string>(baseTags) { ["kind"] = "cgroup.swap.limit" };

                if (swapLimUnlimited)
                {
                    tSwapLimit["unlimited"] = "true";
                }

                mg.SetValue(swapLimit, tSwapLimit);

                mg.SetValue(swapUsage, new Dictionary<string, string>(baseTags) { ["kind"] = "cgroup.swap.usage" });
            }

            if (OperatingSystem.IsWindows())
            {
                if (TryReadWindows(out var total, out var avail))
                {
                    mg.SetValue((long)total, Tag("total", "ok", os: "Windows"));
                    mg.SetValue((long)(total - avail), Tag("used", "ok", os: "Windows"));
                    mg.SetValue((long)avail, Tag("available", "ok", os: "Windows"));
                }
                else
                {
                    AddUnsupported(mg, "Windows");
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                if (TryReadLinux(out var total, out var free, out var available, out var cached, out var buffers))
                {
                    mg.SetValue(total, Tag("total", "ok", os: "Linux"));

                    var used = total - available;

                    if (used < 0)
                    {
                        used = Math.Max(0, total - free - cached - buffers);
                    }

                    mg.SetValue(used, Tag("used", "ok", os: "Linux"));
                    mg.SetValue(available, Tag("available", "ok", os: "Linux"));
                    mg.SetValue(cached, Tag("cached", "ok", os: "Linux"));
                    mg.SetValue(buffers, Tag("buffers", "ok", os: "Linux"));
                }
                else
                {
                    AddUnsupported(mg, "Linux");
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (TryReadMac(out var total, out var free, out var active, out var inactive, out var wired))
                {
                    mg.SetValue((long)total, Tag("total", "ok", os: "macOS"));

                    var used = (long)(active + wired + inactive);

                    mg.SetValue(used, Tag("used", "ok", os: "macOS"));
                    mg.SetValue((long)free, Tag("free", "ok", os: "macOS"));
                    mg.SetValue((long)active, Tag("active", "ok", os: "macOS"));
                    mg.SetValue((long)inactive, Tag("inactive", "ok", os: "macOS"));
                    mg.SetValue((long)wired, Tag("wired", "ok", os: "macOS"));
                }
                else
                {
                    AddUnsupported(mg, "macOS");
                }
            }
            else
            {
                AddUnsupported(mg, "Unknown");
            }

            return Task.FromResult<IMetric?>(mg);
        }
        catch (OperationCanceledException)
        {
            var mg = _factory.MultiGauge(id, name).WithResetOnGet(true).Build(); mg.SetValue(0, new Dictionary<string, string> { ["status"] = "cancelled" });

            return Task.FromResult<IMetric?>(mg);
        }
        catch (IOException ex) { return ErrorMetric(id, name, ex); }
        catch (UnauthorizedAccessException ex) { return ErrorMetric(id, name, ex); }
        catch (FormatException ex) { return ErrorMetric(id, name, ex); }
        catch (DllNotFoundException ex) { return ErrorMetric(id, name, ex); }
        catch (EntryPointNotFoundException ex) { return ErrorMetric(id, name, ex); }
        catch (ExternalException ex) { return ErrorMetric(id, name, ex); } // Any other exception will bubble up (no broad catch-all), as requested by analyzers.

    }
    /// <summary> /// Builds a standard error multi-gauge metric with a short reason. /// </summary> 
    private Task<IMetric?> ErrorMetric(string id, string name, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var mg = _factory.MultiGauge(id, name).WithResetOnGet(true).Build();

        mg.SetValue(0, new Dictionary<string, string>
        {
            ["status"] = "error",
            ["error"] = ex.GetType().Name,
            ["reason"] = Short(ex.Message)
        });

        return Task.FromResult<IMetric?>(mg);
    }

    /// <summary>
    /// Reads memory information from Windows system.
    /// </summary>
    /// <param name="total">The total physical memory in bytes.</param>
    /// <param name="avail">The available physical memory in bytes.</param>
    /// <returns>True if the information is successfully retrieved; otherwise, false.</returns>
    private static bool TryReadWindows(out ulong total, out ulong avail)
    {
        total = avail = 0;

        var s = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

        if (!GlobalMemoryStatusEx(ref s))
        {
            return false;
        }

        total = s.ullTotalPhys;
        avail = s.ullAvailPhys;

        return true;
    }

    /// <summary>
    /// Structure to hold memory status information on Windows.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX : IEquatable<MEMORYSTATUSEX>
    {
        public uint dwLength;
        public readonly uint dwMemoryLoad;
        public readonly ulong ullTotalPhys;
        public readonly ulong ullAvailPhys;
        public readonly ulong ullTotalPageFile;
        public readonly ulong ullAvailPageFile;
        public readonly ulong ullTotalVirtual;
        public readonly ulong ullAvailVirtual;
        public readonly ulong ullAvailExtendedVirtual;

        public bool Equals(MEMORYSTATUSEX other) =>
            dwLength == other.dwLength &&
            dwMemoryLoad == other.dwMemoryLoad &&
            ullTotalPhys == other.ullTotalPhys &&
            ullAvailPhys == other.ullAvailPhys &&
            ullTotalPageFile == other.ullTotalPageFile &&
            ullAvailPageFile == other.ullAvailPageFile &&
            ullTotalVirtual == other.ullTotalVirtual &&
            ullAvailVirtual == other.ullAvailVirtual &&
            ullAvailExtendedVirtual == other.ullAvailExtendedVirtual;

        public override bool Equals(object? obj) => obj is MEMORYSTATUSEX other && Equals(other);

        public override int GetHashCode()
        {
            int h = HashCode.Combine(
                dwLength, dwMemoryLoad,
                ullTotalPhys, ullAvailPhys,
                ullTotalPageFile, ullAvailPageFile,
                ullTotalVirtual, ullAvailVirtual);

            return HashCode.Combine(h, ullAvailExtendedVirtual);
        }

        public static bool operator ==(MEMORYSTATUSEX left, MEMORYSTATUSEX right) => left.Equals(right);
        public static bool operator !=(MEMORYSTATUSEX left, MEMORYSTATUSEX right) => !left.Equals(right);
    }

    /// <summary>
    /// Calls the Windows API to retrieve memory status.
    /// </summary>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ---- Linux (/proc/meminfo) ----
    /// <summary>
    /// Reads system memory information from Linux.
    /// </summary>
    /// <param name="total">The total memory in bytes.</param>
    /// <param name="free">The free memory in bytes.</param>
    /// <param name="available">The available memory in bytes.</param>
    /// <param name="cached">The cached memory in bytes.</param>
    /// <param name="buffers">The buffers memory in bytes.</param>
    /// <returns>True if the information is successfully retrieved; otherwise, false.</returns>
    private static bool TryReadLinux(out long total, out long free, out long available, out long cached, out long buffers)
    {
        total = free = available = cached = buffers = 0;
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (TryParse(line, "MemTotal:", out var t)) total = t * 1024;
                else if (TryParse(line, "MemFree:", out var f)) free = f * 1024;
                else if (TryParse(line, "MemAvailable:", out var a)) available = a * 1024;
                else if (TryParse(line, "Cached:", out var c)) cached = c * 1024;
                else if (TryParse(line, "Buffers:", out var b)) buffers = b * 1024;
            }
            return total > 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (FormatException) { return false; }
        catch { throw; }
    }

    /// <summary>
    /// Attempts to parse a line from /proc/meminfo.
    /// </summary>
    /// <param name="line">The line to parse.</param>
    /// <param name="prefix">The expected prefix (e.g., "MemTotal:").</param>
    /// <param name="value">The parsed numeric value in kilobytes.</param>
    /// <returns>True if the line starts with the given prefix and the value was parsed; otherwise, false.</returns>
    private static bool TryParse(string line, string prefix, out long value)
    {
        ArgumentNullException.ThrowIfNull(line);

        value = 0;

        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return false;
        }

        return long.TryParse(parts[1], out value);
    }


    // ---- macOS (vm_stat / sysctl) ----
    /// <summary>
    /// Reads memory statistics from macOS.
    /// </summary>
    /// <param name="total">The total memory in bytes.</param>
    /// <param name="free">The free memory in bytes.</param>
    /// <param name="active">The active memory in bytes.</param>
    /// <param name="inactive">The inactive memory in bytes.</param>
    /// <param name="wired">The wired memory in bytes.</param>
    /// <returns>True if the information is successfully retrieved; otherwise, false.</returns>
    private static bool TryReadMac(out ulong total, out ulong free, out ulong active, out ulong inactive, out ulong wired)
    {
        total = free = active = inactive = wired = 0;
        try
        {
            if (!MacVmStat.TryPageSize(out var pageSize)) return false;
            if (!MacVmStat.TryVmStats(out var vm)) return false;
            if (!MacVmStat.TryHwMemSize(out var hwMem)) return false;

            total = hwMem;
            free = vm.free_count * pageSize;
            active = vm.active_count * pageSize;
            inactive = vm.inactive_count * pageSize;
            wired = vm.wire_count * pageSize;
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (IOException) { return false; }
        catch { throw; }
    }

    // ---- cgroup v2 helpers ----
    /// <summary>
    /// Reads memory information from cgroup v2 (Linux containers).
    /// </summary>
    /// <param name="limit">The memory limit in bytes.</param>
    /// <param name="usage">The memory usage in bytes.</param>
    /// <param name="swapLimit">The swap limit in bytes.</param>
    /// <param name="swapUsage">The swap usage in bytes.</param>
    /// <param name="limitUnlimited">Indicates if the limit is unlimited.</param>
    /// <param name="swapLimitUnlimited">Indicates if the swap limit is unlimited.</param>
    /// <returns>True if the information is successfully retrieved; otherwise, false.</returns>
    private static bool TryReadCgroupV2(out ulong limit, out ulong usage, out ulong swapLimit, out ulong swapUsage, out bool limitUnlimited, out bool swapLimitUnlimited)
    {
        limit = usage = swapLimit = swapUsage = 0;
        limitUnlimited = swapLimitUnlimited = false;

        try
        {
            const string root = "/sys/fs/cgroup";
            var limitPath = Path.Combine(root, "memory.max");
            var usagePath = Path.Combine(root, "memory.current");
            var swapLimitPath = Path.Combine(root, "memory.swap.max");
            var swapUsagePath = Path.Combine(root, "memory.swap.current");

            if (!File.Exists(limitPath) || !File.Exists(usagePath)) return false;

            (limitUnlimited, limit) = ReadUlongOrUnlimited(limitPath);
            usage = ReadUlong(usagePath);

            if (File.Exists(swapLimitPath) && File.Exists(swapUsagePath))
            {
                (swapLimitUnlimited, swapLimit) = ReadUlongOrUnlimited(swapLimitPath);
                swapUsage = ReadUlong(swapUsagePath);
            }
            else
            {
                swapLimitUnlimited = true;
                swapLimit = swapUsage = 0;
            }

            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (FormatException) { return false; }
        catch { throw; }

        static ulong ReadUlong(string path) =>
            ulong.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0UL;

        static (bool unlimited, ulong value) ReadUlongOrUnlimited(string path)
        {
            var s = File.ReadAllText(path).Trim();
            if (string.Equals(s, "max", StringComparison.OrdinalIgnoreCase)) return (true, 0UL);
            return (false, ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0UL);
        }
    }

    /// <summary>
    /// Creates a dictionary of tags for memory metrics.
    /// </summary>
    /// <param name="kind">The type of the metric (e.g., "total", "used", "free").</param>
    /// <param name="status">The status of the metric (e.g., "ok", "error").</param>
    /// <param name="os">The operating system (e.g., "Windows", "Linux", "macOS").</param>
    /// <returns>A <see cref="Dictionary{TKey, TValue}"/> containing the tags.</returns>
    private static Dictionary<string, string> Tag(string kind, string status, string os)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["status"] = status,
            ["os"] = os
        };
    }

    /// <summary>
    /// Adds an unsupported status for the specified operating system.
    /// </summary>
    /// <param name="mg">The multi-gauge metric.</param>
    /// <param name="os">The unsupported operating system.</param>
    private static void AddUnsupported(IMultiGauge mg, string os)
    {
        ArgumentNullException.ThrowIfNull(mg);
        ArgumentNullException.ThrowIfNull(os);

        mg.SetValue(0, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["status"] = "unsupported",
            ["os"] = os
        });
    }

    /// <summary>
    /// Shortens a string to 160 characters for logging purposes.
    /// </summary>
    /// <param name="s">The string to shorten.</param>
    /// <returns>The shortened string.</returns>
    private static string Short(string s)
    {
        return string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
    }

    // ---- Explicit IMetricCollector helper methods (factory shortcuts) ----

    /// <summary>
    /// Creates a summary metric for the system memory usage.
    /// </summary>
    /// <param name="id">The identifier for the summary metric.</param>
    /// <param name="name">The name for the summary metric.</param>
    /// <param name="quantiles">The quantiles for the summary (default: 0.5, 0.9, 0.99).</param>
    /// <param name="tags">Optional tags to associate with the summary metric.</param>
    /// <param name="resetOnGet">Indicates if the summary should reset when accessed (not used in this context).</param>
    /// <returns>A built summary metric.</returns>
    ISummaryMetric IMetricCollector.CreateSummary(string id, string name, IEnumerable<double> quantiles, IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
    {
        var q = quantiles?.ToArray() ?? new[] { 0.5, 0.9, 0.99 };
        var sb = _factory.Summary(id, name).WithQuantiles(q);

        if (tags != null)
        {
            foreach (var kv in tags)
            {
                sb.WithTag(kv.Key, kv.Value);
            }
        }

        return sb.Build();
    }

    /// <summary>
    /// Creates a bucket histogram metric for system memory usage.
    /// </summary>
    /// <param name="id">The identifier for the histogram metric.</param>
    /// <param name="name">The name for the histogram metric.</param>
    /// <param name="bucketUpperBounds">The upper bounds for the histogram buckets.</param>
    /// <param name="tags">Optional tags to associate with the histogram metric.</param>
    /// <returns>A built bucket histogram metric.</returns>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
    {
        var bounds = bucketUpperBounds?.ToArray() ?? Array.Empty<double>();
        var hb = _factory.Histogram(id, name).WithBounds(bounds);

        if (tags != null)
        {
            foreach (var kv in tags)
            {
                hb.WithTag(kv.Key, kv.Value);
            }
        }

        return hb.Build();
    }
}
