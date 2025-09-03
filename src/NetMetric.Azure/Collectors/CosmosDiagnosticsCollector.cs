// <copyright file="CosmosDiagnosticsCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Azure;
using Microsoft.Extensions.Logging;

namespace NetMetric.Azure.Collectors;

/// <summary>
/// Collects lightweight diagnostics from Azure Cosmos DB by sampling request units (RUs) and client-side
/// latency for each configured (database, container) target, and exposes them as NetMetric metrics.
/// </summary>
/// <remarks>
/// <para>
/// For every configured (database, container) pair, this collector calls
/// <see cref="ICosmosDiagnosticsProvider.SampleRuAndLatencyAsync(string, string, string, System.Threading.CancellationToken)"/>
/// and records the following metrics:
/// </para>
/// <list type="bullet">
///   <item><description><c>azure.cosmos.request.charge</c> — <b>summary</b> with quantiles (overall RU distribution).</description></item>
///   <item><description><c>azure.cosmos.request.charge.last</c> — <b>multi-gauge</b> (last RU value per (db, container)).</description></item>
///   <item><description><c>azure.cosmos.latency.ms</c> — <b>multi-gauge</b> (measured latency in milliseconds per (db, container)).</description></item>
///   <item><description><c>azure.cosmos.collect.errors</c> — <b>gauge</b> (cumulative number of collection errors).</description></item>
///   <item><description><c>azure.cosmos.collect.last_error_unix</c> — <b>gauge</b> (Unix timestamp of the last error).</description></item>
/// </list>
/// <para>
/// Transient failures (e.g., <see cref="Microsoft.Azure.Cosmos.CosmosException"/> or <see cref="RequestFailedException"/>)
/// increment the error metrics; the collector continues with remaining targets so that a single failure does not
/// prevent other metrics from being produced.
/// </para>
/// <para>
/// <b>Thread-safety:</b> This collector is intended to be used by the NetMetric scheduler and does not maintain
/// shared mutable state beyond atomic error counters and metric instruments, which are safe for concurrent use by design.
/// </para>
/// <para>
/// <b>Example</b><br/>
/// The snippet below shows how to register Cosmos options and attach this collector via the Azure module:
/// <code language="csharp"><![CDATA[
/// services.Configure<CosmosOptions>(o =>
/// {
///     o.AccountEndpoint = "https://myaccount.documents.azure.com:443/";
///     o.Containers = new[]
///     {
///         ("salesdb", "orders"),
///         ("salesdb", "customers"),
///     };
/// });
///
/// // AzureModule will internally create CosmosDiagnosticsAdapter and CosmosDiagnosticsCollector.
/// // Later, the NetMetric host will call CollectAsync(...) on a schedule.
/// ]]></code>
/// </para>
/// </remarks>
internal sealed class CosmosDiagnosticsCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;
    private readonly ICosmosDiagnosticsProvider _diag;
    private readonly string _endpoint;
    private readonly IReadOnlyList<(string Database, string Container)> _targets;
    private readonly ILogger? _log;

    private readonly ISummaryMetric _ruSummary;
    private readonly IMultiGauge _ruLast;
    private readonly IMultiGauge _latGauge;
    private readonly IGauge _errors;
    private readonly IGauge _lastErrorUnix;
    private long _errorCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosDiagnosticsCollector"/> class.
    /// </summary>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to create metric instruments.</param>
    /// <param name="diag">The diagnostics provider that performs the RU/latency sampling against Cosmos DB.</param>
    /// <param name="endpoint">
    /// The Cosmos DB account endpoint URI (e.g., <c>https://&lt;account&gt;.documents.azure.com:443/</c>).
    /// </param>
    /// <param name="targets">The set of (database, container) pairs to probe each collection cycle.</param>
    /// <param name="logger">Optional logger for warnings and diagnostic messages.</param>
    /// <remarks>
    /// The constructor creates and configures all metric instruments (summary, multi-gauges, gauges) up-front so that
    /// collection cycles only perform recording and avoid per-iteration allocations.
    /// </remarks>
    public CosmosDiagnosticsCollector(
        IMetricFactory factory,
        ICosmosDiagnosticsProvider diag,
        string endpoint,
        IReadOnlyList<(string Database, string Container)> targets,
        ILogger? logger = null)
    {
        _factory = factory;
        _diag = diag;
        _endpoint = endpoint;
        _targets = targets;
        _log = logger;

        _ruSummary = _factory.Summary("azure.cosmos.request.charge", "CosmosDB RU (overall)")
                             .WithQuantiles(0.5, 0.9, 0.99)
                             .WithTag("cloud.provider", "azure").WithTag("module", "azure")
                             .Build();

        _ruLast = _factory.MultiGauge("azure.cosmos.request.charge.last", "CosmosDB RU (last per container)")
                          .WithTag("cloud.provider", "azure").WithTag("module", "azure")
                          .Build();

        _latGauge = _factory.MultiGauge("azure.cosmos.latency.ms", "CosmosDB latency (ms) per container")
                            .WithUnit("ms")
                            .WithTag("cloud.provider", "azure").WithTag("module", "azure")
                            .Build();

        _errors = _factory.Gauge("azure.cosmos.collect.errors", "Collector error count (gauge)")
                          .WithTag("cloud.provider", "azure").WithTag("module", "azure")
                          .Build();

        _lastErrorUnix = _factory.Gauge("azure.cosmos.collect.last_error_unix", "Last error unix time")
                                 .WithTag("cloud.provider", "azure").WithTag("module", "azure")
                                 .Build();
    }

    /// <summary>
    /// Collects metrics for all configured Cosmos DB containers by sampling RU charge and latency.
    /// </summary>
    /// <param name="ct">A cancellation token to observe.</param>
    /// <returns>
    /// The last updated metric instrument (for chaining in some backends); currently returns the latency <see cref="IMetric"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For every target, this method calls
    /// <see cref="ICosmosDiagnosticsProvider.SampleRuAndLatencyAsync(string, string, string, System.Threading.CancellationToken)"/>,
    /// records the results into the configured instruments, and updates error gauges on failure.
    /// </para>
    /// <para>
    /// <b>Cancellation:</b> If <paramref name="ct"/> is canceled, the method throws <see cref="OperationCanceledException"/>.
    /// Transient Cosmos/HTTP errors are handled per-target so that other targets continue to be processed.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    public async Task<IMetric?> CollectAsync(CancellationToken ct)
    {
        foreach (var (db, container) in _targets)
        {
            try
            {
                var (ru, ms) = await _diag
                    .SampleRuAndLatencyAsync(_endpoint, db, container, ct)
                    .ConfigureAwait(false);

                _ruSummary.Record(ru);

                _ruLast.AddSibling("azure.cosmos.request.charge.last", "ru last",
                    ru,
                    new Dictionary<string, string> { ["database"] = db, ["container"] = container });

                _latGauge.AddSibling("azure.cosmos.latency.ms", "latency",
                    ms,
                    new Dictionary<string, string> { ["database"] = db, ["container"] = container });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Microsoft.Azure.Cosmos.CosmosException)
            {
                OnCollectError(db, container);
            }
            catch (RequestFailedException)
            {
                OnCollectError(db, container);
            }
        }

        return _latGauge;
    }

    /// <summary>
    /// Updates error-related gauges when a per-target collection failure occurs.
    /// </summary>
    /// <param name="db">The database name associated with the failed attempt.</param>
    /// <param name="container">The container name associated with the failed attempt.</param>
    /// <remarks>
    /// The parameters are currently unused beyond diagnostics; the method atomically increments the error count gauge
    /// and stamps the <c>azure.cosmos.collect.last_error_unix</c> gauge with <see cref="DateTimeOffset.UtcNow"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnCollectError(string db, string container)
    {
        _ = db;
        _ = container;

        Interlocked.Increment(ref _errorCount);

        _errors.SetValue(_errorCount);
        _lastErrorUnix.SetValue(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>
    /// Creates and returns a summary metric using the provided configuration.
    /// </summary>
    /// <param name="id">The metric identifier (stable, machine-readable).</param>
    /// <param name="name">The human-readable metric display name.</param>
    /// <param name="quantiles">The quantiles to include in the summary (e.g., 0.5, 0.9, 0.99).</param>
    /// <param name="tags">Optional static tags to attach at instrument creation time.</param>
    /// <param name="resetOnGet">
    /// If supported by the backend, indicates whether the summary should be reset after a scrape/read.  
    /// Ignored for backends that do not support this behavior.
    /// </param>
    /// <returns>A configured <see cref="ISummaryMetric"/> instance.</returns>
    /// <remarks>
    /// This helper is exposed for convenience when constructing additional instruments with consistent tagging.
    /// </remarks>
    public ISummaryMetric CreateSummary(
        string id,
        string name,
        IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags,
        bool resetOnGet)
        => _factory.Summary(id, name)
                   .WithQuantiles(quantiles?.ToArray() ?? Array.Empty<double>())
                   .WithTags(t =>
                   {
                       if (tags is null) return;
                       foreach (var kv in tags) t.Add(kv.Key, kv.Value);
                   })
                   .Build();

    /// <summary>
    /// Creates and returns a bucket histogram metric using the provided configuration.
    /// </summary>
    /// <param name="id">The metric identifier (stable, machine-readable).</param>
    /// <param name="name">The human-readable metric display name.</param>
    /// <param name="bucketUpperBounds">
    /// The inclusive upper bounds of histogram buckets.  
    /// If the backend does not support custom buckets, backend defaults will be used.
    /// </param>
    /// <param name="tags">Optional static tags to attach at instrument creation time.</param>
    /// <returns>A configured <see cref="IBucketHistogramMetric"/> instance.</returns>
    /// <remarks>
    /// Use this to construct latency or size distributions when summaries are not preferred
    /// or when backends require bucketed histograms (e.g., Prometheus style).
    /// </remarks>
    public IBucketHistogramMetric CreateBucketHistogram(
        string id,
        string name,
        IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
        => _factory.Histogram(id, name)
                   .WithTags(t =>
                   {
                       if (tags is null) return;
                       foreach (var kv in tags) t.Add(kv.Key, kv.Value);
                   })
                   .Build();
}
