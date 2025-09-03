// <copyright file="SystemCpuUsageCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Reports total system CPU usage as a percentage using delta of cumulative OS counters.
/// </summary>
/// <remarks>
/// This class calculates the system-wide CPU usage as a percentage by using the delta of cumulative CPU time values
/// provided by the operating system. The formula for the CPU usage percentage is:
/// <para>percent = (Δbusy / Δtotal) * 100</para>
/// 
/// <para>Platform-specific behavior:</para>
/// <list type="bullet">
///     <item>Linux: Includes iowait by default (can be toggled with INCLUDE_IOWAIT flag)</item>
///     <item>Windows: Uses GetSystemTimes kernel, where busy = (kernel - idle) + user</item>
///     <item>macOS: Prefers host_statistics64, but falls back to sysctl kern.cp_time if needed</item>
/// </list>
/// 
/// The first call to <see cref="CollectAsync"/> will return an "empty" result because there is no prior snapshot to compare against.
/// </remarks>
public sealed class SystemCpuUsageCollector : IMetricCollector
{
    private const string Id = "cpu.system.percent";
    private const string Name = "System CPU Usage %";

    /// <inheritdoc />
    public static readonly double[] DefaultQuantiles = new[] { 0.5, 0.9, 0.99 };
    /// <inheritdoc />
    public static readonly double[] DefaultBucketBounds = Array.Empty<double>();

    /// <summary>
    /// Truncates the provided string to a maximum length of 160 characters.
    /// </summary>
    /// <param name="s">The string to be truncated.</param>
    /// <returns>A truncated string, or the original string if it is less than or equal to 160 characters in length.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the input string is null.</exception>
    private static string Short(string s)
    {
        if (s == null)
        {
            throw new ArgumentNullException(nameof(s), "Input string cannot be null.");
        }

        return s.Length <= 160 ? s : s[..160];
    }

    private readonly object _lock = new();

    private CpuTimes? _last;

    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemCpuUsageCollector"/> class.
    /// </summary>
    /// <param name="factory">The factory used to create metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown when the <paramref name="factory"/> is null.</exception>
    public SystemCpuUsageCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects the current system CPU usage as a percentage.
    /// </summary>
    /// <param name="ct">A cancellation token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, containing the collected metric.</returns>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var cur = ReadSystemTimes();

            bool haveDelta = false;

            double busyPercent = 0.0;

            lock (_lock)
            {
                if (_last is not null)
                {
                    var delta = cur - _last!;
                    var busy = delta.BusyTotal;
                    var total = delta.Total;

                    busyPercent = total > 0 ? (busy / total) * 100.0 : 0.0;
                    haveDelta = true;
                }
                _last = cur;
            }

            var g = _factory.Gauge(Id, Name).WithTag("status", haveDelta ? "ok" : "empty").Build();

            g.SetValue(Math.Clamp(busyPercent, 0.0, 100.0));

