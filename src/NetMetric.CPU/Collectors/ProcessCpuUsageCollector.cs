// <copyright file="ProcessCpuUsageCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Collects the CPU usage percentage of the <em>current process</em>, normalized by the number of logical CPUs.
/// </summary>
/// <remarks>
/// <para>
/// The collector produces a single <c>Gauge</c> metric with the identifier <c>cpu.process.percent</c> and name
/// <c>"Process CPU Usage %"</c>. The value is computed using a delta method between consecutive samples:
/// </para>
/// <para>
/// <c>percent = (Δprocess_cpu_seconds / (Δwall_seconds × cpu_count)) × 100</c>
/// </para>
/// <para>
/// Where:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>Δprocess_cpu_seconds</c> = change in <see cref="Process.TotalProcessorTime"/> for the current process.
/// </description></item>
/// <item><description>
/// <c>Δwall_seconds</c> = elapsed wall-clock time measured from <see cref="Stopwatch.GetTimestamp"/>.
/// A monotonic clock is used to remain robust against system time adjustments.
/// </description></item>
/// <item><description>
/// <c>cpu_count</c> = <see cref="Environment.ProcessorCount"/>, i.e., logical processors.
/// </description></item>
/// </list>
/// <para>
/// The first observation will be reported with a <c>status=empty</c> tag because there is no previous
/// sample to diff against. If the sampling window is shorter than <see cref="MinWindowSeconds"/>,
/// the sample is also reported as empty to avoid jitter.
/// </para>
/// <para>
/// <strong>Metric schema</strong>
/// </para>
/// <list type="table">
/// <listheader>
/// <term>Field</term><description>Description</description>
/// </listheader>
/// <item><term>id</term><description><c>cpu.process.percent</c></description></item>
/// <item><term>name</term><description><c>Process CPU Usage %</c></description></item>
/// <item><term>type</term><description>Gauge</description></item>
/// <item><term>value</term><description>Double in range [0, 100]</description></item>
/// <item><term>tag:status</term><description><c>ok</c> | <c>empty</c> | <c>cancelled</c> | <c>error</c></description></item>
/// <item><term>tag:error</term><description>Exception type name (only when <c>status=error</c>)</description></item>
/// <item><term>tag:reason</term><description>Shortened exception message (only when <c>status=error</c>)</description></item>
/// </list>
/// <para>
/// <strong>Thread safety:</strong> This type is safe for concurrent use. A private lock protects the internal
/// delta state (<see cref="_lastCpu"/> and <see cref="_lastTicks"/>). All other members are read-only.
/// </para>
/// <para>
/// <strong>Performance:</strong> The collector is lightweight: it queries the current process once per collection
/// cycle and performs constant-time arithmetic. To minimize jitter, prefer a collection interval ≥ 250ms.
/// </para>
/// <para>
/// <strong>Platform support:</strong> Works on all platforms supported by .NET (Windows, Linux, macOS).
/// </para>
/// <example>
/// <code language="csharp"><![CDATA[
/// // 1) Manual creation and collection
/// IMetricFactory factory = GetMetricFactory();
/// var collector = new ProcessCpuUsageCollector(factory);
/// var metric = await collector.CollectAsync();
/// if (metric is IGaugeMetric g) {
///     Console.WriteLine($"CPU% = {g.Value}");
///     // Tags include: status=ok|empty|cancelled|error
/// }
///
/// // 2) Registering with a scheduler (pseudo-code)
/// scheduler.Every(TimeSpan.FromSeconds(1)).Run(async ct =>
/// {
///     await collector.CollectAsync(ct);
/// });
/// ]]></code>
/// </example>
/// <seealso cref="System.Diagnostics.Process"/>
/// <seealso cref="System.Diagnostics.Stopwatch"/>
/// <seealso cref="Environment.ProcessorCount"/>
/// </remarks>
public sealed class ProcessCpuUsageCollector : IMetricCollector
{
    /// <summary>
    /// The stable metric identifier: <c>cpu.process.percent</c>.
    /// </summary>
    private const string Id = "cpu.process.percent";

