// <copyright file="PerCoreCpuUsageCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

#pragma warning disable CA1031

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Collects per-core CPU usage percentages using a delta method.
/// </summary>
/// <remarks>
/// <para>
/// The collector reads cumulative CPU times per core from the operating system and computes usage on
/// each invocation by diffing the current snapshot against the previous one:
/// <c>Usage% = (Δbusy / Δtotal) × 100</c>.
/// </para>
/// <para>
/// Notes:
/// </para>
/// <list type="bullet">
///   <item><description>The first call returns an "empty" data point per core because there is no prior snapshot.</description></item>
///   <item><description>Linux source: <c>/proc/stat</c> (per-CPU lines, includes <c>iowait</c> in busy by default).</description></item>
///   <item><description>macOS source: <c>host_processor_info(PROCESSOR_CPU_LOAD_INFO)</c> over <c>libSystem</c>.</description></item>
///   <item><description>Windows source: <c>NtQuerySystemInformation(SystemProcessorPerformanceInformation)</c> from <c>ntdll.dll</c>.</description></item>
/// </list>
/// <para>
/// Emitted metric:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>cpu.core.percent</c> (Multi-Gauge):
///     <list type="bullet">
///       <item><description><b>value</b>: instantaneous usage percent per core (0–100).</description></item>
///       <item><description><b>tags</b>: <c>core</c> = zero-based core index, <c>status</c> = <c>ok</c>|<c>empty</c>|<c>unsupported</c>.</description></item>
///     </list>
///   </description></item>
/// </list>
/// <para>
/// Thread-safety: The collector is safe for concurrent <see cref="CollectAsync(CancellationToken)"/> calls.
/// Internal state transitions are protected by a private lock.
/// </para>
/// <para>
/// Performance: this is a lightweight operation; it performs O(N) work in the number of logical cores and
/// only touches minimal OS APIs and/or procfs files.
/// </para>
/// <example>
/// The following example shows how to register and call the collector in a background loop:
/// <code language="csharp"><![CDATA[
/// var factory = metricFactoryProvider.GetFactory();
/// var collector = new PerCoreCpuUsageCollector(factory);
///
/// using var cts = new CancellationTokenSource();
/// while (!cts.Token.IsCancellationRequested)
/// {
///     var metric = await collector.CollectAsync(cts.Token);
///     exporter.Export(metric); // Export your IMultiGauge instance
///     await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
/// }
/// ]]></code>
/// </example>
/// </remarks>
public sealed class PerCoreCpuUsageCollector : IMetricCollector
{
    /// <summary>
    /// Synchronizes access to the last snapshot and ensures consistent multi-threaded reads/writes.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// Holds the last per-core cumulative times; used to compute deltas on the next collection.
    /// </summary>
    private CpuCoreTimes[]? _last;

    /// <summary>
    /// Default quantiles used when creating <see cref="ISummaryMetric"/> instances via the explicit <see cref="IMetricCollector"/> helpers.
    /// </summary>
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    /// <summary>
    /// Default histogram bucket upper bounds used when creating <see cref="IBucketHistogramMetric"/> instances via the explicit <see cref="IMetricCollector"/> helpers.
    /// </summary>
    private static readonly double[] DefaultBucketUpperBounds = Array.Empty<double>();

    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PerCoreCpuUsageCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to build the multi-gauge and optional helper metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public PerCoreCpuUsageCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects per-core CPU usage and emits a multi-gauge with one sample per logical core.
    /// </summary>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result is an <see cref="IMetric"/> instance:
    /// a multi-gauge named <c>cpu.core.percent</c> (tags: <c>core</c>, <c>status</c>).
    /// </returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description>If the platform is not supported or the OS probes fail, a single sample with <c>status=unsupported</c> is emitted.</description></item>
    ///   <item><description>On the first successful read per process lifetime, one sample per core with <c>status=empty</c> is emitted.</description></item>
    ///   <item><description>Subsequent calls emit <c>status=ok</c> with usage values clamped to [0, 100].</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is canceled.</exception>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var cur = ReadPerCoreTimes(ct);

