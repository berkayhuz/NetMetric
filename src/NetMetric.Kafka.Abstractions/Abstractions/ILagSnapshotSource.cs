// <copyright file="ILagSnapshotSource.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kafka.Abstractions;

/// <summary>
/// Defines a minimal, pull-based abstraction for obtaining Kafka consumer lag snapshots.
/// </summary>
/// <remarks>
/// <para>
/// A <em>lag snapshot</em> is a point-in-time view of how far a consumer group is behind
/// the log end offset for each topic/partition. Implementations typically compute lag using
/// broker metadata (end offsets) and the group's committed offsets, or by delegating to an
/// external probe (e.g., <see cref="IKafkaLagProbe"/>).
/// </para>
/// <para>
/// <b>Timeliness and consistency.</b> A single call to <see cref="Read"/> should reflect a consistent
/// set of lags taken from the same observation moment to the extent the underlying client allows.
/// Implementations should document any relaxed consistency (per-partition updates, eventual results, etc.).
/// Values are non-negative and represent <c>log_end_offset - committed_offset</c>.
/// </para>
/// <para>
/// <b>Error handling.</b> This abstraction does not prescribe how transient broker/network failures are
/// surfaced. Implementations may throw exceptions to signal unrecoverable errors, or return an empty
/// sequence when no data is available. Callers should be resilient to either behavior.
/// </para>
/// </remarks>
/// <example>
/// <para>
/// The following example adapts an <see cref="IKafkaLagProbe"/> into an <see cref="ILagSnapshotSource"/>
/// and demonstrates periodic polling:
/// </para>
/// <code><![CDATA[
/// using NetMetric.Kafka.Abstractions;
/// using System.Collections.Generic;
/// using System.Threading;
/// using System.Threading.Tasks;
///
/// public sealed class ProbeBackedLagSnapshotSource : ILagSnapshotSource
/// {
///     private readonly IKafkaLagProbe _probe;
///     private readonly string _groupId;
///
///     public ProbeBackedLagSnapshotSource(IKafkaLagProbe probe, string consumerGroup)
///     {
///         _probe = probe ?? throw new ArgumentNullException(nameof(probe));
///         _groupId = consumerGroup ?? throw new ArgumentNullException(nameof(consumerGroup));
///     }
///
///     public IEnumerable<(string topic, int partition, long lag)> Read()
///     {
///         // Synchronously bridge for simplicity; production code may cache or use async flows.
///         var map = _probe.GetLagByPartitionAsync(_groupId, CancellationToken.None)
///                         .GetAwaiter().GetResult();
///
///         foreach (var kvp in map)
///         {
///             var (topic, partition) = kvp.Key;
///             yield return (topic, partition, kvp.Value);
///         }
///     }
/// }
///
/// // Usage (e.g., in a hosted service or metric collector):
/// ILagSnapshotSource source = new ProbeBackedLagSnapshotSource(probe, "orders-consumer");
/// foreach (var (topic, partition, lag) in source.Read())
/// {
///     // Emit metric: kafka.consumer.lag{group="orders-consumer",topic,partition} = lag
/// }
/// ]]></code>
/// </example>
/// <threadsafety>
/// Implementations should be either thread-safe or explicitly document their threading requirements.
/// Callers may invoke <see cref="Read"/> concurrently when integrating with multi-threaded schedulers.
/// </threadsafety>
/// <seealso cref="IKafkaLagProbe"/>
public interface ILagSnapshotSource
{
    /// <summary>
    /// Produces a point-in-time snapshot of consumer lag for all observed topic partitions.
    /// </summary>
    /// <returns>
    /// An enumerable of tuples where each element contains:
    /// <list type="bullet">
    ///   <item><description><c>topic</c>: The Kafka topic name.</description></item>
    ///   <item><description><c>partition</c>: The partition identifier.</description></item>
    ///   <item><description><c>lag</c>: The non-negative number of messages the consumer is behind the log end offset.</description></item>
    /// </list>
    /// The enumeration may be empty if no partitions are currently observed.
    /// </returns>
    /// <remarks>
    /// Implementations should strive to return each partition at most once per invocation.
    /// If duplicates can occur, they should be documented alongside the resolution strategy (e.g., last-writer-wins).
    /// </remarks>
    IEnumerable<(string topic, int partition, long lag)> Read();
}
