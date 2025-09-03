// <copyright file="CurrentProcessThreadCpuCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Collects per-thread CPU usage for the <em>current</em> process and exposes it
/// as a multi-gauge metric where each time series is tagged with the thread identifier.
/// </summary>
/// <remarks>
/// <para>
/// This collector walks <see cref="System.Diagnostics.Process.Threads"/> of the current process,
/// samples <see cref="System.Diagnostics.ProcessThread.TotalProcessorTime"/>, and computes the usage
/// percentage since the previous observation as:
/// <c>(ΔCPU / ΔWallTime) * 100</c>. Results are clamped to the range <c>[0, 100]</c>.
/// </para>
///
/// <para><strong>Scope</strong>: This is a <em>process-local</em> view. It does not enumerate or
/// report threads belonging to other processes.</para>
///
/// <para><strong>Resilience</strong>:
/// <list type="bullet">
/// <item><description>On the first run, threads have no prior sample and are skipped.</description></item>
/// <item><description>If the collector is cancelled via <see cref="System.Threading.CancellationToken"/>,
/// it emits a single time series tagged with <c>status="cancelled"</c>.</description></item>
/// <item><description>On unexpected errors, it emits a single time series tagged with
/// <c>status="error"</c>, the exception type, and a shortened reason.</description></item>
/// </list>
/// </para>
///
/// <para><strong>Thread-safety</strong>:
/// Internal state (the previous samples per thread) is protected by a private lock to allow
/// concurrent calls to <see cref="CollectAsync(System.Threading.CancellationToken)"/> from multiple threads.
/// Only one caller at a time computes and mutates internal caches.</para>
///
/// <para><strong>Performance</strong>:
/// Enumerating <see cref="System.Diagnostics.Process.Threads"/> is O(T) in the number of threads of
/// the current process and allocates transient collections for liveness cleanup.
/// The computation itself is proportional to the number of live threads observed.</para>
///
/// <para><strong>Cardinality</strong>:
/// Each emitted series is tagged with the thread identifier (<c>tid</c>). Avoid running this
/// collector at very high frequencies for processes with highly volatile thread counts to keep
/// time-series cardinality bounded.</para>
///
/// <example>
/// The following example registers and runs the collector and reads the emitted metric:
/// <code language="csharp"><![CDATA[
/// // IMetricFactory factory = ... obtained from your DI container
/// // ITimeProvider clock = new UtcTimeProvider();
/// 
/// var collector = new CurrentProcessThreadCpuCollector(factory, clock);
/// var metric = await collector.CollectAsync();
/// 
/// if (metric is IMultiGaugeMetric mg)
/// {
///     foreach (var series in mg.GetSeries())
///     {
///         // series.Value is CPU% for the thread, series.Tags["tid"] is the thread id
///         Console.WriteLine($"TID={series.Tags["tid"]}, CPU%={series.Value:0.00}, status={series.Tags.GetValueOrDefault("status","ok")}");
///     }
/// }
/// ]]></code>
/// </example>
///
/// <seealso cref="System.Diagnostics.Process"/>
/// <seealso cref="System.Diagnostics.ProcessThread"/>
/// <seealso cref="IMetricCollector"/>
/// </remarks>
public sealed class CurrentProcessThreadCpuCollector : IMetricCollector
{
    /// <summary>
    /// Default quantiles used when creating <see cref="ISummaryMetric"/> instances via the explicit
    /// <see cref="IMetricCollector.CreateSummary(string,string,System.Collections.Generic.IEnumerable{double},System.Collections.Generic.IReadOnlyDictionary{string,string}?,bool)"/> helper.
    /// </summary>
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    /// <summary>
    /// Default (empty) bucket upper bounds used when creating <see cref="IBucketHistogramMetric"/>
    /// instances via the explicit <see cref="IMetricCollector.CreateBucketHistogram(string,string,System.Collections.Generic.IEnumerable{double},System.Collections.Generic.IReadOnlyDictionary{string,string}?)"/> helper.
    /// </summary>
    private static readonly double[] DefaultBucketBounds = Array.Empty<double>();

    /// <summary>
    /// Synchronizes access to the internal <see cref="_last"/> cache and liveness cleanup.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// Stores the previous CPU sample and its timestamp for each thread id (TID).
    /// Used to compute deltas across consecutive collections.
    /// </summary>
    private readonly Dictionary<int, (TimeSpan cpu, DateTime ts)> _last = new();

