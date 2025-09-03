// <copyright file="CertificatesModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Modules;

/// <summary>
/// Entry-point module that wires together all certificate-related metric collectors in NetMetric.
/// </summary>
/// <remarks>
/// <para>
/// This module encapsulates multiple collectors that rely on a common <see cref="CertificateAggregator"/> instance
/// for self-metrics, ensuring consistent scan context when calculating duration, error counts, and last scan time.
/// </para>
/// <para>
/// It automatically registers the following collectors:
/// <list type="bullet">
///   <item><description><see cref="CertificateDaysLeftCollector"/> — emits days-until-expiry per certificate (multi-gauge).</description></item>
///   <item><description><see cref="CertificateSeverityCountCollector"/> — emits counts grouped by severity (warn, crit, expired).</description></item>
///   <item><description><see cref="CertificateExpiryBucketsCollector"/> — emits bucketed distribution of days left until expiry.</description></item>
///   <item><description><c>CertScanDurationCollector</c> — measures duration of certificate scans.</description></item>
///   <item><description><c>CertScanErrorsCollector</c> — cumulative count of errors encountered during scans.</description></item>
///   <item><description><c>CertScanLastScanCollector</c> — timestamp of last successful scan (Unix seconds).</description></item>
/// </list>
/// </para>
/// <para>
/// By default, lifecycle methods (<see cref="OnInit"/>, <see cref="OnDispose"/>, <see cref="OnBeforeCollect"/>, <see cref="OnAfterCollect"/>)
/// are no-ops, but they can be overridden by implementers of <see cref="IModuleLifecycle"/> if additional hooks are needed.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Example: Registering the CertificatesModule in your metric pipeline.
/// var sources = new ICertificateSource[]
/// {
///     new FileCertificateSource(@"C:\certs\api.pfx"),
///     new X509StoreCertificateSource() // defaults to StoreName.My, CurrentUser
/// };
///
/// var options = new CertificatesOptions
/// {
///     WarningDays = 14,
///     CriticalDays = 3
/// };
///
/// IMetricFactory factory = /* obtain from DI container or context */;
///
/// var module = new CertificatesModule(sources, options, factory);
///
/// foreach (var collector in module.GetCollectors())
/// {
///     var metric = await collector.CollectAsync();
///     if (metric is not null)
///     {
///         // Export to backend
///     }
/// }
/// ]]></code>
/// </example>
public sealed class CertificatesModule : IModule, IModuleLifecycle
{
    /// <summary>
    /// Gets the display name of this module, used by diagnostic and registry systems.
    /// </summary>
    public string Name => "NetMetric.Certificates";

    private readonly IMetricCollector[] _collectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificatesModule"/> class.
    /// </summary>
    /// <param name="sources">Certificate sources to enumerate (e.g., file paths, X509 stores, endpoints).</param>
    /// <param name="options">Certificate evaluation options controlling thresholds, filters, and scan settings.</param>
    /// <param name="factory">Metric factory used to build underlying instruments (gauges, histograms, counters, etc.).</param>
    /// <remarks>
    /// A dedicated <see cref="CertificateAggregator"/> is created for self-metrics
    /// (<c>CertScanDurationCollector</c>, <c>CertScanErrorsCollector</c>, <c>CertScanLastScanCollector</c>)
    /// so that scan duration, error count, and timestamps remain consistent across a single scan execution.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="sources"/>, <paramref name="options"/>, or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    public CertificatesModule(IEnumerable<ICertificateSource> sources, CertificatesOptions options, IMetricFactory factory)
    {
        // Let self-metrics collectors share the same aggregator instance.
        var selfAgg = new CertificateAggregator(sources, options);

        _collectors = new IMetricCollector[]
        {
            // main certificate metrics
            new CertificateDaysLeftCollector(sources, options, factory),
            new CertificateSeverityCountCollector(sources, options, factory),
            new CertificateExpiryBucketsCollector(sources, options, factory),

            // self-metrics (share selfAgg)
            new CertScanDurationCollector(selfAgg, factory),
            new CertScanErrorsCollector(selfAgg, factory),
            new CertScanLastScanCollector(selfAgg, factory),
        };
    }

    /// <summary>
    /// Gets the set of collectors registered by this module.
    /// </summary>
    /// <returns>An enumerable of <see cref="IMetricCollector"/> instances ready for scheduling.</returns>
    public IEnumerable<IMetricCollector> GetCollectors() => _collectors;

    /// <summary>
    /// Called when the module is initialized. Default implementation is a no-op.
    /// </summary>
    public void OnInit() { }

    /// <summary>
    /// Called when the module is disposed. Default implementation is a no-op.
    /// </summary>
    public void OnDispose() { }

    /// <summary>
    /// Called immediately before a collection cycle starts. Default implementation is a no-op.
    /// </summary>
    public void OnBeforeCollect() { }

    /// <summary>
    /// Called immediately after a collection cycle ends. Default implementation is a no-op.
    /// </summary>
    public void OnAfterCollect() { }
}
