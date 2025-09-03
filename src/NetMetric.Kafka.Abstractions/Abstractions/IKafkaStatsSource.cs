// <copyright file="IKafkaStatsSource.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kafka.Abstractions;

/// <summary>
/// Abstraction over a Kafka client statistics stream that parses vendor-specific payloads
/// (for example, Confluent.Kafka <c>statistics</c> JSON) and exposes typed snapshots.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are responsible for transforming raw, vendor-specific telemetry into a strongly-typed
/// <see cref="KafkaClientStatsSnapshot"/> and for making the most recent snapshot available on demand.
/// Typical implementations subscribe to a client statistics event (e.g., Confluent.Kafka's
/// <c>Statistics</c> handler), parse incoming JSON, and store the latest successful parse.
/// </para>
///
/// <para><b>Expected behavior</b></para>
/// <list type="bullet">
///   <item>
///     <description><see cref="TryGetSnapshot"/> returns the most recent successfully parsed snapshot,
///     or <see langword="null"/> if no data has been observed yet or the last update failed to parse.</description>
///   </item>
///   <item>
///     <description><see cref="ClientId"/> is a stable, human-readable identifier that helps correlate metrics
///     (for example, <c>"producer-1"</c> or <c>"consumer-A"</c>).</description>
///   </item>
///   <item>
///     <description><see cref="ClientType"/> indicates the Kafka role, typically <c>"producer"</c> or <c>"consumer"</c>.
///     Implementations should use lower-case values for consistency.</description>
///   </item>
/// </list>
///
/// <para><b>Thread safety</b></para>
/// <para>
/// Consumers may call <see cref="TryGetSnapshot"/> from arbitrary threads while the underlying client continues
/// to emit statistics. Implementations should ensure safe, low-contention publication of the latest snapshot
/// (e.g., via immutable instances and <see cref="System.Threading.Volatile"/> reads/writes).
/// </para>
///
/// <para><b>Lifetime</b></para>
/// <para>
/// The interface inherits <see cref="System.IDisposable"/> so implementations can detach from client events,
/// release pinned buffers, and dispose of any internal resources. Once disposed, callers should treat the instance
/// as unusable; subsequent calls to <see cref="TryGetSnapshot"/> should return <see langword="null"/> or throw
/// <see cref="System.ObjectDisposedException"/> depending on the implementation.
/// </para>
///
/// <para><b>Typical usage</b></para>
/// <list type="number">
///   <item>
///     <description>Register a concrete implementation in DI alongside the Kafka client lifecycle.</description>
///   </item>
///   <item>
///     <description>Wire the vendor statistics callback to update the latest snapshot.</description>
///   </item>
///   <item>
///     <description>Downstream collectors poll <see cref="TryGetSnapshot"/> periodically to export gauges/counters.</description>
///   </item>
/// </list>
///
/// <example>
/// <code language="csharp"><![CDATA[
/// // Registration (e.g., Microsoft.Extensions.DependencyInjection)
/// services.AddSingleton<IKafkaStatsSource>(sp =>
/// {
///     var source = new ConfluentKafkaStatsSource(clientId: "producer-1", clientType: "producer");
///     // The source wires itself to the Confluent.Kafka producer's Statistics event.
///     return source;
/// });
///
/// // Consumption from a collector
/// public sealed class KafkaClientStatsCollector
/// {
///     private readonly IKafkaStatsSource _source;
///
///     public KafkaClientStatsCollector(IKafkaStatsSource source) => _source = source;
///
///     public void CollectOnce()
///     {
///         var snapshot = _source.TryGetSnapshot();
///         if (snapshot is null)
///         {
///             // No data yet; skip this cycle.
///             return;
///         }
///
///         // Publish metrics using fields exposed by the snapshot
///         // e.g., gauges for queue size, counters for error counts, latency percentiles, etc.
///     }
/// }
/// ]]></code>
/// </example>
/// </remarks>
/// <seealso cref="KafkaClientStatsSnapshot"/>
public interface IKafkaStatsSource : IDisposable
{
    /// <summary>
    /// Gets the human-readable client identifier used to correlate metrics and logs
    /// (for example, <c>"producer-1"</c> or <c>"consumer-A"</c>).
    /// </summary>
    /// <value>A stable, descriptive identifier for the underlying Kafka client.</value>
    string ClientId { get; }

    /// <summary>
    /// Gets the Kafka client role.
    /// </summary>
    /// <value>
    /// A lower-case string describing the client type, typically <c>"producer"</c> or <c>"consumer"</c>.
    /// </value>
    string ClientType { get; }

    /// <summary>
    /// Returns the latest parsed snapshot of Kafka client statistics if available.
    /// </summary>
    /// <returns>
    /// The most recent <see cref="KafkaClientStatsSnapshot"/> produced by the implementation,
    /// or <see langword="null"/> if no snapshot is currently available.
    /// </returns>
    KafkaClientStatsSnapshot? TryGetSnapshot();
}
