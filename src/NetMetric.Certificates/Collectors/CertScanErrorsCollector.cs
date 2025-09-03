// <copyright file="CertScanErrorsCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Collectors;

/// <summary>
/// Collects and publishes the total number of certificate scan errors.
/// </summary>
/// <remarks>
/// <para>
/// Emits a parent multi-gauge with id <c>nm.cert.days_until_expiry</c>.
/// For every certificate in the current snapshot, a sibling gauge sample is appended with:
/// </para>
/// <list type="bullet">
///   <item><description><c>id</c>: <c>nm.cert.days_until_expiry.sample</c></description></item>
///   <item><description>Tags: <c>severity</c>, <c>thumb</c>, <c>subject</c>, <c>issuer</c>, <c>alg</c>, <c>source</c>, <c>store</c>, <c>host</c></description></item>
/// </list>
/// <para>
/// The collector is designed for low allocation overhead and stable cardinality; fixed tags are attached
/// to the parent metric (<c>component=certificates</c>), and item-level tags are limited to essential fields.
/// </para>
/// </remarks>

/// <example>
/// The following example registers the collector and exposes the cumulative error counter:
/// <code language="csharp"><![CDATA[
/// // Build a shared aggregator (e.g., reused by self-metrics)
/// var aggregator = new CertificateAggregator(sources, options);
///
/// // Create the collector with the application's metric factory
/// var collector = new CertScanErrorsCollector(aggregator, metricFactory);
///
/// // During each scrape/collection cycle:
/// var metric = await collector.CollectAsync(ct);
/// // 'metric' is an ICounterMetric publishing "nm.cert.scan.errors_total"
/// ]]></code>
/// </example>
/// <seealso cref="ICounterMetric"/>
/// <seealso cref="IMetricFactory"/>
/// <seealso cref="CertificateAggregator"/>
public sealed class CertScanErrorsCollector : IMetricCollector
{
    /// <summary>
    /// Default quantiles used by <see cref="IMetricCollector.CreateSummary(string, string, IEnumerable{double}, IReadOnlyDictionary{string, string}?, bool)"/>
    /// when a caller provides <see langword="null"/> for quantiles.
    /// </summary>
    private static readonly double[] DefaultSummaryQuantiles = new[] { 0.5, 0.9, 0.99 };

    private readonly CertificateAggregator _agg;
    private readonly ICounterMetric _errors;
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertScanErrorsCollector"/> class.
    /// </summary>
    /// <param name="aggregator">The <see cref="CertificateAggregator"/> that provides certificate scan snapshots and statistics.</param>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to construct the underlying counter metric.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="aggregator"/> or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Creates a counter with:
    /// <list type="bullet">
    ///   <item><description><c>id</c>: <c>nm.cert.scan.errors_total</c></description></item>
    ///   <item><description><c>name</c>: <c>Total scan errors</c></description></item>
    /// </list>
    /// </remarks>
    public CertScanErrorsCollector(CertificateAggregator aggregator, IMetricFactory factory)
    {
        _agg = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        _errors = factory
            .Counter("nm.cert.scan.errors_total", "Total scan errors")
            .Build();
    }

    /// <summary>
    /// Collects the current number of scan errors from the aggregator and updates
    /// the cumulative <c>nm.cert.scan.errors_total</c> counter accordingly.
    /// </summary>
    /// <param name="ct">An optional <see cref="CancellationToken"/> to observe during the snapshot operation.</param>
    /// <returns>
    /// The same <see cref="ICounterMetric"/> instance representing the cumulative error count
    /// (never <see langword="null"/>).
    /// </returns>
    /// <remarks>
    /// If the latest snapshot reports <c>Errors &gt; 0</c>, that value is added to the counter. When
    /// <c>Errors == 0</c>, the counter is left unchanged to preserve monotonicity.
    /// </remarks>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var snap = await _agg.GetSnapshotAsync(ct).ConfigureAwait(false);
        if (snap.stats.Errors > 0)
        {
            _errors.Increment(snap.stats.Errors); // cumulative
        }
        return _errors;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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
