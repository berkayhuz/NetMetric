using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace NetMetric.RabbitMQ.Collectors;

/// <summary>
/// Measures RabbitMQ publisher confirmation (confirm) round-trip latency and reports the latest observed value as a gauge (milliseconds).
/// </summary>
/// <remarks>
/// <para>
/// This collector publishes a minimal message on a channel with publisher confirmations enabled
/// and measures the end-to-end latency for that publish path. The final latency for the most recent
/// attempt is stored in a gauge metric identified by <c>{prefix}.publish.confirm.last_ms</c>.
/// </para>
/// <para>
/// If the broker is unreachable or the connection is interrupted during the operation,
/// the collector sets the gauge to <c>0</c> to indicate that a valid latency sample could not be produced
/// at that time. Other unexpected exceptions are not caught and may propagate to the caller.
/// </para>
/// <para>
/// Each invocation creates a short-lived channel configured for confirms. The message body is intentionally empty
/// to reduce measurement noise from payload serialization and I/O, focusing the measurement on the publish/confirm path.
/// </para>
/// <para><b>Metric</b></para>
/// <list type="bullet">
///   <item><description><c>Id</c>: <c>{prefix}.publish.confirm.last_ms</c> (e.g., <c>rabbitmq.publish.confirm.last_ms</c>)</description></item>
///   <item><description><c>Type</c>: <em>Gauge</em></description></item>
///   <item><description><c>Unit</c>: milliseconds</description></item>
///   <item><description><c>Value</c>: last observed confirm latency; <c>0</c> on known connectivity/interrupt errors</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Resolve dependencies (e.g., via DI) and instantiate the collector.
/// var factory = serviceProvider.GetRequiredService<IMetricFactory>();
/// var provider = serviceProvider.GetRequiredService<IRabbitMqConnectionProvider>();
///
/// // Optional prefix to group metrics, e.g. "rabbitmq" or "netmetric.rabbitmq".
/// var collector = new PublishConfirmLatencyCollector(factory, provider, prefix: "rabbitmq");
///
/// // Periodically sample confirm latency, e.g., in a background job.
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
/// var metric = await collector.CollectAsync(cts.Token);
///
/// // The returned metric is the underlying gauge that was updated with the last latency value (in ms).
/// Console.WriteLine(metric?.Id); // rabbitmq.publish.confirm.last_ms
/// ]]></code>
/// </example>
/// <threadsafety>
/// This type does not maintain mutable shared state beyond updating the underlying gauge metric.
/// Concurrent calls to <see cref="CollectAsync(System.Threading.CancellationToken)"/> are supported; each call
/// operates on its own short-lived channel instance. Gauge updates are expected to be thread-safe per the metric factory contract.
/// </threadsafety>
/// <seealso href="https://www.rabbitmq.com/confirms.html">RabbitMQ Publisher Confirms</seealso>
public sealed class PublishConfirmLatencyCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IRabbitMqConnectionProvider _provider;
    private readonly IMetricFactory _factory;
    private readonly IGauge _lastLatencyMs;
    private readonly string _metricId;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublishConfirmLatencyCollector"/> class.
    /// </summary>
    /// <param name="f">The metric factory used to construct the latency gauge and other metric primitives.</param>
    /// <param name="provider">The RabbitMQ connection provider responsible for creating confirm-enabled channels.</param>
    /// <param name="prefix">
    /// The metric prefix. If null or whitespace, <c>"rabbitmq"</c> is used.
    /// The final metric id is <c>{prefix}.publish.confirm.last_ms</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="f"/> or <paramref name="provider"/> is <see langword="null"/>.
    /// </exception>
    public PublishConfirmLatencyCollector(IMetricFactory f, IRabbitMqConnectionProvider provider, string prefix)
    {
        _factory = f ?? throw new ArgumentNullException(nameof(f));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _metricId = $"{(string.IsNullOrWhiteSpace(prefix) ? "rabbitmq" : prefix)}.publish.confirm.last_ms";
        _lastLatencyMs = _factory.Gauge(_metricId, "RabbitMQ Publish Confirm Last Latency (ms)").Build();
    }

    /// <summary>
    /// Asynchronously measures the publish-confirm round-trip latency and updates the gauge with the latest value (milliseconds).
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// The underlying gauge metric instance that was updated with the last observed latency (in milliseconds).
    /// Returns the same gauge instance on all invocations.
    /// </returns>
    /// <remarks>
    /// On <see cref="OperationInterruptedException"/> or <see cref="BrokerUnreachableException"/>,
    /// the gauge is set to <c>0</c> and returned. Other exceptions may propagate to the caller.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown if the connection provider has been disposed.</exception>
    /// <exception cref="TimeoutException">May be thrown by the underlying client when broker I/O exceeds configured timeouts.</exception>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var opts = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);

            var channel = await _provider.CreateChannelAsync(opts, ct).ConfigureAwait(false);

            await using (channel)
            {
                var props = new BasicProperties { DeliveryMode = DeliveryModes.Transient };
                var key = Guid.NewGuid().ToString("N");

                var body = ReadOnlyMemory<byte>.Empty;

                sw.Restart();

                await channel.BasicPublishAsync(
                    exchange: "",
                    routingKey: key,
                    mandatory: false,
                    basicProperties: props,
                    body: body,
                    cancellationToken: ct).ConfigureAwait(false);

                sw.Stop();

                _lastLatencyMs.SetValue(sw.Elapsed.TotalMilliseconds);

                return _lastLatencyMs;
            }
        }
        catch (OperationInterruptedException)
        {
            sw.Stop();

            _lastLatencyMs.SetValue(0);

            return _lastLatencyMs;
        }
        catch (BrokerUnreachableException)
        {
            sw.Stop();

            _lastLatencyMs.SetValue(0);

            return _lastLatencyMs;
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
