// <copyright file="CertScanLastScanCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Collectors;

/// <summary>
/// Emits a snapshot gauge metric that records the last time a certificate scan was performed,
/// represented as Unix time (seconds since the Unix epoch, UTC).
/// </summary>
/// <remarks>
/// <para>
/// This collector triggers a lightweight scan via <see cref="CertificateAggregator"/> to ensure
/// the "last scan" timestamp reflects the current collection cycle, then returns a single-value
/// gauge metric with the Unix timestamp captured on the collector side (<see cref="DateTimeOffset.UtcNow"/>).
/// </para>
/// <para>
/// The resulting metric is a <em>point-in-time</em> snapshot, not a cumulative counter. Use it to
/// validate scheduler health (e.g., "has scanning run recently?") or to annotate dashboards with
/// the time of the most recent scan.
/// </para>
/// <para>
/// Thread-safety: this type is stateless except for transient local variables during collection,
/// and is safe to use concurrently from multiple threads provided the underlying
/// <see cref="CertificateAggregator"/> and <see cref="IMetricFactory"/> implementations are thread-safe.
/// </para>
/// </remarks>
/// <example>
/// <para>Registering the collector in a module or composition root:</para>
/// <code language="csharp"><![CDATA[
/// var aggregator = new CertificateAggregator(sources, options);
/// var collector  = new CertScanLastScanCollector(aggregator, metricFactory);
///
/// // On each scrape/collection cycle:
/// var metric = await collector.CollectAsync(ct);
/// // Exporters will observe a single gauge value such as:
/// // nm.cert.scan.last_scan_unix_seconds{...} 1725252662
/// ]]></code>
/// </example>
/// <seealso cref="CertificateAggregator"/>
/// <seealso cref="CertificatesModule"/>
public sealed class CertScanLastScanCollector : IMetricCollector
{
    /// <summary>
    /// Default quantiles used when composing summary metrics via <see cref="IMetricCollector.CreateSummary"/>.
    /// </summary>
    private static readonly double[] DefaultSummaryQuantiles = new[] { 0.5, 0.9, 0.99 };

    private readonly CertificateAggregator _agg;
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertScanLastScanCollector"/> class.
    /// </summary>
    /// <param name="aggregator">Certificate data aggregator used to trigger a scan snapshot.</param>
    /// <param name="factory">Metric factory for creating fallback instruments when required.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="aggregator"/> or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    public CertScanLastScanCollector(CertificateAggregator aggregator, IMetricFactory factory)
    {
        _agg = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Executes (or reuses) a certificate scan through the shared <see cref="CertificateAggregator"/>,
    /// then returns a one-shot gauge metric containing the current UTC Unix time in seconds.
    /// </summary>
    /// <param name="ct">A cancellation token to observe while awaiting the snapshot.</param>
    /// <returns>
    /// A gauge metric with:
    /// <list type="bullet">
    ///   <item><description><c>id</c>: <c>nm.cert.scan.last_scan_unix_seconds</c></description></item>
    ///   <item><description><c>name</c>: <c>Last scan time (Unix seconds)</c></description></item>
    ///   <item><description><c>value</c>: <c>DateTimeOffset.UtcNow.ToUnixTimeSeconds()</c></description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// The aggregator may serve a cached snapshot depending on its configured TTL; the timestamp
    /// emitted by this collector always reflects the <em>collector invocation time</em>.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is canceled.</exception>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        _ = await _agg.GetSnapshotAsync(ct).ConfigureAwait(false);
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new SnapshotGauge("nm.cert.scan.last_scan_unix_seconds", "Last scan time (Unix seconds)", unix);
    }

    /// <summary>
    /// Minimal snapshot gauge implementation used to emit a one-off value.
    /// </summary>
    /// <remarks>
    /// Exporters are expected to read the current value via <see cref="MetricBase.GetValue"/> once per cycle.
    /// </remarks>
    private sealed class SnapshotGauge : MetricBase, IGauge
    {
        private double _value;

        /// <summary>
        /// Creates a new <see cref="SnapshotGauge"/>.
        /// </summary>
        /// <param name="id">Stable metric identifier.</param>
        /// <param name="name">Human-readable metric name for dashboards.</param>
        /// <param name="unix">Unix time in seconds (UTC) to initialize the gauge with.</param>
        public SnapshotGauge(string id, string name, long unix)
            : base(id, name, InstrumentKind.Gauge)
        {
            _value = unix;
        }

        /// <summary>
        /// Sets the gauge to a specific value.
        /// </summary>
        /// <param name="value">The new gauge value.</param>
        public void SetValue(double value) => _value = value;  // doğru arayüz imzası

        /// <summary>
        /// Returns the current gauge value packaged for export.
        /// </summary>
        /// <returns>A <see cref="GaugeValue"/> wrapping the current numeric value.</returns>
        public override object? GetValue() => new GaugeValue(_value);
    }

    /// <summary>
    /// Creates a summary metric via the underlying factory.
    /// </summary>
    /// <param name="id">Metric identifier (stable id used by backends and scrapers).</param>
    /// <param name="name">Human-readable metric name suitable for dashboards.</param>
    /// <param name="quantiles">
    /// Desired quantiles. If <see langword="null"/>, defaults to <c>0.5</c>, <c>0.9</c>, and <c>0.99</c>.
    /// </param>
    /// <param name="tags">Optional static tags attached to all observations of the summary.</param>
    /// <param name="resetOnGet">Whether the summary should reset upon collection.</param>
    /// <returns>The built <see cref="ISummaryMetric"/> instance.</returns>
    ISummaryMetric IMetricCollector.CreateSummary(
        string id,
        string name,
        IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags,
        bool resetOnGet)
        => _factory
            .Summary(id, name)
            .WithQuantiles(quantiles?.ToArray() ?? DefaultSummaryQuantiles)
            .Build();

    /// <summary>
    /// Creates a bucket histogram metric via the underlying factory.
    /// </summary>
    /// <param name="id">Metric identifier (stable id used by backends and scrapers).</param>
    /// <param name="name">Human-readable metric name suitable for dashboards.</param>
    /// <param name="bucketUpperBounds">
    /// Upper bounds for histogram buckets. If <see langword="null"/>, an empty set is used.
    /// </param>
    /// <param name="tags">Optional static tags attached to all observations of the histogram.</param>
    /// <returns>The built <see cref="IBucketHistogramMetric"/> instance.</returns>
    /// <remarks>
    /// The provided bounds are materialized into an array once; callers are encouraged to reuse
    /// the same bounds across histograms to minimize allocations.
    /// </remarks>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id,
        string name,
        IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
        => _factory
            .Histogram(id, name)
            .WithBounds(bucketUpperBounds?.ToArray() ?? Array.Empty<double>())
            .Build();
}
