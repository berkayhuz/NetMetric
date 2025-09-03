// <copyright file="IKafkaLagProbe.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kafka.Abstractions;

/// <summary>
/// Defines an abstraction for retrieving Kafka consumer lag at the granularity of
/// <c>(topic, partition)</c> for a specific consumer group.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lag definition.</b> Unless otherwise specified by the implementation,
/// <i>lag</i> is the non-negative difference between the end offset of a partition and the
/// consumer group's committed (or current) offset for that partition at the time of sampling.
/// Implementations should normalize negative or unavailable values to <c>0</c> or omit the
/// partition from the result set (see the <see cref="GetLagAsync(string, System.Threading.CancellationToken)"/>
/// return contract).
/// </para>
/// <para>
/// <b>Data completeness.</b> The returned dictionary may exclude partitions that are unknown,
/// filtered, or temporarily unavailable. Callers must treat missing entries as "lag unknown"
/// rather than "lag is zero".
/// </para>
/// <para>
/// <b>Sampling semantics.</b> Implementations are free to compute lag using any suitable mechanism,
/// including admin APIs, fetching end offsets + committed offsets, or proxying an external service.
/// Sampling is assumed to be best-effort and point-in-time; strong consistency across partitions is not required.
/// </para>
/// </remarks>
/// <threadsafety>
/// Implementations should be safe to use from multiple threads concurrently or document the expected usage scope.
/// The interface itself imposes no threading model.
/// </threadsafety>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Resolve the probe (e.g., from DI) and read lag for each partition.
/// IKafkaLagProbe probe = serviceProvider.GetRequiredService<IKafkaLagProbe>();
///
/// // Cancellation supported; use a bounded timeout for operational calls.
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
///
/// var lags = await probe.GetLagAsync("orders-consumer", cts.Token);
///
/// foreach (var entry in lags)
/// {
///     var topic = entry.Key.topic;
///     var partition = entry.Key.partition;
///     long lag = entry.Value; // non-negative
///     Console.WriteLine($"{topic}[{partition}] lag={lag}");
/// }
/// ]]></code>
/// </example>
public interface IKafkaLagProbe
{
    /// <summary>
    /// Asynchronously obtains the current consumer lag for each known <c>(topic, partition)</c>
    /// within the specified consumer group.
    /// </summary>
    /// <param name="consumerGroup">
    /// The Kafka consumer group name for which lag should be computed. Must be a non-empty string.
    /// </param>
    /// <param name="ct">
    /// A <see cref="System.Threading.CancellationToken"/> that can be used to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes with a read-only dictionary mapping <c>(topic, partition)</c> to a
    /// non-negative lag value (<see cref="long"/>). Partitions for which lag cannot be determined
    /// may be omitted from the dictionary.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Implementations should prefer returning only partitions with reliable measurements and may omit
    /// partitions that are not yet assigned, are deleted, or could not be queried within internal timeouts.
    /// </para>
    /// <para>
    /// Callers should interpret an absent <c>(topic, partition)</c> key as "unknown/unspecified" and handle it accordingly.
    /// </para>
    /// </remarks>
    /// <exception cref="System.ArgumentException">
    /// Thrown if <paramref name="consumerGroup"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown if the operation is canceled via <paramref name="ct"/>.
    /// </exception>
    /// <exception cref="System.Exception">
    /// Implementations may surface provider-specific exceptions (e.g., transport, authorization, or
    /// metadata lookup failures). Callers should catch and handle such exceptions as appropriate.
    /// </exception>
    Task<IReadOnlyDictionary<(string topic, int partition), long>> GetLagAsync(
        string consumerGroup,
        CancellationToken ct = default);
}