    /// <summary>
    /// The human-readable metric name: <c>Process CPU Usage %</c>.
    /// </summary>
    private const string Name = "Process CPU Usage %";

    /// <summary>
    /// Default quantiles used when creating <see cref="ISummaryMetric"/> instances via
    /// <see cref="CreateSummary(string, string, System.Collections.Generic.IEnumerable{double}, System.Collections.Generic.IReadOnlyDictionary{string, string}?, bool)"/>.
    /// </summary>
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    /// <summary>
    /// Default histogram bucket upper bounds used when creating <see cref="IBucketHistogramMetric"/> instances via
    /// <see cref="CreateBucketHistogram(string, string, System.Collections.Generic.IEnumerable{double}, System.Collections.Generic.IReadOnlyDictionary{string, string}?)"/>.
    /// </summary>
    private static readonly double[] DefaultBounds = Array.Empty<double>();

    /// <summary>
    /// Returns a truncated version of <paramref name="s"/>, capped at 160 characters.
    /// Intended for shortening exception messages stored in tags.
    /// </summary>
    /// <param name="s">The input string.</param>
    /// <returns>The original string if ≤ 160 characters; otherwise a substring of length 160.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="s"/> is <see langword="null"/>.</exception>
    private static string Short(string s)
    {
        if (s == null)
        {
            throw new ArgumentNullException(nameof(s), "Input string cannot be null.");
        }

        return s.Length <= 160 ? s : s[..160];
    }

    /// <summary>
    /// The minimum sampling window (seconds) required to compute a stable percentage.
    /// Samples shorter than this threshold are reported as <c>status=empty</c>.
    /// </summary>
    private const double MinWindowSeconds = 0.10; // 100 ms

    /// <summary>
    /// Conversion factor from stopwatch ticks to seconds: <c>1.0 / Stopwatch.Frequency</c>.
    /// </summary>
    private static readonly double TickToSeconds = 1.0 / Stopwatch.Frequency;

    /// <summary>
    /// Synchronization object protecting <see cref="_lastCpu"/> and <see cref="_lastTicks"/>.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// The last observed <see cref="Process.TotalProcessorTime"/> for the current process.
    /// Zero indicates that no previous sample exists.
    /// </summary>
    private TimeSpan _lastCpu = TimeSpan.Zero;

    /// <summary>
    /// The <see cref="Stopwatch.GetTimestamp"/> value captured at the previous sample.
    /// </summary>
    private long _lastTicks;

    /// <summary>
    /// The number of logical processors on the machine. Used to normalize CPU usage to a 0–100% scale.
    /// </summary>
    private readonly int _cpuCount = Environment.ProcessorCount;

    /// <summary>
    /// Factory used to build metric instances exposed by this collector.
    /// </summary>
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Time provider used for wall time and scheduling concerns. Defaults to <see cref="UtcTimeProvider"/>.
    /// </summary>
    private readonly ITimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessCpuUsageCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create the output <c>Gauge</c> metric.</param>
    /// <param name="clock">Optional time provider. If <see langword="null"/>, <see cref="UtcTimeProvider"/> is used.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public ProcessCpuUsageCollector(IMetricFactory factory, ITimeProvider? clock = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _clock = clock ?? new UtcTimeProvider();
        _lastTicks = Stopwatch.GetTimestamp(); // Set the monotonic baseline.
    }

