using RabbitMQ.Client.Exceptions;

namespace NetMetric.RabbitMQ.Collectors;

/// <summary>
/// Collects an approximate per-queue <em>consumer rate</em> (messages/second) by sampling
/// queue statistics and computing deltas between consecutive observations.
/// </summary>
/// <remarks>
/// <para>
/// This collector queries RabbitMQ for each configured queue using
/// <c>QueueDeclarePassive</c> and derives an instantaneous rate from the delta of
/// <c>MessageCount + ConsumerCount</c> over elapsed wall-clock seconds since the previous probe.
/// The result is published as a multi-gauge where each sibling corresponds to one queue,
/// identified by the <c>queue</c> tag.
/// </para>
/// <para>
/// <b>What this metric is—and is not</b>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Approximation:</b> The rate is a coarse indicator intended for dashboards and quick health checks.  
///     It does <b>not</b> account for message size, requeues, acknowledgements, or per-consumer throughput.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Lightweight:</b> Uses passive queue declarations only; no message flow is affected.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Error signaling:</b> When a queue or broker is unreachable, a zero value is emitted with tags
///     <c>status=error</c> and <c>reason=&lt;short message&gt;</c> to keep time series continuous while surfacing the failure.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Tags</b><br/>
/// Every sibling time series sets:
/// <list type="bullet">
///   <item><description><c>queue</c> (see <see cref="RabbitMqTagKeys.Queue"/>)</description></item>
///   <item><description><c>status</c> (optional; set to <c>error</c> on failures)</description></item>
///   <item><description><c>reason</c> (optional; short error description when <c>status=error</c>)</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Thread-safety:</b> Instances are not thread-safe across concurrent <see cref="CollectAsync(System.Threading.CancellationToken)"/>
/// invocations. Callers should ensure calls are serialized per instance (e.g., by the scheduler of the hosting module).
/// </para>
/// <para>
/// <b>Performance considerations:</b> Each collection creates a transient channel via
/// <c>IRabbitMqConnectionProvider.CreateChannelAsync(System.Threading.CancellationToken)</c>
/// and performs passive declarations for the configured queues. 
/// Keep the queue list reasonably small to avoid undue broker load.
/// </para>
/// <para>
/// <b>See also:</b>
/// <list type="bullet">
///   <item><description><see cref="IMetricCollector"/> for the metric-factory helpers used by this collector.</description></item>
///   <item><description><see cref="IModuleLifecycle"/> for lifecycle hooks honored by the hosting module.</description></item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Registration (e.g., in a DI container)
/// services.AddSingleton<IMetricFactory, MetricFactory>();
/// services.AddSingleton<IRabbitMqConnectionProvider, DefaultRabbitMqConnectionProvider>();
///
/// services.AddSingleton<IMetricCollector>(sp =>
/// {
///     var factory = sp.GetRequiredService<IMetricFactory>();
///     var provider = sp.GetRequiredService<IRabbitMqConnectionProvider>();
///     var queues = new[] { "orders", "payments" };
///     return new ConsumerRateCollector(factory, provider, "rabbitmq", queues);
/// });
///
/// // Periodic collection (e.g., hosted service)
/// public sealed class MetricsPump : BackgroundService
/// {
///     private readonly IMetricCollector _collector;
///     private readonly IMetricSink _sink; // wherever you push metrics
///
///     public MetricsPump(IMetricCollector collector, IMetricSink sink)
///     {
///         _collector = collector;
///         _sink = sink;
///     }
///
///     protected override async Task ExecuteAsync(CancellationToken stoppingToken)
///     {
///         if (_collector is IModuleLifecycle lf) lf.OnInit();
///
///         while (!stoppingToken.IsCancellationRequested)
///         {
///             lf?.OnBeforeCollect();
///             var metric = await _collector.CollectAsync(stoppingToken);
///             if (metric is not null) _sink.Emit(metric);
///             lf?.OnAfterCollect();
///             await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
///         }
///
///         lf?.OnDispose();
///     }
/// }
/// ]]></code>
/// </example>
public sealed class ConsumerRateCollector : IMetricCollector, IModuleLifecycle
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IRabbitMqConnectionProvider _provider;
    private readonly IMetricFactory _factory;
    private readonly string[] _queues;
    private readonly string _prefix;
    private readonly IMultiGauge _rates;
    private readonly Dictionary<string, ulong> _lastTotals = new(StringComparer.Ordinal);
    private DateTime _lastTsUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsumerRateCollector"/> class.
    /// </summary>
    /// <param name="f">The metric factory used to create metric objects (e.g., <see cref="IMultiGauge"/> instances).</param>
    /// <param name="provider">The RabbitMQ connection provider used to open channels for passive declarations.</param>
    /// <param name="prefix">The metric id prefix (defaults to <c>"rabbitmq"</c> when null/blank).</param>
    /// <param name="queues">The set of queue names to monitor. Null or empty entries are ignored.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="f"/> or <paramref name="provider"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Creates a multi-gauge with id <c>{prefix}.consumer.rate</c> and human-readable name
    /// <c>"RabbitMQ Consumer Rate (msg/s)"</c>. Individual queue series are emitted as siblings under that root.
    /// </remarks>
    public ConsumerRateCollector(IMetricFactory f, IRabbitMqConnectionProvider provider, string prefix, IEnumerable<string> queues)
    {
        _factory = f ?? throw new ArgumentNullException(nameof(f));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _prefix = string.IsNullOrWhiteSpace(prefix) ? "rabbitmq" : prefix;
        _queues = queues?.Where(q => !string.IsNullOrWhiteSpace(q)).Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        _rates = _factory.MultiGauge($"{_prefix}.consumer.rate", "RabbitMQ Consumer Rate (msg/s)").WithResetOnGet(true).Build();
    }

    /// <summary>
    /// Records the initial timestamp used to compute elapsed time for the first sampling interval.
    /// </summary>
    /// <remarks>
    /// Called by the hosting module before the first <see cref="CollectAsync(System.Threading.CancellationToken)"/>.
    /// </remarks>
    public void OnInit() => _lastTsUtc = DateTime.UtcNow;

    /// <summary>
    /// Hook invoked immediately before a collection cycle. No-op in this implementation.
    /// </summary>
    public void OnBeforeCollect() { }

    /// <summary>
    /// Hook invoked immediately after a collection cycle. No-op in this implementation.
    /// </summary>
    public void OnAfterCollect() { }

    /// <summary>
    /// Releases resources held by the collector. No-op in this implementation.
    /// </summary>
    public void OnDispose() { }

    /// <summary>
    /// Samples the configured queues and updates the multi-gauge with the latest per-queue consumer rate.
    /// </summary>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>
    /// The root <see cref="IMetric"/> representing the multi-gauge (<c>{prefix}.consumer.rate</c>), or <see langword="null"/> on no data.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For each queue, the collector:
    /// <list type="number">
    ///   <item><description>Performs a passive declaration to read <c>MessageCount</c> and <c>ConsumerCount</c>.</description></item>
    ///   <item><description>Computes <c>rate = max(0, (currentTotal - previousTotal) / elapsedSeconds)</c>.</description></item>
    ///   <item><description>Emits a sibling series <c>{prefix}.consumer.rate.&lt;queue&gt;</c> with tag <c>queue=&lt;queue&gt;</c>.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// On <see cref="OperationInterruptedException"/> or <see cref="BrokerUnreachableException"/>,
    /// emits a zero value with tags <c>status=error</c> and <c>reason</c> (shortened to 160 chars),
    /// then continues with remaining queues.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is canceled.</exception>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var elapsed = Math.Max(0.001, (now - _lastTsUtc).TotalSeconds);

        _lastTsUtc = now;

        if (_queues.Length == 0)
        {
            return _rates;
        }

        var channel = await _provider.CreateChannelAsync(ct: ct).ConfigureAwait(false);

        await using (channel)
        {
            foreach (var q in _queues)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var res = await channel.QueueDeclarePassiveAsync(q, ct).ConfigureAwait(false);
                    var total = res.MessageCount + res.ConsumerCount;

                    if (_lastTotals.TryGetValue(q, out var last))
                    {
                        var rate = Math.Max(0, (double)total - last) / elapsed;

                        _rates.AddSibling(
                            $"{_prefix}.consumer.rate.{q}",
                            $"Consumer Rate {q}",
                            rate,
                            new Dictionary<string, string> { [RabbitMqTagKeys.Queue] = q });
                    }

                    _lastTotals[q] = total;
                }
                catch (OperationInterruptedException ex)
                {
                    _rates.AddSibling(
                        $"{_prefix}.consumer.rate.{q}",
                        $"Consumer Rate {q}",
                        0,
                        new Dictionary<string, string>
                        {
                            [RabbitMqTagKeys.Queue] = q,
                            ["status"] = "error",
                            ["reason"] = Short(ex.Message)
                        });
                }
                catch (BrokerUnreachableException ex)
                {
                    _rates.AddSibling(
                        $"{_prefix}.consumer.rate.{q}",
                        $"Consumer Rate {q}",
                        0,
                        new Dictionary<string, string>
                        {
                            [RabbitMqTagKeys.Queue] = q,
                            ["status"] = "error",
                            ["reason"] = Short(ex.Message)
                        });
                }
            }
        }

        return _rates;

        static string Short(string s) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
    }

    /// <summary>
    /// Creates a summary metric with the specified quantiles.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="quantiles">The requested quantiles (defaults to 0.5/0.9/0.99 when null).</param>
    /// <param name="tags">Optional tags to associate with the metric.</param>
    /// <param name="resetOnGet">Whether the metric should reset on scrape.</param>
    /// <returns>A configured <see cref="ISummaryMetric"/> instance.</returns>
    /// <remarks>
    /// This helper delegates to <see cref="IMetricFactory.Summary(string, string)"/> and applies the provided
    /// quantiles. The <paramref name="tags"/> and <paramref name="resetOnGet"/> parameters are accepted to satisfy
    /// the <see cref="IMetricCollector"/> contract; their handling depends on the factory implementation.
    /// </remarks>
    ISummaryMetric IMetricCollector.CreateSummary(
        string id,
        string name,
        IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags,
        bool resetOnGet)
    {
        var q = quantiles?.ToArray() ?? DefaultQuantiles;
        return _factory.Summary(id, name).WithQuantiles(q).Build();
    }

    /// <summary>
    /// Creates a bucket histogram metric with the specified upper bounds.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="bucketUpperBounds">Inclusive upper bounds for buckets, in ascending order.</param>
    /// <param name="tags">Optional tags to associate with the metric.</param>
    /// <returns>A configured <see cref="IBucketHistogramMetric"/> instance.</returns>
    /// <remarks>
    /// This helper delegates to <see cref="IMetricFactory.Histogram(string, string)"/> and applies the provided bounds.
    /// The handling of <paramref name="tags"/> depends on the factory implementation.
    /// </remarks>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id,
        string name,
        IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
    {
        var bounds = bucketUpperBounds?.ToArray() ?? Array.Empty<double>();
        return _factory.Histogram(id, name).WithBounds(bounds).Build();
    }
}
