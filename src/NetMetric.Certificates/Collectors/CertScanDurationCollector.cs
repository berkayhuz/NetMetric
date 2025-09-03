// <copyright file="CertScanDurationCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Collectors;

/// <summary>
/// Measures the end-to-end duration of a certificate scanning cycle and publishes it
/// as a summary metric in milliseconds.
/// </summary>
/// <remarks>
/// <para>
/// Wraps a call to
/// <see cref="CertificateAggregator.GetSnapshotAsync(System.Threading.CancellationToken)"/>
/// with a <see cref="System.Diagnostics.Stopwatch"/> and records the elapsed time
/// to a summary metric whose id is <c>nm.cert.scan.duration_ms</c>.
/// </para>
/// <para>
/// The summary metric exposes quantiles and is suitable for tracking median
/// and tail latencies. By default, quantiles <c>0.5</c>, <c>0.9</c>, and <c>0.99</c>
/// are enabled.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Composition root / module setup
/// var aggregator = new CertificateAggregator(sources, options);
/// var collector  = new CertScanDurationCollector(aggregator, metricFactory);
///
/// // On each collection tick
/// var metric = await collector.CollectAsync(ct);
/// if (metric is ISummaryMetric s) exporter.Export(s);
/// ]]></code>
/// </example>
public sealed class CertScanDurationCollector : IMetricCollector
{
    /// <summary>
    /// Default quantiles used by the summary metric (<c>0.5</c>, <c>0.9</c>, <c>0.99</c>).
    /// </summary>
    private static readonly double[] DefaultSummaryQuantiles = new[] { 0.5, 0.9, 0.99 };

    private readonly CertificateAggregator _agg;
    private readonly ISummaryMetric _duration;
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertScanDurationCollector"/> class.
    /// </summary>
    /// <param name="aggregator">The certificate <see cref="CertificateAggregator"/> used to execute the scan.</param>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to create metric instruments.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="aggregator"/> or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Builds a summary metric with:
    /// <list type="bullet">
    ///   <item><description><c>id</c>: <c>nm.cert.scan.duration_ms</c></description></item>
    ///   <item><description><c>name</c>: <c>Certificate scan duration (ms)</c></description></item>
    ///   <item><description>Quantiles: <c>0.5</c>, <c>0.9</c>, <c>0.99</c></description></item>
    /// </list>
    /// </remarks>
    public CertScanDurationCollector(CertificateAggregator aggregator, IMetricFactory factory)
    {
        _agg = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        _duration = factory
            .Summary("nm.cert.scan.duration_ms", "Certificate scan duration (ms)")
            .WithQuantiles(DefaultSummaryQuantiles)
            .Build();
    }

    /// <summary>
    /// Executes a certificate scan, measures its elapsed time using a stopwatch,
    /// and records the value (in milliseconds) into the summary metric.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to cancel the scan.</param>
    /// <returns>
    /// The <see cref="ISummaryMetric"/> instance populated with the latest observation;
    /// callers may export it as part of the current collection cycle.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the provided <paramref name="ct"/> is canceled before or during enumeration.
    /// </exception>
    /// <remarks>
    /// The stopwatch measures wall-clock elapsed time for <see cref="CertificateAggregator.GetSnapshotAsync(System.Threading.CancellationToken)"/>.
    /// </remarks>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _ = await _agg.GetSnapshotAsync(ct).ConfigureAwait(false);
        sw.Stop();

        _duration.Record(sw.Elapsed.TotalMilliseconds);
        return _duration;
    }

    /// <summary>
    /// Creates a new summary metric using the underlying <see cref="IMetricFactory"/>.
    /// </summary>
    /// <param name="id">Stable metric identifier.</param>
    /// <param name="name">Human-readable metric name.</param>
    /// <param name="quantiles">Desired quantiles; if <see langword="null"/>, defaults are applied.</param>
    /// <param name="tags">Optional constant tags applied to all observations of the summary.</param>
    /// <param name="resetOnGet">Whether the summary should reset internal state on collection.</param>
    /// <returns>A constructed <see cref="ISummaryMetric"/>.</returns>
    /// <remarks>
    /// Exposed for composition scenarios where infrastructure code wants to reuse the same factory
    /// while creating additional instruments alongside this collector.
    /// </remarks>
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
    /// Creates a bucket histogram metric using the underlying <see cref="IMetricFactory"/>.
    /// </summary>
    /// <param name="id">Stable metric identifier.</param>
    /// <param name="name">Human-readable metric name.</param>
    /// <param name="bucketUpperBounds">
    /// Upper bounds for histogram buckets. If <see langword="null"/>, an empty set is used.
    /// </param>
    /// <param name="tags">Optional constant tags applied to all observations of the histogram.</param>
    /// <returns>A constructed <see cref="IBucketHistogramMetric"/>.</returns>
    /// <remarks>
    /// The provided bounds are materialized once; it is recommended to reuse shared arrays to minimize allocations.
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