    /// <summary>
    /// The factory used to build metrics (gauges, histograms, summaries) in a consistent manner.
    /// </summary>
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Provides the current UTC time. Defaults to <see cref="UtcTimeProvider"/>.
    /// </summary>
    private readonly ITimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="CurrentProcessThreadCpuCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to construct metric instances.</param>
    /// <param name="clock">
    /// Optional time provider used to obtain <see cref="DateTime.UtcNow"/> for interval calculations.
    /// When <see langword="null"/>, a new <see cref="UtcTimeProvider"/> is used.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public CurrentProcessThreadCpuCollector(IMetricFactory factory, ITimeProvider? clock = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _clock = clock ?? new UtcTimeProvider();
    }

    /// <summary>
    /// Collects per-thread CPU usage for the current process and returns a multi-gauge metric.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For each thread that has a prior observation in <see cref="_last"/>, the method computes the CPU%
    /// over the elapsed wall time since that prior point and emits a data point with tags:
    /// <list type="bullet">
    /// <item><description><c>tid</c>: OS thread id.</description></item>
    /// <item><description><c>status</c>: <c>"ok"</c> for successful samples; otherwise a collector-level status is emitted.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Threads seen for the first time are cached for the next cycle but do not immediately emit a data point,
    /// to avoid misleading spikes from an undefined baseline.
    /// </para>
    /// <para>
    /// After enumeration, stale entries in <see cref="_last"/> (threads that have exited) are removed to keep the cache bounded.
    /// </para>
    /// <para>
    /// If no thread produced a sample (for example, on the very first run), the metric contains a single series
    /// with <c>status="empty"</c> and value <c>0</c>.
    /// </para>
    /// </remarks>
    /// <param name="ct">A token used to observe cancellation during enumeration and computation.</param>
    /// <returns>
    /// A task that completes synchronously and yields the constructed <see cref="IMetric"/> instance
    /// (typically the multi-gauge created by the factory).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is signaled while collecting.</exception>
    public Task<IMetric?> CollectAsync(System.Threading.CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        // Builder -> Build()
        var multi = _factory.MultiGauge("cpu.thread.percent", "Per-Thread CPU Usage %").WithResetOnGet(true).Build();

        try
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();

            int added = 0;

            lock (_lock)
            {
                foreach (System.Diagnostics.ProcessThread t in p.Threads)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var cpu = t.TotalProcessorTime;
                        var ts = now;

                        if (_last.TryGetValue(t.Id, out var prev))
                        {
                            var dCpu = (cpu - prev.cpu).TotalSeconds;
                            var dWall = (ts - prev.ts).TotalSeconds;

                            double pct = dWall > 0 ? (dCpu / dWall) * 100.0 : 0.0;

                            multi.SetValue(
                                Math.Clamp(pct, 0, 100),
                                new Dictionary<string, string>
                                {
                                    ["tid"] = t.Id.ToString(),
                                    ["status"] = "ok"
                                });

                            added++;
                        }

                        _last[t.Id] = (cpu, ts);
                    }
                    catch
                    {
                        // Re-throw to be handled by the outer catch block where a single error series is emitted.
                        throw;
                    }
                }

                var live = new HashSet<int>(capacity: p.Threads.Count);

                foreach (System.Diagnostics.ProcessThread x in p.Threads)
                {
                    live.Add(x.Id);
                }

                var toRemove = new List<int>();

                foreach (var id in _last.Keys)
                {
                    if (!live.Contains(id))
                    {
                        toRemove.Add(id);
                    }
                }

                foreach (var id in toRemove)
                {
                    _last.Remove(id);
                }
            }

            if (added == 0)
            {
                multi.SetValue(0, new Dictionary<string, string> { ["status"] = "empty" });
            }

            return Task.FromResult<IMetric?>(multi);
        }
        catch (OperationCanceledException)
        {
            multi.SetValue(0, new Dictionary<string, string> { ["status"] = "cancelled" });

            return Task.FromResult<IMetric?>(multi);
        }
        catch (Exception ex)
        {
            multi.SetValue(0, new Dictionary<string, string>
            {
                ["status"] = "error",
                ["error"] = ex.GetType().Name,
                ["reason"] = Short(ex.Message)
            });

            return Task.FromResult<IMetric?>(multi);

            // Unreachable; the metric has already been returned with error metadata.
            throw;
        }

        static string Short(string s)
        {
            return s.Length <= 160 ? s : s[..160];
        }
    }

    // ---- IMetricCollector helper factory methods (explicit) ----

    /// <summary>
    /// Creates an <see cref="ISummaryMetric"/> using the provided factory and optional quantiles.
    /// </summary>
    /// <param name="id">The unique identifier for the summary metric.</param>
    /// <param name="name">A human-readable name for the summary metric.</param>
    /// <param name="quantiles">Quantiles to compute. If <see langword="null"/>, defaults to <c>0.5, 0.9, 0.99</c>.</param>
    /// <param name="tags">Optional constant tags associated with this metric.</param>
    /// <param name="resetOnGet">
    /// Ignored by this implementation; included to satisfy the interface and for forward compatibility.
    /// </param>
    /// <returns>The constructed <see cref="ISummaryMetric"/>.</returns>
    ISummaryMetric IMetricCollector.CreateSummary(
        string id,
        string name,
        IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags,
        bool resetOnGet)
    {
        return _factory.Summary(id, name).WithQuantiles(quantiles?.ToArray() ?? DefaultQuantiles).Build();
    }

    /// <summary>
    /// Creates an <see cref="IBucketHistogramMetric"/> using the provided factory and optional bucket bounds.
    /// </summary>
    /// <param name="id">The unique identifier for the histogram metric.</param>
    /// <param name="name">A human-readable name for the histogram metric.</param>
    /// <param name="bucketUpperBounds">
    /// The inclusive upper bounds for each histogram bucket (must be sorted ascending).
    /// If <see langword="null"/>, an empty set of bounds is used (implementation-defined behavior).
    /// </param>
    /// <param name="tags">Optional constant tags associated with this metric.</param>
    /// <returns>The constructed <see cref="IBucketHistogramMetric"/>.</returns>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id,
        string name,
        IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
    {
        return _factory.Histogram(id, name).WithBounds(bucketUpperBounds?.ToArray() ?? DefaultBucketBounds).Build();
    }
}
