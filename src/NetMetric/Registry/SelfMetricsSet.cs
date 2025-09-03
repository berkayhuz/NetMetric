// <copyright file="SelfMetricsSet.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Registry;

/// <summary>
/// A set of built-in metrics that track the internal health and performance of NetMetric itself.  
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> Provides “self-observability” for the metrics pipeline, measuring:
/// <list type="bullet">
///   <item><description>Collection success/error counts</description></item>
///   <item><description>Export success/error counts</description></item>
///   <item><description>Collection latency distribution</description></item>
///   <item><description>Export latency distribution</description></item>
/// </list>
/// </para>
/// <para>
/// Metrics are created with a configurable prefix (<see cref="MetricOptions.SelfMetricsPrefix"/>),
/// defaulting to <c>"netmetric"</c>. This makes them easy to identify in exported systems.
/// </para>
/// <para>
/// Histograms for durations are bounded between <c>1 ms</c> and <c>10 s</c> on a log-ish scale.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Register self-metrics
/// var set = new SelfMetricsSet(factory, options);
///
/// // Record a collection scope
/// using (var scope = set.StartCollect())
/// {
///     try
///     {
///         CollectData();
///         scope.Ok(); // increments collects.ok and observes duration
///     }
///     catch
///     {
///         scope.Error(); // increments collects.error and observes duration
///     }
/// }
///
/// // Record an export scope
/// using (var scope = set.StartExport())
/// {
///     try
///     {
///         ExportData();
///         scope.Ok(); // increments exports.ok
///     }
///     catch
///     {
///         scope.Error(); // increments exports.error
///     }
/// }
/// </code>
/// </example>
internal sealed class SelfMetricsSet
{
    private readonly IMetricFactory _factory;
    private readonly string _prefix;

    private readonly ICounterMetric _collectsOk;
    private readonly ICounterMetric _collectsErr;
    private readonly ICounterMetric _exportsOk;
    private readonly ICounterMetric _exportsErr;
    private readonly IBucketHistogramMetric _collectDuration;
    private readonly IBucketHistogramMetric _exportDuration;

    /// <summary>
    /// Initializes a new <see cref="SelfMetricsSet"/> with built-in counters and histograms.
    /// </summary>
    /// <param name="factory">Factory used to create metrics.</param>
    /// <param name="opts">Options containing <see cref="MetricOptions.SelfMetricsPrefix"/> and other defaults.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="opts"/> is null.</exception>
    public SelfMetricsSet(IMetricFactory factory, MetricOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);

        _factory = factory;
        _prefix = string.IsNullOrWhiteSpace(opts.SelfMetricsPrefix) ? "netmetric" : opts.SelfMetricsPrefix!;

        // Bounds: 1 ms → 10 s
        var bounds = new double[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 };

        _collectsOk = _factory.Counter($"{_prefix}.collects.ok", "NetMetric Collects OK").Build();
        _collectsErr = _factory.Counter($"{_prefix}.collects.error", "NetMetric Collects Error").Build();
        _exportsOk = _factory.Counter($"{_prefix}.exports.ok", "NetMetric Exports OK").Build();
        _exportsErr = _factory.Counter($"{_prefix}.exports.error", "NetMetric Exports Error").Build();

        _collectDuration = _factory.Histogram($"{_prefix}.collect.duration", "NetMetric Collect Duration (ms)")
                                   .WithUnit("ms").WithBounds(bounds).Build();

        _exportDuration = _factory.Histogram($"{_prefix}.export.duration", "NetMetric Export Duration (ms)")
                                   .WithUnit("ms").WithBounds(bounds).Build();
    }

    /// <summary>
    /// Starts a new collection scope to track duration and outcome of a collection operation.
    /// </summary>
    /// <returns>A disposable <see cref="CollectScope"/>.</returns>
    public CollectScope StartCollect() => new(this);

    /// <summary>
    /// Starts a new export scope to track duration and outcome of an export operation.
    /// </summary>
    /// <returns>A disposable <see cref="ExportScope"/>.</returns>
    public ExportScope StartExport() => new(this);

    /// <summary>
    /// Disposable scope for measuring and recording a collection operation.
    /// </summary>
    public readonly struct CollectScope : IDisposable, IEquatable<CollectScope>
    {
        private readonly SelfMetricsSet _set;
        private readonly long _ts;

        /// <summary>
        /// Initializes a new <see cref="CollectScope"/> bound to the given <see cref="SelfMetricsSet"/>.
        /// </summary>
        public CollectScope(SelfMetricsSet set)
        {
            _set = set;
            _ts = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Marks the scope as successful, increments the <c>collects.ok</c> counter,
        /// and records the elapsed duration.
        /// </summary>
        public void Ok()
        {
            var ms = (Stopwatch.GetTimestamp() - _ts) * 1000.0 / Stopwatch.Frequency;
            _set._collectDuration.Observe(ms);
            _set._collectsOk.Increment();
        }

        /// <summary>
        /// Marks the scope as failed, increments the <c>collects.error</c> counter,
        /// and records the elapsed duration.
        /// </summary>
        public void Error()
        {
            var ms = (Stopwatch.GetTimestamp() - _ts) * 1000.0 / Stopwatch.Frequency;
            _set._collectDuration.Observe(ms);
            _set._collectsErr.Increment();
        }

        /// <inheritdoc/>
        public void Dispose() { }

        // Equality members
        public bool Equals(CollectScope other) => ReferenceEquals(_set, other._set) && _ts == other._ts;
        public override bool Equals(object? obj) => obj is CollectScope other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_set, _ts);
        public static bool operator ==(CollectScope left, CollectScope right) => left.Equals(right);
        public static bool operator !=(CollectScope left, CollectScope right) => !left.Equals(right);
    }

    /// <summary>
    /// Disposable scope for measuring and recording an export operation.
    /// </summary>
    public readonly struct ExportScope : IDisposable, IEquatable<ExportScope>
    {
        private readonly SelfMetricsSet _set;
        private readonly long _ts;

        /// <summary>
        /// Initializes a new <see cref="ExportScope"/> bound to the given <see cref="SelfMetricsSet"/>.
        /// </summary>
        public ExportScope(SelfMetricsSet set)
        {
            _set = set;
            _ts = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Marks the scope as successful, increments the <c>exports.ok</c> counter,
        /// and records the elapsed duration.
        /// </summary>
        public void Ok()
        {
            var ms = (Stopwatch.GetTimestamp() - _ts) * 1000.0 / Stopwatch.Frequency;
            _set._exportDuration.Observe(ms);
            _set._exportsOk.Increment();
        }

        /// <summary>
        /// Marks the scope as failed, increments the <c>exports.error</c> counter,
        /// and records the elapsed duration.
        /// </summary>
        public void Error()
        {
            var ms = (Stopwatch.GetTimestamp() - _ts) * 1000.0 / Stopwatch.Frequency;
            _set._exportDuration.Observe(ms);
            _set._exportsErr.Increment();
        }

        /// <inheritdoc/>
        public void Dispose() { }

        // Equality members
        public bool Equals(ExportScope other) => ReferenceEquals(_set, other._set) && _ts == other._ts;
        public override bool Equals(object? obj) => obj is ExportScope other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_set, _ts);
        public static bool operator ==(ExportScope left, ExportScope right) => left.Equals(right);
        public static bool operator !=(ExportScope left, ExportScope right) => !left.Equals(right);
    }
}
