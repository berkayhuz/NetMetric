// <copyright file="CertificateExpiryBucketsCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Collectors;

/// <summary>
/// Collects a bucketed distribution of remaining days until certificate expiry.
/// </summary>
/// <remarks>
/// <para>
/// This collector aggregates certificates from all configured <see cref="ICertificateSource"/> implementations,
/// computes the days left until each certificate expires, normalizes negative values to <c>0</c>,
/// and records observations into a bucket histogram metric.
/// </para>
/// <para>
/// Bucket boundaries (in days) are taken from <see cref="CertificatesOptions.BucketBoundsDays"/> if provided and non-empty;
/// otherwise, the default set is used: <c>{ 0, 7, 14, 30, 60, 90, 180 }</c>.
/// </para>
/// <para>
/// The underlying histogram instance is created once in the constructor and reused across collections
/// to minimize allocations. Observations are appended every time <see cref="CollectAsync(System.Threading.CancellationToken)"/>
/// is called, so downstream exporters that snapshot-and-reset should be configured appropriately.
/// </para>
/// <para>
/// <b>Thread safety:</b> The collector is typically scheduled by a single collection loop. If multiple callers may
/// invoke <see cref="CollectAsync(System.Threading.CancellationToken)"/> concurrently, coordinate calls externally or
/// ensure your <see cref="IMetricFactory"/> and returned instruments are safe for concurrent use.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Example: Register certificate sources and add the bucketed distribution metric.
/// var sources = new ICertificateSource[]
/// {
///     new FileCertificateSource(@"C:\certs\api.pfx"),
///     new X509StoreCertificateSource() // defaults to StoreName.My, CurrentUser
/// };
///
/// var options = new CertificatesOptions
/// {
///     WarningDays = 14,
///     CriticalDays = 3,
///     BucketBoundsDays = new List<double> { 0, 3, 7, 14, 30, 60, 90 }
/// };
///
/// IMetricFactory factory = /* obtain from your DI container */;
///
/// var collector = new CertificateExpiryBucketsCollector(sources, options, factory);
/// var metric = await collector.CollectAsync(); // emits observations into "nm.cert.days_left_bucket"
/// ]]></code>
/// </example>
/// <seealso cref="CertificateDaysLeftCollector"/>
/// <seealso cref="CertificateSeverityCountCollector"/>
/// <seealso cref="CertScanDurationCollector"/>
/// <seealso cref="CertScanErrorsCollector"/>
/// <seealso cref="CertScanLastScanCollector"/>
public sealed class CertificateExpiryBucketsCollector : IMetricCollector
{
    /// <summary>
    /// Default quantiles used by <see cref="IMetricCollector.CreateSummary(string,string,System.Collections.Generic.IEnumerable{double},System.Collections.Generic.IReadOnlyDictionary{string, string}?,bool)"/>
    /// when callers pass <see langword="null"/> for the quantiles parameter.
    /// </summary>
    private static readonly double[] DefaultSummaryQuantiles = new[] { 0.5, 0.9, 0.99 };

    /// <summary>
    /// Default histogram bucket upper bounds (in days) used when
    /// <see cref="CertificatesOptions.BucketBoundsDays"/> is not provided or empty.
    /// </summary>
    private static readonly double[] DefaultBucketBounds = new[] { 0d, 7d, 14d, 30d, 60d, 90d, 180d };

    /// <summary>
    /// Aggregator that enumerates sources, applies deduplication, and computes days-left per certificate.
    /// </summary>
    private readonly CertificateAggregator _agg;

    /// <summary>
    /// The bucket histogram instrument that receives observations of days-left values.
    /// </summary>
    private readonly IBucketHistogramMetric _h;

    /// <summary>
    /// Metric factory used to create instruments and to satisfy the explicit <see cref="IMetricCollector"/> helpers.
    /// </summary>
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateExpiryBucketsCollector"/> class.
    /// </summary>
    /// <param name="sources">Certificate sources to enumerate.</param>
    /// <param name="options">Certificate collection and bucketing options.</param>
    /// <param name="factory">Metric factory used to create the histogram.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="factory"/> is <see langword="null"/> or when the aggregator dependencies are invalid.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Builds a histogram with id <c>nm.cert.days_left_bucket</c> and name <c>"Certificate days-left distribution"</c>.
    /// If <paramref name="options"/> supplies <see cref="CertificatesOptions.BucketBoundsDays"/> with one or more items,
    /// those values are materialized and used as the histogram bounds; otherwise, <see cref="DefaultBucketBounds"/> is applied.
    /// </para>
    /// </remarks>
    public CertificateExpiryBucketsCollector(IEnumerable<ICertificateSource> sources, CertificatesOptions options, IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _agg = new CertificateAggregator(sources, options);

        // Use provided bucket bounds if non-empty; otherwise fall back to default.
        var bounds = (options.BucketBoundsDays != null && options.BucketBoundsDays.Count > 0)
            ? options.BucketBoundsDays.ToArray()
            : DefaultBucketBounds;

        _h = factory.Histogram("nm.cert.days_left_bucket", "Certificate days-left distribution")
                    .WithBounds(bounds)
                    .Build();
    }

    /// <summary>
    /// Collects the current bucketed distribution of days left until certificate expiry.
    /// </summary>
    /// <param name="ct">A cancellation token observed while awaiting asynchronous operations.</param>
    /// <returns>
    /// The histogram metric populated with observations for the current snapshot,
    /// or <see langword="null"/> when no metric is produced.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For each certificate in the snapshot, negative day counts (already expired) are clamped to <c>0</c>
    /// so they fall into the first bucket.
    /// </para>
    /// <para>
    /// This method does not reset the histogram; exporters that expect scrape-time deltas should
    /// configure reset-on-scrape behavior on the exporter side, if supported.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown if the operation is canceled via <paramref name="ct"/>.
    /// </exception>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var snap = await _agg.GetSnapshotAsync(ct).ConfigureAwait(false);

        foreach (var it in snap.items)
        {
            // Ensure negatives fall into the first bucket (0 days left).
            _h.Observe(Math.Max(0, it.DaysLeft));
        }

        return _h;
    }

    // ---- explicit IMetricCollector helpers ----

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
