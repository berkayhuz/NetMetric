// <copyright file="CertificateExpiringCountCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Collectors;

/// <summary>
/// Collects a point-in-time count of certificates that will expire within the configured warning window
/// and exposes it as a single gauge snapshot.
/// </summary>
/// <remarks>
/// <para>
/// This collector performs a lightweight enumeration over all configured <see cref="ICertificateSource"/> instances
/// and counts items whose remaining lifetime (in days) is less than or equal to
/// <see cref="CertificatesOptions.WarningDays"/>. The resulting value is emitted as a one-off gauge
/// (<c>nm.cert.expiring_snapshot</c>) suitable for dashboards and alerts that care about the current snapshot rather
/// than a cumulative trend.
/// </para>
/// <para><b>Emitted metric</b></para>
/// <list type="bullet">
///   <item><description><c>id</c>: <c>nm.cert.expiring_snapshot</c></description></item>
///  <item><description><c>name</c>: <c>Expiring certificates snapshot</c></description></item>
///   <item><description><c>value</c>: number of certificates with <c>daysUntilExpiry &lt;= WarningDays</c></description></item>
/// </list>
/// <para>
/// A gauge is intentional here: the set of expiring certificates may increase or decrease between collections.
/// Using a counter would misrepresent a snapshot as a monotonically increasing series.
/// </para>
/// <para><b>Thread-safety and performance</b><br/>
/// Enumeration is performed sequentially per source via <see cref="ICertificateSource.EnumerateAsync"/>, and the
/// implementation performs a single pass without materializing intermediate collections. The inner snapshot gauge
/// is a minimal, allocation-friendly wrapper around a single numeric value.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Configure sources (store + file), options and factory elsewhere:
/// IEnumerable<ICertificateSource> sources = new ICertificateSource[]
/// {
///     new X509StoreCertificateSource(),                      // CurrentUser/My
///     new FileCertificateSource(@"C:\certs\server.pfx")      // File-based
/// };
///
/// var options = new CertificatesOptions { WarningDays = 30 };
/// IMetricFactory factory = GetMetricFactory();
///
/// // Create the collector and run it on demand (e.g., in your scheduler):
/// var collector = new CertificateExpiringCountCollector(sources, options, factory);
/// var metric = await collector.CollectAsync();
///
/// // metric is an IGauge (via MetricBase) that exports id/name/value:
/// // id    : "nm.cert.expiring_snapshot"
/// // name  : "Expiring certificates snapshot"
/// // value : e.g., 2
/// ]]></code>
/// </example>
public sealed class CertificateExpiringCountCollector : IMetricCollector
{
    private static readonly double[] DefaultSummaryQuantiles = new[] { 0.5, 0.9, 0.99 };

    /// <summary>
    /// The ordered set of certificate sources to enumerate.
    /// </summary>
    private readonly IEnumerable<ICertificateSource> _sources;

    /// <summary>
    /// Options controlling thresholds such as <see cref="CertificatesOptions.WarningDays"/>.
    /// </summary>
    private readonly CertificatesOptions _opts;

    /// <summary>
    /// Metric factory used to create summary and histogram instruments on demand.
    /// </summary>
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateExpiringCountCollector"/> class.
    /// </summary>
    /// <param name="sources">Certificate sources (stores, files, remote endpoints, etc.).</param>
    /// <param name="options">Certificate evaluation options including warning thresholds.</param>
    /// <param name="factory">Metric factory for creating additional instruments.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="sources"/>, <paramref name="options"/>, or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    public CertificateExpiringCountCollector(
        IEnumerable<ICertificateSource> sources,
        CertificatesOptions options,
        IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _opts = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Enumerates all configured certificate sources and returns a gauge snapshot
    /// representing how many certificates will expire within the warning window.
    /// </summary>
    /// <param name="ct">A cancellation token to observe while awaiting asynchronous operations.</param>
    /// <returns>
    /// A gauge metric (<see cref="global::NetMetric.Abstractions.IMetric"/>) with id <c>nm.cert.expiring_snapshot</c>
    /// and the current count.
    /// </returns>
    /// <remarks>
    /// A certificate is considered <i>expiring</i> if
    /// <c>(certificate.NotAfterUtc - UtcNow).TotalDays &lt;= WarningDays</c>.
    /// </remarks>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        long expiring = 0;

        foreach (var s in _sources)
        {
            await foreach (var c in s.EnumerateAsync(ct).ConfigureAwait(false))
            {
                var days = (c.NotAfterUtc - now).TotalDays;
                if (days <= _opts.WarningDays)
                {
                    expiring++;
                }
            }
        }

        // Return a snapshot gauge (a counter would be cumulative and thus not suitable for point-in-time counts).
        return new SnapshotGauge("nm.cert.expiring_snapshot", "Expiring certificates snapshot", expiring);
    }

    /// <summary>
    /// Minimal snapshot gauge implementation used to return a point-in-time value.
    /// </summary>
    private sealed class SnapshotGauge : MetricBase, IGauge
    {
        private double _value;

        /// <summary>
        /// Creates a new <see cref="SnapshotGauge"/>.
        /// </summary>
        /// <param name="id">Metric identifier.</param>
        /// <param name="name">Human-readable metric name.</param>
        /// <param name="v">Initial value.</param>
        public SnapshotGauge(string id, string name, long v)
            : base(id, name, InstrumentKind.Gauge)
        {
            _value = v;
        }

        /// <summary>
        /// Sets the gauge to the specified <paramref name="value"/>.
        /// </summary>
        /// <param name="value">The new gauge value.</param>
        public void SetValue(double value) => _value = value;

        /// <summary>
        /// Returns the current gauge value packaged for export.
        /// </summary>
        /// <returns>The current gauge value wrapped in a <see cref="GaugeValue"/>.</returns>
        public override object? GetValue() => new GaugeValue(_value);
    }

    // ---- explicit IMetricCollector helpers ----

    /// <summary>
    /// Creates a summary metric using the underlying factory.
    /// </summary>
    /// <param name="id">Metric identifier (stable id used by backends and scrapers).</param>
    /// <param name="name">Human-readable metric name suitable for dashboards.</param>
    /// <param name="quantiles">
    /// Desired quantiles. If <see langword="null"/>, defaults to <c>0.5</c>, <c>0.9</c>, and <c>0.99</c>.
    /// </param>
    /// <param name="tags">Optional static tags attached to all observations of the summary.</param>
    /// <param name="resetOnGet">Whether the summary should reset upon collection.</param>
    /// <returns>The built <see cref="global::NetMetric.Abstractions.ISummaryMetric"/> instance.</returns>
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
    /// Creates a bucket histogram metric using the underlying factory.
    /// </summary>
    /// <param name="id">Metric identifier (stable id used by backends and scrapers).</param>
    /// <param name="name">Human-readable metric name suitable for dashboards.</param>
    /// <param name="bucketUpperBounds">
    /// Upper bounds for histogram buckets. If <see langword="null"/>, an empty set is used.
    /// </param>
    /// <param name="tags">Optional static tags attached to all observations of the histogram.</param>
    /// <returns>The built <see cref="global::NetMetric.Abstractions.IBucketHistogramMetric"/> instance.</returns>
    /// <remarks>
    /// The provided bounds are materialized into an array once; callers are encouraged to reuse the same bounds
    /// across histograms to minimize allocations.
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
