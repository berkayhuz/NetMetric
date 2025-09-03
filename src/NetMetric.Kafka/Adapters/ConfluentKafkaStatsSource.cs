// <copyright file="ConfluentKafkaStatsSource.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kafka.Adapters;

/// <summary>
/// Provides a typed façade over the <c>Confluent.Kafka</c> client <em>statistics</em> callback,
/// exposing the latest parsed snapshot as a <see cref="KafkaClientStatsSnapshot"/>.
/// </summary>
/// <remarks>
/// <para>
/// The Confluent Kafka client can be configured to periodically emit a JSON statistics payload.
/// <see cref="ConfluentKafkaStatsSource"/> reads that payload from an injected
/// <see cref="KafkaStatisticsSink"/> and projects a small, frequently-used subset of fields
/// into a strongly typed snapshot for downstream consumers.
/// </para>
///
/// <para>
/// This type is suitable for registration with .NET Dependency Injection. Prefer a lifetime
/// that matches the underlying Kafka client instance (e.g., <c>Scoped</c> or <c>Singleton</c>
/// depending on your application architecture).
/// </para>
///
/// <para>
/// JSON fields that are not present or cannot be parsed are safely defaulted as described in
/// <see cref="TryGetSnapshot"/>. The method returns <see langword="null"/> if no statistics
/// have been captured yet or if the most recent payload cannot be parsed.
/// </para>
/// </remarks>
/// <threadsafety>
/// This type is thread-safe for concurrent calls to <see cref="TryGetSnapshot"/> so long as the
/// provided <see cref="KafkaStatisticsSink"/> is itself safe for concurrent <c>TryGet</c> calls.
/// The instance does not maintain mutable shared state beyond references to constructor parameters.
/// </threadsafety>
/// <example>
/// <para>Registering in DI and polling for snapshots:</para>
/// <code language="csharp"><![CDATA[
/// // In your composition root
/// services.AddSingleton<KafkaStatisticsSink>();
/// services.AddSingleton<IKafkaStatsSource>(sp =>
///     new ConfluentKafkaStatsSource(
///         clientId: "orders-producer-1",
///         clientType: "producer",
///         sink: sp.GetRequiredService<KafkaStatisticsSink>()));
///
/// // In a hosted service or metrics exporter
/// public class KafkaMetricsPusher : BackgroundService
/// {
///     private readonly IKafkaStatsSource _stats;
///     private readonly ILogger<KafkaMetricsPusher> _logger;
///
///     public KafkaMetricsPusher(IKafkaStatsSource stats, ILogger<KafkaMetricsPusher> logger)
///     {
///         _stats = stats;
///         _logger = logger;
///     }
///
///     protected override async Task ExecuteAsync(CancellationToken stoppingToken)
///     {
///         while (!stoppingToken.IsCancellationRequested)
///         {
///             var s = _stats.TryGetSnapshot();
///             if (s is not null)
///             {
///                 _logger.LogInformation(
///                     "Kafka[{ClientId}] tx={Tx} rx={Rx} p95={P95} broker={Broker}",
///                     s.ClientId, s.TxBytes, s.RxBytes, s.LatencyP95, s.BrokerName);
///             }
///
///             await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
///         }
///     }
/// }
/// ]]></code>
/// </example>
/// <seealso cref="KafkaStatisticsSink"/>
/// <seealso cref="KafkaClientStatsSnapshot"/>
public sealed class ConfluentKafkaStatsSource : IKafkaStatsSource
{
    private readonly KafkaStatisticsSink _sink;

    /// <summary>
    /// Gets the client identifier associated with the Kafka client.
    /// </summary>
    /// <value>
    /// The application-defined Kafka client identifier (e.g., <c>"orders-producer-1"</c>).
    /// </value>
    public string ClientId { get; }