        // Builder -> Build() -> IMultiGauge
        var m = _factory.MultiGauge("cpu.core.percent", "Per-Core CPU Usage %").WithResetOnGet(true).Build();

        if (cur.Length == 0)
        {
            m.SetValue(0, new Dictionary<string, string> { ["status"] = "unsupported" });
            return Task.FromResult<IMetric?>(m);
        }

        lock (_lock)
        {
            if (_last is null || _last.Length != cur.Length)
            {
                _last = cur;

                for (int i = 0; i < cur.Length; i++)
                {
                    m.SetValue(0, new Dictionary<string, string>
                    {
                        ["core"] = i.ToString(CultureInfo.InvariantCulture),
                        ["status"] = "empty"
                    });
                }

                return Task.FromResult<IMetric?>(m);
            }

            for (int i = 0; i < cur.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var d = cur[i] - _last[i];

                double pct = d.Total > 0 ? Math.Clamp((d.Busy / d.Total) * 100.0, 0, 100) : 0.0;

                m.SetValue(pct, new Dictionary<string, string>
                {
                    ["core"] = i.ToString(CultureInfo.InvariantCulture),
                    ["status"] = "ok"
                });
            }

            _last = cur;
        }

        return Task.FromResult<IMetric?>(m);
    }

    /// <summary>
    /// Immutable value type that stores cumulative idle and busy times for a single CPU core.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Values are read as monotonically increasing counters from the OS. Differences between two snapshots
    /// represent elapsed time in arbitrary OS units (jiffies on Linux, ticks/time units on Windows/macOS).
    /// </para>
    /// <para>
    /// <see cref="Total"/> is computed as <c>Idle + Busy</c>. The struct supports subtraction to produce a delta snapshot.
    /// </para>
    /// </remarks>
    private readonly struct CpuCoreTimes : IEquatable<CpuCoreTimes>
    {
        /// <summary>
        /// Gets the cumulative idle time for the core.
        /// </summary>
        public double Idle { get; }

        /// <summary>
        /// Gets the cumulative non-idle (busy) time for the core.
        /// </summary>
        public double Busy { get; }

        /// <summary>
        /// Gets the cumulative total time (<c>Idle + Busy</c>).
        /// </summary>
        public double Total => Idle + Busy;

        /// <summary>
        /// Initializes a new instance of the <see cref="CpuCoreTimes"/> struct.
        /// </summary>
        /// <param name="idle">The cumulative idle time.</param>
        /// <param name="busy">The cumulative busy time.</param>
        public CpuCoreTimes(double idle, double busy)
        {
            Idle = idle;
            Busy = busy;
        }

        /// <inheritdoc/>
        public static bool operator ==(CpuCoreTimes left, CpuCoreTimes right) => left.Equals(right);

        /// <inheritdoc/>
        public static bool operator !=(CpuCoreTimes left, CpuCoreTimes right) => !(left == right);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is CpuCoreTimes other && Equals(other);

        /// <inheritdoc/>
        public bool Equals(CpuCoreTimes other) => Idle == other.Idle && Busy == other.Busy;

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Idle, Busy);

        /// <summary>
        /// Returns the non-negative difference between this and another snapshot.
        /// </summary>
        /// <param name="other">The earlier snapshot.</param>
        /// <returns>A new <see cref="CpuCoreTimes"/> representing deltas (clamped at zero).</returns>
        public CpuCoreTimes Subtract(CpuCoreTimes other)
            => new CpuCoreTimes(Math.Max(0, Idle - other.Idle), Math.Max(0, Busy - other.Busy));

        /// <summary>
        /// Subtracts two snapshots and clamps negative results to zero.
        /// </summary>
        /// <param name="a">The later snapshot.</param>
        /// <param name="b">The earlier snapshot.</param>
        /// <returns>The delta snapshot.</returns>
        public static CpuCoreTimes operator -(CpuCoreTimes a, CpuCoreTimes b) => a.Subtract(b);
    }

    /// <summary>
    /// Reads per-core cumulative times for the current OS.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An array of per-core cumulative times, or an empty array if unsupported.</returns>
    private static CpuCoreTimes[] ReadPerCoreTimes(CancellationToken ct)
    {
        if (OperatingSystem.IsLinux())
        {
            return ReadLinux(ct);
        }
        else if (OperatingSystem.IsMacOS())
        {
            return ReadMac(ct);
        }
        else if (OperatingSystem.IsWindows())
        {
            return ReadWindows(ct);
        }

        return Array.Empty<CpuCoreTimes>();
    }

    // ---- Linux (/proc/stat) -------------------------------------------------

    /// <summary>
    /// Reads per-core cumulative times from <c>/proc/stat</c> on Linux.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An array of per-core cumulative times, or an empty array on failure.</returns>
    /// <remarks>
    /// The implementation treats <c>iowait</c> as busy by default. To exclude <c>iowait</c>,
    /// set <c>INCLUDE_IOWAIT = false</c>.
    /// </remarks>
    private static CpuCoreTimes[] ReadLinux(CancellationToken ct)
    {
        const bool INCLUDE_IOWAIT = true;

        var list = new List<CpuCoreTimes>(capacity: Environment.ProcessorCount);

        try
        {
            foreach (var line in File.ReadLines("/proc/stat"))
            {
                ct.ThrowIfCancellationRequested();

                if (!line.StartsWith("cpu", StringComparison.Ordinal))
                {
                    continue;
                }

                // Skip the aggregate "cpu " line; keep "cpu0", "cpu1", ...
                if (line.StartsWith("cpu ", StringComparison.Ordinal))
                {
                    continue;
                }

                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // Fields: user nice system idle iowait irq softirq steal guest guest_nice ...
                double user = Parse(p[1]);
                double nice = Parse(p[2]);
                double system = Parse(p[3]);
                double idle = Parse(p[4]);
                double iowait = p.Length > 5 ? Parse(p[5]) : 0;
                double irq = p.Length > 6 ? Parse(p[6]) : 0;
                double softirq = p.Length > 7 ? Parse(p[7]) : 0;
                double steal = p.Length > 8 ? Parse(p[8]) : 0;

                double busy = user + nice + system + irq + softirq + steal + (INCLUDE_IOWAIT ? iowait : 0);

                list.Add(new CpuCoreTimes(idle, busy));
            }
        }
        catch
        {
            return Array.Empty<CpuCoreTimes>();
        }

        return list.ToArray();

        static double Parse(string s)
            => double.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    // ---- macOS (host_processor_info) ----------------------------------------

    /// <summary>
    /// Returns the host port for the current machine.
    /// </summary>
    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int mach_host_self();

    private const int PROCESSOR_CPU_LOAD_INFO = 2;

    /// <summary>
    /// Retrieves per-processor load information from the Mach host interface.
    /// </summary>
    /// <param name="host">The host port returned by <see cref="mach_host_self"/>.</param>
    /// <param name="flavor">The information flavor (use <see cref="PROCESSOR_CPU_LOAD_INFO"/>).</param>
    /// <param name="outProcessorCount">On return, the number of processors reported.</param>
    /// <param name="outProcessorInfo">On return, a pointer to a buffer containing per-CPU info.</param>
    /// <param name="outProcessorInfoCount">On return, the number of <c>int</c> entries in <paramref name="outProcessorInfo"/>.</param>
    /// <returns>Zero on success; non-zero kernel status on failure.</returns>
    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_processor_info(
        int host,
        int flavor,
        out int outProcessorCount,
        out IntPtr outProcessorInfo,
        out uint outProcessorInfoCount);

    /// <summary>
    /// Deallocates a region of virtual memory in the caller's task.
    /// </summary>
    /// <param name="task">The task whose memory is to be deallocated (use <see cref="mach_task_self"/>).</param>
    /// <param name="address">The address of the memory to deallocate.</param>
    /// <param name="size">The size of the memory to deallocate.</param>
    /// <returns>Zero on success; non-zero kernel status on failure.</returns>
    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int vm_deallocate(IntPtr task, IntPtr address, IntPtr size);

    /// <summary>
    /// Gets the port representing the current task (process).
    /// </summary>
    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr mach_task_self();

    /// <summary>
    /// Reads per-core cumulative times via Mach host interfaces on macOS.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An array of per-core cumulative times, or an empty array on failure.</returns>
    private static CpuCoreTimes[] ReadMac(CancellationToken ct)
    {
        IntPtr info = IntPtr.Zero;
        int infoCount = 0;

        try
        {
            int host = mach_host_self();

            if (host == 0)
            {
                return Array.Empty<CpuCoreTimes>();
            }

            var kr = host_processor_info(host, PROCESSOR_CPU_LOAD_INFO, out int count, out info, out uint outInfoCount);

            if (kr != 0 || info == IntPtr.Zero || count <= 0)
            {
                return Array.Empty<CpuCoreTimes>();
            }

            infoCount = (int)outInfoCount;

            var arr = new CpuCoreTimes[count];

            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();

                // Each CPU has 4 ints: user, nice, system, idle
                int baseOff = (i * 4) * sizeof(int);

                uint user = (uint)Marshal.ReadInt32(info, baseOff + 0 * sizeof(int));
                uint nice = (uint)Marshal.ReadInt32(info, baseOff + 1 * sizeof(int));
                uint system = (uint)Marshal.ReadInt32(info, baseOff + 2 * sizeof(int));
                uint idle = (uint)Marshal.ReadInt32(info, baseOff + 3 * sizeof(int));

                double busy = (double)user + nice + system;

                arr[i] = new CpuCoreTimes(idle, busy);
            }

            return arr;
        }
        catch
        {
            return Array.Empty<CpuCoreTimes>();
        }
        finally
        {
            if (info != IntPtr.Zero)
            {
                // outInfoCount is a count of ints; convert to bytes for deallocation size
                int deallocateStatus = vm_deallocate(mach_task_self(), info, (IntPtr)(infoCount * sizeof(uint)));

                if (deallocateStatus != 0)
                {
                    // non-throwing best-effort cleanup
                    Console.WriteLine($"vm_deallocate failed with error code: {deallocateStatus}");
                }
            }
        }
    }

    // ---- Windows (NtQuerySystemInformation) ----------------------------------

    /// <summary>
    /// Identifies the type of system information to query via <see cref="NtQuerySystemInformation(SYSTEM_INFORMATION_CLASS, IntPtr, int, out int)"/>.
    /// </summary>
    [Flags]
    private enum SYSTEM_INFORMATION_CLASS : int
    {
        /// <summary>
        /// Retrieves an array of <see cref="SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION"/> structures, one per logical processor.
        /// </summary>
        SystemProcessorPerformanceInformation = 8,

        /// <summary>
        /// No information type (placeholder).
        /// </summary>
        None = 0
    }

    /// <summary>
    /// Represents per-processor performance counters returned by the Windows kernel.
    /// </summary>
    /// <remarks>
    /// Times are cumulative. <see cref="KernelTime"/> includes <see cref="IdleTime"/> according to the native API contract.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION : IEquatable<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>
    {
        /// <summary>Gets or sets the cumulative idle time.</summary>
        public long IdleTime;

        /// <summary>Gets or sets the cumulative privileged (kernel) time (includes idle).</summary>
        public long KernelTime;

        /// <summary>Gets or sets the cumulative user mode time.</summary>
        public long UserTime;

        /// <summary>Gets or sets the cumulative DPC time.</summary>
        public long DpcTime;

        /// <summary>Gets or sets the cumulative interrupt service time.</summary>
        public long InterruptTime;

        /// <summary>Gets or sets the number of interrupts.</summary>
        public uint InterruptCount;

        /// <inheritdoc/>
        public static bool operator ==(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION left, SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION right) => left.Equals(right);

        /// <inheritdoc/>
        public static bool operator !=(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION left, SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION right) => !(left == right);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION other && Equals(other);

        /// <inheritdoc/>
        public bool Equals(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION other)
            => IdleTime == other.IdleTime
               && KernelTime == other.KernelTime
               && UserTime == other.UserTime
               && DpcTime == other.DpcTime
               && InterruptTime == other.InterruptTime
               && InterruptCount == other.InterruptCount;

        /// <inheritdoc/>
        public override int GetHashCode()
            => HashCode.Combine(IdleTime, KernelTime, UserTime, DpcTime, InterruptTime, InterruptCount);
    }

    /// <summary>
    /// Queries variable-length system information from the Windows kernel.
    /// </summary>
    /// <param name="klass">The class of system information to query.</param>
    /// <param name="buffer">A pointer to a caller-allocated buffer that receives the information.</param>
    /// <param name="length">The length, in bytes, of the buffer pointed to by <paramref name="buffer"/>.</param>
    /// <param name="returnLength">On return, the number of bytes written to <paramref name="buffer"/>.</param>
    /// <returns>Zero (STATUS_SUCCESS) on success; non-zero NTSTATUS on failure.</returns>
    [DllImport("ntdll.dll", SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false, ThrowOnUnmappableChar = true, EntryPoint = "NtQuerySystemInformation")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtQuerySystemInformation(SYSTEM_INFORMATION_CLASS klass, IntPtr buffer, int length, out int returnLength);

    /// <summary>
    /// Reads per-core cumulative times via <c>NtQuerySystemInformation</c> on Windows.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An array of per-core cumulative times, or an empty array on failure.</returns>
    private static CpuCoreTimes[] ReadWindows(CancellationToken ct)
    {
        IntPtr buf = IntPtr.Zero;

        try
        {
            int cpuCount = Environment.ProcessorCount;
            int elemSize = Marshal.SizeOf<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
            int sz = elemSize * cpuCount;

            buf = Marshal.AllocHGlobal(sz);

            int status = NtQuerySystemInformation(SYSTEM_INFORMATION_CLASS.SystemProcessorPerformanceInformation, buf, sz, out int ret);

            if (status != 0 || ret < elemSize)
            {
                return Array.Empty<CpuCoreTimes>();
            }

            var arr = new CpuCoreTimes[cpuCount];

            for (int i = 0; i < cpuCount; i++)
            {
                ct.ThrowIfCancellationRequested();

                var item = Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(buf + i * elemSize);

                double idle = item.IdleTime;
                double k = item.KernelTime;
                double user = item.UserTime;

                // KernelTime includes IdleTime. Remove idle to get kernel non-idle.
                double kernelNonIdle = k >= idle ? (k - idle) : k;
                double busy = Math.Max(0, kernelNonIdle) + Math.Max(0, user);

                arr[i] = new CpuCoreTimes(idle, busy);
            }

            return arr;
        }
        catch
        {
            return Array.Empty<CpuCoreTimes>();
        }
        finally
        {
            if (buf != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buf);
            }
        }
    }

    // ---- IMetricCollector helper factory methods (explicit) ------------------

    /// <inheritdoc />
    ISummaryMetric IMetricCollector.CreateSummary(
        string id,
        string name,
        IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags,
        bool resetOnGet)
    {
        var summary = _factory.Summary(id, name)
                              .WithQuantiles(quantiles?.ToArray() ?? DefaultQuantiles)
                              .Build();

        return summary;
    }

    /// <inheritdoc />
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id,
        string name,
        IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
    {
        var histogram = _factory.Histogram(id, name)
                                .WithBounds(bucketUpperBounds?.ToArray() ?? DefaultBucketUpperBounds)
                                .Build();

        return histogram;
    }
}
