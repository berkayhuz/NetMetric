// <copyright file="StorageQueueDepthCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Azure;

namespace NetMetric.Azure.Collectors;

/// <summary>
/// Collects Azure Storage Queue depths for a set of queues and exposes them as NetMetric metrics.
/// </summary>
/// <remarks>
/// For each queue, this collector queries the approximate message count via
/// <see cref="IAzureStorageQueueAdmin"/> and publishes:
/// <list type="bullet">
///   <item>
///     <description><c>azure.storage.queue.depth</c> (multi-gauge): depth per queue.</description>
///   </item>
///   <item>
///     <description><c>azure.storage.collect.errors</c> (gauge): cumulative number of collection errors.</description>
///   </item>
///   <item>
///     <description><c>azure.storage.collect.last_error_unix</c> (gauge): Unix timestamp of the last error.</description>
///   </item>
/// </list>
/// Collection is parallelized up to <c>maxQueuesPerCollect</c> (or CPU count if not provided).
/// </remarks>
internal sealed class StorageQueueDepthCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;
    private readonly IAzureStorageQueueAdmin _admin;
    private readonly string _account;
    private readonly IReadOnlyList<string> _queues;
    private readonly string _suffix;
    private readonly int _dop;

    private readonly IMultiGauge _depth;
    private readonly IGauge _errors;       // gauge instead of counter
    private readonly IGauge _lastErrorUnix;
    private long _errorCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageQueueDepthCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create gauges and histograms.</param>
    /// <param name="admin">The Azure Storage Queue admin adapter used to query queue properties.</param>
    /// <param name="account">The storage account name (e.g., <c>mystorageaccount</c>).</param>
    /// <param name="queues">The list of queue names to collect from. If empty, <see cref="CollectAsync(System.Threading.CancellationToken)"/> returns immediately.</param>
    /// <param name="suffix">
    /// The DNS endpoint suffix (e.g., <c>core.windows.net</c>). If <c>null</c> or whitespace,
    /// the value defaults to <c>"core.windows.net"</c>. Useful for sovereign/specialized clouds.
    /// </param>
    /// <param name="maxQueuesPerCollect">
    /// Optional maximum concurrency (degree of parallelism). If <c>null</c>, defaults to <see cref="Environment.ProcessorCount"/>.
    /// A value of <c>1</c> forces sequential collection.
    /// </param>
    /// <remarks>
    /// Metrics created:
    /// <list type="bullet">
    ///   <item><description><c>azure.storage.queue.depth</c> (multi-gauge) with tags <c>cloud.provider=azure</c>, <c>module=azure</c>.</description></item>
    ///   <item><description><c>azure.storage.collect.errors</c> (gauge) with the same static tags.</description></item>
    ///   <item><description><c>azure.storage.collect.last_error_unix</c> (gauge) with the same static tags.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/>, <paramref name="admin"/>, <paramref name="account"/>, or <paramref name="queues"/> is <c>null</c>.</exception>
    public StorageQueueDepthCollector(
        IMetricFactory factory,
        IAzureStorageQueueAdmin admin,
        string account,
        IReadOnlyList<string> queues,
        string suffix,
        int? maxQueuesPerCollect = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _admin = admin ?? throw new ArgumentNullException(nameof(admin));
        _account = account ?? throw new ArgumentNullException(nameof(account));
        _queues = queues ?? throw new ArgumentNullException(nameof(queues));
        _suffix = string.IsNullOrWhiteSpace(suffix) ? "core.windows.net" : suffix;
        _dop = Math.Max(1, maxQueuesPerCollect ?? Environment.ProcessorCount);

        _depth = _factory.MultiGauge("azure.storage.queue.depth", "Azure Storage Queue Depth")
                         .WithTag("cloud.provider", "azure").WithTag("module", "azure")
                         .Build();

        _errors = _factory.Gauge("azure.storage.collect.errors", "Collector error count (gauge)")
                          .WithTag("cloud.provider", "azure").WithTag("module", "azure")
                          .Build();

        _lastErrorUnix = _factory.Gauge("azure.storage.collect.last_error_unix", "Last error unix time")
                                 .WithTag("cloud.provider", "azure").WithTag("module", "azure")
                                 .Build();
    }

    /// <summary>
    /// Collects queue depth metrics for all configured storage queues.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The multi-gauge metric that represents queue depths.</returns>
    /// <exception cref="OperationCanceledException">Propagated if the operation is canceled.</exception>
    public async Task<IMetric?> CollectAsync(CancellationToken ct)
    {
        if (_queues.Count == 0) return _depth;

        using var sem = new System.Threading.SemaphoreSlim(_dop, _dop);

        var tasks = _queues.Select(async q =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var count = await _admin
                    .GetApproxMessageCountAsync(_account, q, _suffix, ct)
                    .ConfigureAwait(false);

                _depth.AddSibling("azure.storage.queue.depth", "storage queue depth", count,
                    new Dictionary<string, string> { ["account"] = _account, ["queue"] = q });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (RequestFailedException)
            {
                System.Threading.Interlocked.Increment(ref _errorCount);

                _errors.SetValue(_errorCount);
                _lastErrorUnix.SetValue(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            finally
            {
                sem.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return _depth;
    }

    /// <summary>
    /// Creates and returns a summary metric using the provided configuration.
    /// </summary>
    /// <param name="id">The metric identifier (machine-friendly).</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="quantiles">Quantiles to include (e.g., <c>0.5</c>, <c>0.9</c>, <c>0.99</c>).</param>
    /// <param name="tags">Optional static tags to attach to the metric.</param>
    /// <param name="resetOnGet">If supported by the backend, resets distribution on read; otherwise ignored.</param>
    /// <returns>A built <see cref="ISummaryMetric"/> instance.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var p99Latency = collector.CreateSummary(
    ///     id: "azure.storage.dequeue.latency",
    ///     name: "Dequeue latency",
    ///     quantiles: new[] { 0.5, 0.9, 0.99 },
    ///     tags: new Dictionary<string,string> { ["queue"] = "orders" },
    ///     resetOnGet: false);
    /// ]]></code>
    /// </example>
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
    /// <param name="id">The metric identifier (machine-friendly).</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="bucketUpperBounds">Histogram bucket upper bounds; if not supported by the backend, defaults are used.</param>
    /// <param name="tags">Optional static tags to attach to the metric.</param>
    /// <returns>A built <see cref="IBucketHistogramMetric"/> instance.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var histogram = collector.CreateBucketHistogram(
    ///     id: "azure.storage.visibility.timeout.ms",
    ///     name: "Visibility Timeout (ms)",
    ///     bucketUpperBounds: new[] { 100.0, 500.0, 1000.0, 5000.0 },
    ///     tags: new Dictionary<string,string> { ["queue"] = "payments" });
    /// ]]></code>
    /// </example>
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