#pragma warning disable CA1031
    /// <summary>
    /// Computes and emits the current process CPU usage percentage as a normalized <c>Gauge</c> metric.
    /// </summary>
    /// <param name="ct">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A task that completes with the constructed metric instance. The metric always exists; the <c>status</c> tag
    /// communicates whether a value was computed (<c>ok</c>) or the sample was skipped (<c>empty</c> / <c>cancelled</c> / <c>error</c>).
    /// </returns>
    /// <remarks>
    /// <para>
    /// The resulting gauge value is clamped to the inclusive range <c>[0, 100]</c> to account for rounding errors
    /// and rare scheduler anomalies.
    /// </para>
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            using var proc = Process.GetCurrentProcess();

            var totalCpu = proc.TotalProcessorTime;

            double percent = 0.0;
            bool haveValue;

            lock (_lock)
            {
                var nowTicks = Stopwatch.GetTimestamp();
                var wallSec = (nowTicks - _lastTicks) * TickToSeconds;

                // If the window is too small or this is the first sample, return an empty value
                if (_lastCpu == TimeSpan.Zero || wallSec < MinWindowSeconds)
                {
                    _lastCpu = totalCpu;
                    _lastTicks = nowTicks;

                    haveValue = false; // Empty value for this sample
                }
                else
                {
                    var cpuDeltaSec = (totalCpu - _lastCpu).TotalSeconds;
                    var denom = wallSec * _cpuCount;

                    percent = denom > 0 ? (cpuDeltaSec / denom) * 100.0 : 0.0;

                    _lastCpu = totalCpu;
                    _lastTicks = nowTicks;

                    haveValue = true;
                }
            }

            // Build the metric and set the appropriate tags based on success or failure
            var g = _factory.Gauge(Id, Name).WithTag("status", haveValue ? "ok" : "empty").Build();

            g.SetValue(Math.Clamp(percent, 0.0, 100.0));

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
            var g = _factory
                .Gauge(Id, Name)
                .WithTag("status", "error")
                .WithTag("error", ex.GetType().Name)
                .WithTag("reason", Short(ex.Message))
                .Build();

            g.SetValue(0);

            return Task.FromResult<IMetric?>(g);

            // Note: the trailing 'throw' is intentionally unreachable as the metric is already reported.
        }
    }
#pragma warning restore CA1031

    // ---- IMetricCollector helper factory methods (explicit) ----

    /// <summary>
    /// Creates a <see cref="ISummaryMetric"/> with the specified quantiles.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The metric display name.</param>
    /// <param name="quantiles">The set of quantiles to compute (defaults to 0.5, 0.9, 0.99 when <see langword="null"/>).</param>
    /// <param name="tags">Optional key/value tags to associate with the metric.</param>
    /// <param name="resetOnGet">Whether to reset the summary on each read (behavior may depend on the implementation).</param>
    /// <returns>An initialized <see cref="ISummaryMetric"/>.</returns>
    /// <remarks>
    /// This helper delegates to <see cref="IMetricFactory.Summary(string, string)"/> and applies defaults when
    /// <paramref name="quantiles"/> is <see langword="null"/>.
    /// </remarks>
    public ISummaryMetric CreateSummary(string id, string name, IEnumerable<double> quantiles, IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
    {
        // Returning the summary metric built from the factory
        return _factory.Summary(id, name).WithQuantiles(quantiles?.ToArray() ?? DefaultQuantiles).Build();
    }

    /// <summary>
    /// Creates a bucketed <see cref="IBucketHistogramMetric"/> using the provided bucket upper bounds.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The metric display name.</param>
    /// <param name="bucketUpperBounds">The inclusive upper bounds for each histogram bucket (defaults to empty when <see langword="null"/>).</param>
    /// <param name="tags">Optional key/value tags to associate with the metric.</param>
    /// <returns>An initialized <see cref="IBucketHistogramMetric"/>.</returns>
    /// <remarks>
    /// This helper delegates to <see cref="IMetricFactory.Histogram(string, string)"/> and applies defaults when
    /// <paramref name="bucketUpperBounds"/> is <see langword="null"/>.
    /// </remarks>
    public IBucketHistogramMetric CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
    {
        // Returning the bucket histogram metric built from the factory
        return _factory.Histogram(id, name).WithBounds(bucketUpperBounds?.ToArray() ?? DefaultBounds).Build();
    }
}
