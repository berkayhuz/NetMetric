// <copyright file="ServiceBusQueueDepthCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Azure;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace NetMetric.Azure.Collectors;

/// <summary>
/// Collects Azure Service Bus queue depths for a set of queues and exposes them as NetMetric metrics.
/// </summary>
/// <remarks>
/// <para>
/// For each queue, this collector queries the <em>total</em> message count
/// (active + dead-letter + transfer-dead-letter) via <see cref="IAzureServiceBusAdmin"/> and publishes:
/// </para>
/// <list type="bullet">
///   <item><description><c>azure.servicebus.queue.depth</c> (multi-gauge): depth per queue.</description></item>
///   <item><description><c>azure.servicebus.collect.errors</c> (gauge): cumulative number of collection errors.</description></item>
///   <item><description><c>azure.servicebus.collect.last_error_unix</c> (gauge): Unix timestamp of the last error.</description></item>
/// </list>
/// <para>
/// Collection is parallelized up to <c>maxQueuesPerCollect</c> (or <see cref="Environment.ProcessorCount"/> if not provided).
/// A per-collect timeout is applied using the configured timeout (in milliseconds); if the timeout elapses, the operation is canceled
/// and <see cref="OperationCanceledException"/> is propagated.
/// </para>
/// <para><b>Thread safety</b><br/>
/// Instances are intended to be used as singletons by a scheduler/host. Internal updates to gauges use
/// atomic operations for error counting, and per-queue work is bounded via a <see cref="SemaphoreSlim"/>.</para>
/// <para><b>Logging</b><br/>
/// This collector does not log by itself; upstream adapters (e.g., <see cref="NetMetric.Azure.Adapters.ServiceBusAdminAdapter"/>)
/// may log transient failures that are retried.</para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Example: register and run in a background collector loop
/// var factory = metricFactory; // IMetricFactory from NetMetric
/// var admin   = new ServiceBusAdminAdapter(commonOptions, credentialProvider, logger);
///
/// var collector = new ServiceBusQueueDepthCollector(
///     factory,
///     admin,
///     fqns: "myspace.servicebus.windows.net",
///     queues: new[] { "orders", "billing-dlq" },
///     timeoutMs: 10_000,
///     maxQueuesPerCollect: 4);
///
/// using var cts = new CancellationTokenSource();
/// var metric = await collector.CollectAsync(cts.Token);
/// // 'metric' will be the multi-gauge representing the last updated depths.
/// ]]></code>
/// </example>
internal sealed class ServiceBusQueueDepthCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;
    private readonly IAzureServiceBusAdmin _admin;
    private readonly string _fqns;
    private readonly IReadOnlyList<string> _queues;
    private readonly int _timeoutMs;
    private readonly int _dop;

    private readonly IMultiGauge _depth;
    private readonly IGauge _errors;
    private readonly IGauge _lastErrorUnix;
    private long _errorCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusQueueDepthCollector"/> class.
    /// </summary>
    /// <param name="factory">Metric factory used to construct gauges and histograms.</param>
    /// <param name="admin">Service Bus admin abstraction used to query queue runtime properties.</param>
    /// <param name="fqns">
    /// Fully qualified Service Bus namespace (e.g., <c>myspace.servicebus.windows.net</c>).
    /// The same namespace is applied to all queues in <paramref name="queues"/>.
    /// </param>
    /// <param name="queues">The set of queue names to collect depth for. May be empty.</param>
    /// <param name="timeoutMs">
    /// Maximum duration in milliseconds for the whole collection round. If <c>&lt;= 0</c>, no explicit timeout is applied.
    /// </param>
    /// <param name="maxQueuesPerCollect">
    /// Optional upper bound on the number of queues to query concurrently. If <c>null</c>, defaults to <see cref="Environment.ProcessorCount"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/>, <paramref name="admin"/>, <paramref name="fqns"/>, or <paramref name="queues"/> is <c>null</c>.</exception>
    public ServiceBusQueueDepthCollector(
        IMetricFactory factory,
        IAzureServiceBusAdmin admin,
        string fqns,
        IReadOnlyList<string> queues,
        int timeoutMs,
        int? maxQueuesPerCollect = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _admin = admin ?? throw new ArgumentNullException(nameof(admin));
        _fqns = fqns ?? throw new ArgumentNullException(nameof(fqns));
        _queues = queues ?? throw new ArgumentNullException(nameof(queues));
        _timeoutMs = timeoutMs;
        _dop = Math.Max(1, maxQueuesPerCollect ?? Environment.ProcessorCount);

        _depth = _factory.MultiGauge("azure.servicebus.queue.depth", "Azure Service Bus Queue Depth")
                         .WithTag("cloud.provider", "azure").WithTag("module", "azure")
                         .Build();

        _errors = _factory.Gauge("azure.servicebus.collect.errors", "Collector error count (gauge)")
                          .WithTag("cloud.provider", "azure").WithTag("module", "azure")
                          .Build();

        _lastErrorUnix = _factory.Gauge("azure.servicebus.collect.last_error_unix", "Last error unix time")
                                 .WithTag("cloud.provider", "azure").WithTag("module", "azure")
                                 .Build();
    }

    /// <summary>
    /// Collects queue depth metrics for all configured queues.
    /// </summary>
    /// <returns>
    /// The multi-gauge representing <c>azure.servicebus.queue.depth</c>. The returned object is the same instance
    /// created in the constructor and updated during collection.
    /// </returns>
    /// <remarks>
    /// <para>
    /// If no queues are configured, the method returns the depth multi-gauge without performing any I/O.
    /// </para>
    /// <para>
    /// On transient Service Bus or ARM errors (e.g., <see cref="ServiceBusException"/> with <see cref="ServiceBusException.IsTransient"/>),
    /// the collector increments the error gauge and continues with remaining queues. Cancellation is honored promptly.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if the provided <see cref="CancellationToken"/> is canceled or the optional timeout elapses.</exception>
    public async Task<IMetric?> CollectAsync(CancellationToken ct)
    {
        if (_queues.Count == 0) return _depth;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_timeoutMs > 0) cts.CancelAfter(TimeSpan.FromMilliseconds(_timeoutMs));

        using var sem = new SemaphoreSlim(_dop, _dop);

        var tasks = _queues.Select(async q =>
        {
            await sem.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                var total = await _admin.GetQueueMessageCountAsync(_fqns, q, cts.Token).ConfigureAwait(false);

                _depth.AddSibling("azure.servicebus.queue.depth", "sb queue depth", total,
                    new Dictionary<string, string> { ["fqns"] = _fqns, ["queue"] = q });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ServiceBusException)
            {
                Interlocked.Increment(ref _errorCount);
                _errors.SetValue(_errorCount);
                _lastErrorUnix.SetValue(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            catch (RequestFailedException)
            {
                Interlocked.Increment(ref _errorCount);
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
    /// <param name="id">Metric identifier (stable, lowercase, dot-separated recommended).</param>
    /// <param name="name">Human-readable metric name.</param>
    /// <param name="quantiles">Quantiles for the summary (e.g., 0.5, 0.9, 0.99). If <c>null</c>, no quantiles are configured.</param>
    /// <param name="tags">Optional static tags to attach to the created metric.</param>
    /// <param name="resetOnGet">
    /// If supported by the backend, resets on read; otherwise ignored by implementations that do not support reset-on-collect semantics.
    /// </param>
    /// <returns>A built <see cref="ISummaryMetric"/>.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var p95Latency = collector.CreateSummary(
    ///     id: "netmetric.example.latency",
    ///     name: "Example Op Latency",
    ///     quantiles: new[] { 0.5, 0.9, 0.95 },
    ///     tags: new Dictionary<string,string> { ["service"] = "checkout" },
    ///     resetOnGet: false);
    ///
    /// p95Latency.Record(42.7);
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
    /// <param name="id">Metric identifier (stable, lowercase, dot-separated recommended).</param>
    /// <param name="name">Human-readable metric name.</param>
    /// <param name="bucketUpperBounds">
    /// Bucket upper bounds; if not supported by the backend, defaults are used by the underlying implementation.
    /// </param>
    /// <param name="tags">Optional static tags to attach to the created metric.</param>
    /// <returns>A built <see cref="IBucketHistogramMetric"/>.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var sizeHistogram = collector.CreateBucketHistogram(
    ///     id: "netmetric.example.payload.size",
    ///     name: "Payload Size",
    ///     bucketUpperBounds: new[] { 128d, 256d, 512d, 1024d, 2048d },
    ///     tags: new Dictionary<string,string> { ["format"] = "json" });
    ///
    /// sizeHistogram.Record(300);
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
