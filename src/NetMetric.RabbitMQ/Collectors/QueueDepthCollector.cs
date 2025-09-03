// © NetMetric 2025 - QueueDepthCollector.cs

using RabbitMQ.Client.Exceptions;

namespace NetMetric.RabbitMQ.Collectors;

/// <summary>
/// Collects the current message depth (ready message count) for one or more RabbitMQ queues
/// and publishes the values as gauge-like time series (one series per queue).
/// </summary>
/// <remarks>
/// <para>
/// This collector performs a passive declaration (<c>queue.declare</c> with <em>passive</em> flag)
/// for each configured queue and reads the broker-reported <c>message-count</c>.
/// A separate time series is emitted for every queue. In case of errors (e.g., queue missing,
/// broker unreachable), a zero value is emitted with additional tags describing the failure.
/// </para>
/// <para>
/// <b>Metric shape</b><br/>
/// Base metric id: <c>rabbitmq.queue.depth</c><br/>
/// For each queue <c>q</c>, a sibling series is emitted with id <c>rabbitmq.queue.depth.&lt;q&gt;</c>.
/// The following tags are attached:
/// <list type="bullet">
///   <item><description><c>queue</c>: the queue name.</description></item>
///   <item><description><c>status</c>: present only on failure (<c>"error"</c> or <c>"cancelled"</c>).</description></item>
///   <item><description><c>reason</c>: short, human-readable error cause (present only on failure).</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Cancellation &amp; failures</b><br/>
/// If collection is cancelled via <see cref="CollectAsync(System.Threading.CancellationToken)"/>,
/// a single status gauge with id <c>rabbitmq.queue.depth.status</c> is returned with
/// <c>status="cancelled"</c> and value <c>0</c>. On unexpected exceptions, a status gauge is
/// returned with <c>status="error"</c> and a short <c>reason</c> tag. In both cases, the
/// result shape is a simple gauge suitable for scraping and alerting.
/// </para>
/// <para>
/// <b>Thread safety</b><br/>
/// Instances are typically registered with a scoped or singleton lifetime. All public members
/// are safe to call concurrently; the collector acquires channels per invocation and does not
/// mutate shared state beyond construction.
/// </para>
/// <para>
/// <b>Performance considerations</b><br/>
/// The collector uses a single channel for the polling cycle and issues one passive declare per
/// queue. On brokers with very large numbers of queues, consider sharding the collector across
/// queue subsets or reducing scrape frequency.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Register the collector (e.g., in a DI composition root)
/// services.AddSingleton<IMetricCollector>(sp =>
/// {
///     var factory  = sp.GetRequiredService<IMetricFactory>();
///     var provider = sp.GetRequiredService<IRabbitMqConnectionProvider>();
///     var queues   = new[] { "orders-ready", "billing-retry", "email-outbox" };
///     return new QueueDepthCollector(factory, provider, queues);
/// });
///
/// // Ad-hoc usage without DI:
/// var metric = await collector.CollectAsync(ct);
/// if (metric is not null)
/// {
///     // Export or inspect the multi-series metric
/// }
/// ]]></code>
/// </example>
public sealed class QueueDepthCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;
    private readonly IRabbitMqConnectionProvider _provider;
    private readonly string[] _queues;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueueDepthCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metric objects.</param>
    /// <param name="provider">The RabbitMQ connection provider used to create AMQP channels.</param>
    /// <param name="queueNames">
    /// The queue names to monitor. <c>null</c> or an empty sequence results in no queue series
    /// being emitted (a valid, empty multi-series metric is still returned).
    /// Duplicate and whitespace-only entries are ignored in a case-sensitive manner.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="factory"/> or <paramref name="provider"/> is <c>null</c>.
    /// </exception>
    public QueueDepthCollector(IMetricFactory factory, IRabbitMqConnectionProvider provider, IEnumerable<string>? queueNames)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _queues = queueNames?.Where(q => !string.IsNullOrWhiteSpace(q))
                             .Distinct(StringComparer.Ordinal)
                             .ToArray()
                  ?? Array.Empty<string>();
    }

    /// <summary>
    /// Collects the current depth (message count) for each configured queue.
    /// </summary>
    /// <param name="ct">A cancellation token to observe while waiting for I/O operations.</param>
    /// <returns>
    /// A multi-series metric containing one time series per queue, or a status gauge if the
    /// operation is cancelled or fails. The method never throws for expected broker conditions
    /// such as a missing queue; those are encoded as time-series with <c>status="error"</c>.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown if <paramref name="ct"/> is signalled before or during collection. In that case,
    /// a <c>status</c> gauge is returned and the exception is swallowed.
    /// </exception>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        const string Id = "rabbitmq.queue.depth";
        const string Name = "RabbitMQ Queue Depth";

        try
        {
            ct.ThrowIfCancellationRequested();

            // Create an empty multi-series metric; by default it will be cleared after being read.
            var mg = _factory.MultiGauge(Id, Name).WithResetOnGet(true).Build();

            if (_queues.Length == 0)
            {
                return mg;
            }

            var channel = await _provider.CreateChannelAsync(ct: ct).ConfigureAwait(false);
            await using (channel)
            {
                foreach (var q in _queues)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var ok = await channel.QueueDeclarePassiveAsync(q, ct).ConfigureAwait(false);

                        mg.AddSibling($"{Id}.{q}", q, ok.MessageCount,
                            new Dictionary<string, string> { { "queue", q } });
                    }
                    catch (OperationInterruptedException ex)
                    {
                        // Queue missing or not accessible: emit a zero value with diagnostic tags.
                        mg.AddSibling($"{Id}.{q}", q, 0,
                            new Dictionary<string, string>
                            {
                                { "queue", q },
                                { "status", "error" },
                                { "reason", Short(ex.Message) }
                            });
                    }
                    catch (BrokerUnreachableException ex)
                    {
                        // Broker connectivity problem: emit a zero value with diagnostic tags.
                        mg.AddSibling($"{Id}.{q}", q, 0,
                            new Dictionary<string, string>
                            {
                                { "queue", q },
                                { "status", "error" },
                                { "reason", Short(ex.Message) }
                            });
                    }
                }
            }

            return mg;
        }
        catch (OperationCanceledException)
        {
            // Encode cancellation as a status metric to keep exporters simple.
            var g = _factory.Gauge($"{Id}.status", "RabbitMQ QueueDepth Status")
                            .WithTag("status", "cancelled")
                            .Build();

            g.SetValue(0);
            return g;
        }
        catch (Exception ex)
        {
            // Gracefully degrade to a status gauge with a short diagnostic reason.
            var g = _factory.Gauge($"{Id}.status", "RabbitMQ QueueDepth Status")
                            .WithTag("status", "error")
                            .WithTag("reason", Short(ex.Message))
                            .Build();
            g.SetValue(0);

            return g;

            // Intentionally unreachable due to the return above; left to aid debugging in development.
            throw;
        }

        static string Short(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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