            return Task.FromResult<IMetric?>(g);
        }
        catch (OperationCanceledException)
        {
            var g = _factory.Gauge(Id, Name).WithTag("status", "cancelled").Build();

            g.SetValue(0);

            return Task.FromResult<IMetric?>(g);
        }
        catch (Exception ex)
        {
            var g = _factory.Gauge(Id, Name).WithTag("status", "error").WithTag("error", ex.GetType().Name).WithTag("reason", Short(ex.Message)).Build();

            g.SetValue(0);

            return Task.FromResult<IMetric?>(g);

            throw;
        }
    }

    // ---- Model ----
    /// <summary>
    /// Represents the CPU time values (idle and busy).
    /// </summary>
    private sealed class CpuTimes
    {
        /// <summary>
        /// Gets the amount of idle CPU time.
        /// </summary>
        public double Idle { get; }

        /// <summary>
        /// Gets the amount of busy CPU time.
        /// </summary>
        public double Busy { get; }

        /// <summary>
        /// Gets the total CPU time (Idle + Busy).
        /// </summary>
        public double Total => Idle + Busy;

        /// <summary>
        /// Gets the total busy CPU time.
        /// </summary>
        public double BusyTotal => Busy;

        /// <summary>
        /// Initializes a new instance of the <see cref="CpuTimes"/> class.
        /// </summary>
        /// <param name="idle">The idle CPU time.</param>
        /// <param name="busy">The busy CPU time.</param>
        public CpuTimes(double idle, double busy)
        {
            Idle = idle;
            Busy = busy;
        }

        /// <summary>
        /// Calculates the delta between two <see cref="CpuTimes"/> instances.
        /// </summary>
        /// <param name="a">The first <see cref="CpuTimes"/> instance.</param>
        /// <param name="b">The second <see cref="CpuTimes"/> instance.</param>
        /// <returns>A new <see cref="CpuTimes"/> instance representing the delta.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either <paramref name="a"/> or <paramref name="b"/> is null.</exception>
        public static CpuTimes operator -(CpuTimes a, CpuTimes b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a), "The first CpuTimes instance cannot be null.");
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b), "The second CpuTimes instance cannot be null.");
            }

            return new CpuTimes(Math.Max(0, a.Idle - b.Idle), Math.Max(0, a.Busy - b.Busy));
        }

        /// <summary>
        /// Subtracts the CPU times of the second instance from the first, returning the delta.
        /// </summary>
        /// <param name="a">The first <see cref="CpuTimes"/> instance.</param>
        /// <param name="b">The second <see cref="CpuTimes"/> instance.</param>
        /// <returns>A new <see cref="CpuTimes"/> instance representing the delta.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either <paramref name="a"/> or <paramref name="b"/> is null.</exception>
        public static CpuTimes Subtract(CpuTimes a, CpuTimes b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a), "The first CpuTimes instance cannot be null.");
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b), "The second CpuTimes instance cannot be null.");
            }

            return new CpuTimes(Math.Max(0, a.Idle - b.Idle), Math.Max(0, a.Busy - b.Busy));
        }

    }

    // ---- Dispatch ----
    /// <summary>
    /// Reads the system CPU times for the current operating system.
    /// </summary>
    /// <returns>The CPU times.</returns>
    private static CpuTimes ReadSystemTimes()
    {
        if (OperatingSystem.IsWindows())
        {
            return ReadWindows();
        }

        else if (OperatingSystem.IsLinux())
        {
            return ReadLinux();
        }

        else if (OperatingSystem.IsMacOS())
        {
            return ReadMac();
        }

        return new CpuTimes(0, 0);
    }

    // ---- Windows (GetSystemTimes) ----
    /// <summary>
    /// Reads the system CPU times on Windows using GetSystemTimes.
    /// </summary>
    /// <returns>The CPU times.</returns>
    private static CpuTimes ReadWindows()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return new CpuTimes(0, 0);
        }

        double idleD = ToDouble(idle);
        double kernelD = ToDouble(kernel);
        double userD = ToDouble(user);

        double busy = Math.Max(0, kernelD - idleD) + Math.Max(0, userD);

        return new CpuTimes(idleD, busy);
    }

    /// <summary>
    /// Converts a FILETIME structure to a double representing the time in 100-nanosecond intervals.
    /// </summary>
    /// <param name="ft">The <see cref="FILETIME"/> structure to be converted.</param>
    /// <returns>A double representing the time in 100-nanosecond intervals.</returns>
    private static double ToDouble(FILETIME ft)
    {
        // Calculate and return the value
        return ((ulong)(uint)ft.dwLowDateTime) + ((ulong)ft.dwHighDateTime << 32);
    }

    /// <summary>
    /// Specifies the DLL import search path behavior to prevent potential DLL hijacking.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // Specify search path explicitly
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME : IEquatable<FILETIME>
    {
        public int dwLowDateTime;
        public int dwHighDateTime;

        public override bool Equals(object? obj)
        {
            if (obj is FILETIME other)
            {
                return this.Equals(other);
            }
            return false;
        }

        public bool Equals(FILETIME other)
        {
            return this.dwLowDateTime == other.dwLowDateTime &&
                   this.dwHighDateTime == other.dwHighDateTime;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(dwLowDateTime, dwHighDateTime);
        }

        public static bool operator ==(FILETIME left, FILETIME right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(FILETIME left, FILETIME right)
        {
            return !(left == right);
        }
    }

    // ---- Linux (/proc/stat) ----
    /// <summary>
    /// Reads the system CPU times on Linux from /proc/stat.
    /// </summary>
    /// <returns>The CPU times.</returns>
    private static CpuTimes ReadLinux()
    {
        const bool INCLUDE_IOWAIT = true;

        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));

            if (line is null)
            {
                return new CpuTimes(0, 0);
            }

            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            double user = Parse(p, 1);
            double nice = Parse(p, 2);
            double system = Parse(p, 3);
            double idle = Parse(p, 4);
            double iowait = Parse(p, 5);
            double irq = Parse(p, 6);
            double softirq = Parse(p, 7);
            double steal = Parse(p, 8);

            double busy = user + nice + system + irq + softirq + steal + (INCLUDE_IOWAIT ? iowait : 0);
            return new CpuTimes(idle, busy);
        }
        catch
        {
            return new CpuTimes(0, 0);

            throw;
        }

        static double Parse(string[] parts, int idx)
        {
            return (idx < parts.Length && double.TryParse(parts[idx], NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) ? v : 0.0;
        }
    }

    // ---- macOS (host_statistics64 / sysctl kern.cp_time) ----
    /// <summary>
    /// Reads the system CPU times on macOS, preferring host_statistics64, then falling back to sysctl kern.cp_time.
    /// </summary>
    /// <returns>The CPU times.</returns>
    private static CpuTimes ReadMac()
    {
        // Prefer host_statistics64
        try
        {
            var cpus = new ulong[4];

            uint count = (uint)cpus.Length;

            // host_statistics64 returns [user, system, idle, nice] for HOST_CPU_LOAD_INFO
            if (host_statistics64(mach_host_self(), HOST_CPU_LOAD_INFO, cpus, ref count) == KERN_SUCCESS && count >= 4)
            {
                double user = cpus[0];
                double system = cpus[1];
                double idle = cpus[2];
                double nice = cpus[3];
                double busy = user + system + nice;

                return new CpuTimes(idle, busy);
            }
        }
        catch
        {
            throw;
        }

        // Fallback: sysctl kern.cp_time; commonly 4 or 5 longs (user, nice, sys, idle[, intr])
        try
        {
            if (TrySysctlCpuTime(out var u, out var s, out var i, out var n, out var intr, out var haveIntr))
            {
                double busy = (double)u + s + n;

                return new CpuTimes(i, busy);
            }
        }
        catch
        {
            throw;
        }

        return new CpuTimes(0, 0);
    }

    private const int KERN_SUCCESS = 0;
    private const int HOST_CPU_LOAD_INFO = 3;

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_statistics64(IntPtr host_priv, int flavor, [Out] ulong[] cpuLoadInfo, ref uint count);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr mach_host_self();

    private static bool TrySysctlCpuTime(out ulong user, out ulong system, out ulong idle, out ulong nice, out ulong intr, out bool haveIntr)
    {
        user = system = idle = nice = intr = 0;

        haveIntr = false;

        nuint size = 0;

        if (sysctlbyname("kern.cp_time", IntPtr.Zero, ref size, IntPtr.Zero, 0) != 0 || size == 0)
        {
            return false;
        }

        var buf = Marshal.AllocHGlobal((IntPtr)size);

        try
        {
            if (sysctlbyname("kern.cp_time", buf, ref size, IntPtr.Zero, 0) != 0)
            {
                return false;
            }

            int count = (int)(size / (nuint)sizeof(long));

            if (count < 4)
            {
                return false;
            }

            long lu = Marshal.ReadInt64(buf, 0 * sizeof(long)); // user
            long ln = Marshal.ReadInt64(buf, 1 * sizeof(long)); // nice
            long ls = Marshal.ReadInt64(buf, 2 * sizeof(long)); // system
            long li = Marshal.ReadInt64(buf, 3 * sizeof(long)); // idle
            long lintr = (count > 4) ? Marshal.ReadInt64(buf, 4 * sizeof(long)) : 0; // sometimes present

            user = (ulong)(lu < 0 ? 0 : lu);
            nice = (ulong)(ln < 0 ? 0 : ln);
            system = (ulong)(ls < 0 ? 0 : ls);
            idle = (ulong)(li < 0 ? 0 : li);
            intr = (ulong)(lintr < 0 ? 0 : lintr);
            haveIntr = count > 4;

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    [DllImport("/usr/lib/libSystem.dylib", SetLastError = true)]
    private static extern int sysctlbyname([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr oldp, ref nuint oldlenp, IntPtr newp, nuint newlen);

    // ---- IMetricCollector helper factory methods (explicit) ------------------

    ISummaryMetric IMetricCollector.CreateSummary(string id, string name, IEnumerable<double> quantiles, IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
    {
        var summary = _factory.Summary(id, name).WithQuantiles(quantiles?.ToArray() ?? DefaultQuantiles).Build();

        return summary;
    }

    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
    {
        var histogram = _factory.Histogram(id, name).WithBounds(bucketUpperBounds?.ToArray() ?? DefaultBucketBounds).Build();

        return histogram;
    }
}
