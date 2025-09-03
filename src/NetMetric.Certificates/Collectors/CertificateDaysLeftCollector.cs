// <copyright file="CertificateDaysLeftCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Collectors;

/// <summary>
/// Collects certificate metadata from configured sources and publishes a multi-gauge that reports
/// the number of days remaining until each certificate expires.
/// </summary>
/// <remarks>
/// <para>
/// The collector emits a parent multi-gauge with the id <c>nm.cert.days_until_expiry</c>.
/// For every certificate in the current snapshot, a sibling gauge sample is appended with:
/// </para>
/// <list type="bullet">
///   <item><description><c>id</c>: <c>nm.cert.days_until_expiry.sample</c></description></item>
/// <item><description><c>name</c>: certificate subject</description></item>
///   <item><description><c>value</c>: integer number of days left</description></item>
///   <item><description>Tags: <c>severity</c>, <c>thumb</c>, <c>subject</c>, <c>issuer</c>, <c>alg</c>, <c>source</c>, <c>store</c>, <c>host</c></description></item>
/// </list>
/// <para>
/// Design goals:
/// </para>
/// <list type="bullet">
///   <item><description><b>Low allocation &amp; stable cardinality</b> — fixed tags (<c>component=certificates</c>) are attached to the parent, item-level tags are limited to essentials.</description></item>
///   <item><description><b>Deterministic behavior</b> — tag dictionaries use ordinal comparison to ensure stable key ordering and lookups.</description></item>
/// </list>
/// </remarks>
/// <seealso cref="IMetricCollector"/>
/// <seealso cref="CertificateSeverityCountCollector"/>
/// <seealso cref="CertificateExpiryBucketsCollector"/>
/// <seealso cref="CertificatesModule"/>
public sealed class CertificateDaysLeftCollector : IMetricCollector
{
    /// <summary>
    /// Default summary quantiles used when none are provided by the caller (50th, 90th, and 99th percentiles).
    /// </summary>
    private static readonly double[] DefaultSummaryQuantiles = new[] { 0.5, 0.9, 0.99 };

    /// <summary>
    /// Aggregates certificate metadata from all sources and computes <c>DaysLeft</c> and <c>Severity</c> per item.
    /// </summary>
    private readonly CertificateAggregator _agg;

    /// <summary>
    /// The underlying multi-gauge built from the factory. All sibling samples are appended to this instance.
    /// </summary>
    private readonly IMultiGauge _g;

    /// <summary>
    /// Factory used to create metrics (gauges, summaries, histograms).
    /// </summary>
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateDaysLeftCollector"/> class.
    /// </summary>
    /// <param name="sources">Certificate sources to read from (stores, files, remote endpoints, etc.).</param>
    /// <param name="options">Collection and evaluation options controlling severity thresholds and filters.</param>
    /// <param name="factory">Metric factory used to create the multi-gauge and any composed metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Builds a multi-gauge with id <c>nm.cert.days_until_expiry</c>, description
    /// <c>"Certificate days until expiry"</c>, <c>resetOnGet=true</c>, initial capacity 256, and a fixed tag
    /// <c>component=certificates</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Prepare sources (e.g., CurrentUser\My store + a specific file)
    /// var sources = new ICertificateSource[]
    /// {
    ///     new X509StoreCertificateSource(),                  // defaults to StoreName.My, CurrentUser
    ///     new FileCertificateSource(@"C:\certs\site.crt")    // single file
    /// };
    ///
    /// // Configure thresholds (days) for warning/critical
    /// var opts = new CertificatesOptions
    /// {
    ///     WarningDays = 30,
    ///     CriticalDays = 7,
    ///     ScanTtl = TimeSpan.FromSeconds(30)
    /// };
    ///
    /// // Use your metric factory
    /// var collector = new CertificateDaysLeftCollector(sources, opts, factory);
    ///
    /// // Collect metrics (e.g., inside your scrape/collection loop)
    /// var metric = await collector.CollectAsync(CancellationToken.None);
    /// // 'metric' is a multi-gauge containing one sibling per certificate with the days-left value.
    /// ]]></code>
    /// </example>
    public CertificateDaysLeftCollector(
        IEnumerable<ICertificateSource> sources,
        CertificatesOptions options,
        IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _agg = new CertificateAggregator(sources, options);

        var mg = factory.MultiGauge("nm.cert.days_until_expiry", "Certificate days until expiry")
            .WithResetOnGet(true)
            .WithInitialCapacity(256)
            .WithTags(t => t.Add("component", "certificates"));

        _g = mg.Build();
    }

    /// <summary>
    /// Collects a fresh snapshot and appends a sibling gauge sample for every certificate,
    /// where the sample value is the number of days remaining until expiry.
    /// </summary>
    /// <param name="ct">A token to observe while awaiting the asynchronous snapshot operation.</param>
    /// <returns>
    /// The populated multi-gauge metric, or the existing instance if no items are present.
    /// </returns>
    /// <remarks>
    /// Item-level tags include <c>severity</c>, <c>thumb</c>, <c>subject</c>, <c>issuer</c>, <c>alg</c>,
    /// <c>source</c>, <c>store</c> and <c>host</c>. Values are added using ordinal string comparison for
    /// deterministic key ordering and lookups.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var snap = await _agg.GetSnapshotAsync(ct).ConfigureAwait(false);
        if (snap.items is null || snap.items.Count == 0)
            return _g;

        foreach (var it in snap.items)
        {
            _g.AddSibling(
                id: "nm.cert.days_until_expiry.sample",
                name: it.Cert.Subject,
                value: it.DaysLeft,
                tags: new Dictionary<string, string>(8, StringComparer.Ordinal)
                {
                    ["severity"] = it.Severity,
                    ["thumb"] = it.Cert.Id,
                    ["subject"] = it.Cert.Subject,
                    ["issuer"] = it.Cert.Issuer,
                    ["alg"] = it.Cert.Algorithm,
                    ["source"] = it.Cert.Source,
                    ["store"] = it.Cert.StoreName ?? string.Empty,
                    ["host"] = it.Cert.HostName ?? string.Empty
                });
        }

        return _g;
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
    /// <remarks>
    /// This helper is provided for composition scenarios where infrastructure code needs consistent
    /// factory options. The current implementation applies <paramref name="quantiles"/> (or defaults)
    /// and builds the summary. <paramref name="tags"/> and <paramref name="resetOnGet"/> are not applied
    /// here; if you require them, attach tags or reset behavior on the returned instrument using your
    /// platform’s facilities.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Create a summary for scan durations (p50/p90/p99)
    /// var s = ((IMetricCollector)collector).CreateSummary(
    ///     id: "nm.cert.scan.duration_ms",
    ///     name: "Certificate scan duration (ms)",
    ///     quantiles: null,      // will use defaults {0.5, 0.9, 0.99}
    ///     tags: null,
    ///     resetOnGet: true
    /// );
    /// ]]></code>
    /// </example>
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
    /// Provided bounds are materialized into an array once. For optimal performance and fewer allocations,
    /// prefer reusing the same bounds instance across histograms.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Create a histogram with day buckets (0,7,14,30,60,90,180)
    /// var h = ((IMetricCollector)collector).CreateBucketHistogram(
    ///     id: "nm.cert.days_left_bucket",
    ///     name: "Certificate days-left distribution",
    ///     bucketUpperBounds: new double[] { 0, 7, 14, 30, 60, 90, 180 },
    ///     tags: null
    /// );
    /// ]]></code>
    /// </example>
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
