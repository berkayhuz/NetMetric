// <copyright file="CertificateSeverityCountCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Collectors;

/// <summary>
/// Collects counts of certificates grouped by severity (<c>warn</c>, <c>crit</c>, <c>expired</c>)
/// and publishes them as sibling samples of a multi-gauge metric.
/// </summary>
/// <remarks>
/// <para>
/// This collector computes per-severity counts from a snapshot produced by
/// <see cref="CertificateAggregator"/> and emits them as three sibling samples under a parent
/// multi-gauge with id <c>nm.cert.severity_count</c>. Each sibling uses the metric id
/// <c>nm.cert.severity_count.sample</c> and carries a <c>severity</c> tag with value
/// <c>warn</c>, <c>crit</c>, or <c>expired</c>.
/// </para>
/// <para>
/// <b>Semantics</b><br/>
/// Every collection pass reflects the current, point-in-time tally of items in each severity bucket.
/// The values can go up or down between collections depending on certificate lifetimes.
/// </para>
/// <para>
/// <b>Performance</b><br/>
/// Single pass over the snapshot (<c>O(n)</c>) with constant extra allocations. Intended to be
/// lightweight and suitable for frequent collection intervals.
/// </para>
/// <para>
/// <b>Thread safety</b><br/>
/// Instances are not guaranteed to be thread-safe for concurrent <see cref="CollectAsync(System.Threading.CancellationToken)"/>
/// calls. If you schedule collectors in parallel, coordinate access externally or create separate instances.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Build sources and options
/// IEnumerable<ICertificateSource> sources = new ICertificateSource[]
/// {
///     new X509StoreCertificateSource(), // from NetMetric.Certificates.Infra
///     // new FileCertificateSource(options, "c:\\certs\\site.pfx"), ...
/// };
///
/// var options = new CertificatesOptions
/// {
///     WarningDays = 14,
///     CriticalDays = 3
/// };
///
/// // Obtain a metric factory from your hosting/runtime
/// IMetricFactory factory = GetMetricFactoryFromHost();
///
/// // Create the collector
/// var collector = new CertificateSeverityCountCollector(sources, options, factory);
///
/// // Collect once (e.g., inside your scheduler)
/// IMetric? metric = await collector.CollectAsync();
///
/// // 'metric' is an IMultiGauge; each sibling has id "nm.cert.severity_count.sample"
/// // with tags["severity"] in { "warn", "crit", "expired" } and the sample value = count.
/// ]]></code>
/// </example>
public sealed class CertificateSeverityCountCollector : IMetricCollector
{
    /// <summary>
    /// Default summary quantiles used when infrastructure code composes summary instruments
    /// through the explicit <see cref="IMetricCollector.CreateSummary(string, string, System.Collections.Generic.IEnumerable{double}, System.Collections.Generic.IReadOnlyDictionary{string, string}?, bool)"/> helper.
    /// </summary>
    private static readonly double[] DefaultSummaryQuantiles = new[] { 0.5, 0.9, 0.99 };

    private readonly CertificateAggregator _agg;
    private readonly IMultiGauge _g;
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateSeverityCountCollector"/> class.
    /// </summary>
    /// <param name="sources">Certificate sources to enumerate (stores, files, endpoints, etc.).</param>
    /// <param name="options">Certificate evaluation options (thresholds, filters, TTL, concurrency).</param>
    /// <param name="factory">Metric factory used to build the multi-gauge and other instruments.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="factory"/> is <see langword="null"/>.
    /// May also propagate if <paramref name="sources"/> or <paramref name="options"/> are <see langword="null"/>,
    /// since they are passed to <see cref="CertificateAggregator"/> which validates its inputs.
    /// </exception>
    /// <remarks>
    /// Builds a parent multi-gauge with id <c>nm.cert.severity_count</c> and name <c>"Certificates by severity"</c>.
    /// Sibling samples will be appended during <see cref="CollectAsync(System.Threading.CancellationToken)"/>.
    /// </remarks>
    public CertificateSeverityCountCollector(IEnumerable<ICertificateSource> sources, CertificatesOptions options, IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _agg = new CertificateAggregator(sources, options);
        _g = factory
            .MultiGauge("nm.cert.severity_count", "Certificates by severity")
            .Build();
    }

    /// <summary>
    /// Aggregates certificate data and emits per-severity counts as sibling gauges.
    /// </summary>
    /// <param name="ct">Cancellation token for the asynchronous operation.</param>
    /// <returns>
    /// The parent multi-gauge populated with three siblings (<c>warn</c>, <c>crit</c>, <c>expired</c>),
    /// each emitted with id <c>nm.cert.severity_count.sample</c> and a <c>severity</c> tag.
    /// </returns>
    /// <remarks>
    /// The severity values correspond to the <c>Severity</c> field computed by the aggregator for each item.
    /// </remarks>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var snap = await _agg.GetSnapshotAsync(ct).ConfigureAwait(false);

        int warn = 0, crit = 0, exp = 0;
        foreach (var it in snap.items)
        {
            switch (it.Severity)
            {
                case "warn":
                    warn++;
                    break;
                case "crit":
                    crit++;
                    break;
                case "expired":
                    exp++;
                    break;
            }
        }

        _g.AddSibling(
            id: "nm.cert.severity_count.sample",
            name: "warn",
            value: warn,
            tags: new Dictionary<string, string>(1, StringComparer.Ordinal) { ["severity"] = "warn" });

        _g.AddSibling(
            id: "nm.cert.severity_count.sample",
            name: "crit",
            value: crit,
            tags: new Dictionary<string, string>(1, StringComparer.Ordinal) { ["severity"] = "crit" });

        _g.AddSibling(
            id: "nm.cert.severity_count.sample",
            name: "expired",
            value: exp,
            tags: new Dictionary<string, string>(1, StringComparer.Ordinal) { ["severity"] = "expired" });

        return _g;
    }

    // ----- explicit IMetricCollector helpers -----

    /// <summary>
    /// Creates a summary metric using the provided quantiles, defaulting to <c>0.5</c>, <c>0.9</c>, and <c>0.99</c>.
    /// </summary>
    /// <param name="id">Metric identifier (stable id used by backends and scrapers).</param>
    /// <param name="name">Human-readable metric name suitable for dashboards.</param>
    /// <param name="quantiles">Desired quantiles, or <see langword="null"/> to use defaults.</param>
    /// <param name="tags">Optional constant tags attached to all observations.</param>
    /// <param name="resetOnGet">Whether the summary resets its internal state on collection.</param>
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
    /// Creates a bucket histogram metric using the specified bucket upper bounds.
    /// </summary>
    /// <param name="id">Metric identifier (stable id used by backends and scrapers).</param>
    /// <param name="name">Human-readable metric name suitable for dashboards.</param>
    /// <param name="bucketUpperBounds">Histogram bucket upper bounds; if <see langword="null"/>, an empty set is used.</param>
    /// <param name="tags">Optional constant tags attached to all observations.</param>
    /// <returns>The built <see cref="IBucketHistogramMetric"/> instance.</returns>
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