    /// <summary>
    /// Gets the type of the Kafka client.
    /// </summary>
    /// <value>
    /// A string describing the client role, typically <c>"producer"</c> or <c>"consumer"</c>.
    /// </value>
    public string ClientType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfluentKafkaStatsSource"/> class.
    /// </summary>
    /// <param name="clientId">The Kafka client identifier.</param>
    /// <param name="clientType">The Kafka client type, such as <c>"producer"</c> or <c>"consumer"</c>.</param>
    /// <param name="sink">
    /// The <see cref="KafkaStatisticsSink"/> that buffers the most recent JSON statistics payload
    /// and its associated timestamp.
    /// </param>
    /// <remarks>
    /// The constructor does not perform validation on the <paramref name="clientType"/> value.
    /// Consumers may use <see cref="ClientType"/> for labeling or metric dimensions.
    /// </remarks>
    public ConfluentKafkaStatsSource(string clientId, string clientType, KafkaStatisticsSink sink)
    {
        ClientId = clientId;
        ClientType = clientType;
        _sink = sink;
    }

    /// <summary>
    /// Attempts to retrieve the most recent parsed snapshot of Kafka client statistics.
    /// </summary>
    /// <returns>
    /// A <see cref="KafkaClientStatsSnapshot"/> containing selected statistics if available and
    /// successfully parsed; otherwise, <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The underlying JSON document is expected to follow the Confluent Kafka statistics schema.
    /// The following fields are read if present; missing or unparsable fields are defaulted:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>txmsgs</c> → <c>double</c> (default <c>0</c>)</description></item>
    ///   <item><description><c>rxmsgs</c> → <c>double</c> (default <c>0</c>)</description></item>
    ///   <item><description><c>tx</c> (bytes) → <c>double</c> (default <c>0</c>)</description></item>
    ///   <item><description><c>rx</c> (bytes) → <c>double</c> (default <c>0</c>)</description></item>
    ///   <item><description><c>queue</c> → <c>double</c> (default <c>0</c>)</description></item>
    ///   <item><description><c>batchsize</c> → <c>double</c> (default <c>0</c>)</description></item>
    ///   <item><description><c>latency_avg</c>, <c>latency_p95</c>, <c>latency_p99</c> → <c>double</c> (default <c>0</c>)</description></item>
    ///   <item><description><c>retries</c>, <c>errors</c> → <c>long</c> (default <c>0</c>)</description></item>
    /// </list>
    /// <para>
    /// The broker name is obtained from the first element under the <c>brokers</c> object;
    /// if none is available, <c>"?"</c> is used.
    /// </para>
    /// </remarks>
    public KafkaClientStatsSnapshot? TryGetSnapshot()
    {
        if (!_sink.TryGet(out var json, out var ts))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Retrieve the broker name from the JSON structure
            var broker = root.TryGetProperty("brokers", out var brokers) && brokers.EnumerateObject().MoveNext()
                ? brokers.EnumerateObject().Current.Name : "?";

            // Helper function to parse doubles safely from JSON
            double D(string p, double d = 0) => root.TryGetProperty(p, out var e) && e.TryGetDouble(out var v) ? v : d;

            // Helper function to parse long values safely from JSON
            long L(string p, long l = 0) => root.TryGetProperty(p, out var e) && e.TryGetInt64(out var v) ? v : l;

            // Return a snapshot of the Kafka client stats
            return new KafkaClientStatsSnapshot(
                ts,
                broker,
                D("txmsgs"),
                D("rxmsgs"),
                D("tx"),
                D("rx"),
                D("queue"),
                D("batchsize"),
                D("latency_avg"),
                D("latency_p95"),
                D("latency_p99"),
                L("retries"),
                L("errors"));
        }
        catch
        {
            return null;

            throw;
        }
    }

    /// <summary>
    /// Releases resources associated with this instance.
    /// </summary>
    /// <remarks>
    /// The current implementation does not hold unmanaged resources and performs no action.
    /// The method exists to satisfy <see cref="IKafkaStatsSource"/> and to allow future expansion
    /// without breaking changes.
    /// </remarks>
    public void Dispose()
    {
    }
}
