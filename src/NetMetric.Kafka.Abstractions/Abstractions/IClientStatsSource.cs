// <copyright file="IClientStatsSource.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kafka.Abstractions;

/// <summary>
/// Defines a minimal contract for obtaining the most recent snapshot of Kafka client statistics.
/// </summary>
/// <remarks>
/// <para>
/// Implementations typically surface statistics produced by Kafka clients (for example,
/// <c>librdkafka</c> via Confluent.Kafka) as a raw JSON payload accompanied by the UTC time
/// at which the snapshot was captured.
/// </para>
/// <para>
/// <b>Intended usage</b><br/>
/// Consumers (such as metric collectors) should call <see cref="TryGetSnapshot"/> on a
/// periodic cadence and, when available, parse the JSON to extract relevant metrics.
/// The method is non-throwing and signals snapshot availability via its Boolean return value.
/// </para>
/// <para>
/// <b>Threading</b><br/>
/// Unless otherwise documented by a specific implementation, instances are safe to read
/// concurrently. Writers (the components feeding statistics) are expected to ensure
/// memory visibility for readers (for example, via <see cref="System.Threading.Volatile"/>
/// operations or appropriate synchronization).
/// </para>
/// <para>
/// <b>Performance</b><br/>
/// Retrieving a snapshot should be an <em>O(1)</em> operation that does not block on I/O.
/// Implementations should avoid allocations where possible and may reuse buffers internally.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// using System;
/// using System.Text.Json;
/// using NetMetric.Kafka.Abstractions;
///
/// public sealed class StatsPollingService
/// {
///     private readonly IClientStatsSource _source;
///
///     public StatsPollingService(IClientStatsSource source)
///         => _source = source;
///
///     public void PollOnce()
///     {
///         if (_source.TryGetSnapshot(out var json, out var utc))
///         {
///             // Parse the vendor-specific JSON and extract metrics
///             using var doc = JsonDocument.Parse(json);
///             var root = doc.RootElement;
///
///             // Example: read a producer request-rate if present
///             if (root.TryGetProperty("producer", out var producer) &&
///                 producer.TryGetProperty("request_rate", out var reqRate))
///             {
///                 double value = reqRate.GetDouble();
///                 Console.WriteLine($"producer.request_rate={value} @{utc:O}");
///             }
///         }
///         else
///         {
///             // No fresh snapshot available at this moment.
///         }
///     }
/// }
/// ]]></code>
/// </example>
/// <seealso cref="System.DateTime"/>
/// <seealso cref="System.Text.Json.JsonDocument"/>
public interface IClientStatsSource
{
    /// <summary>
    /// Attempts to obtain the latest available Kafka client statistics snapshot.
    /// </summary>
    /// <param name="json">
    /// When this method returns <see langword="true"/>, contains the JSON-encoded statistics payload.
    /// When the method returns <see langword="false"/>, the value is unspecified and should be ignored.
    /// </param>
    /// <param name="utcTimestamp">
    /// When this method returns <see langword="true"/>, contains the UTC timestamp indicating
    /// when the snapshot was recorded. When the method returns <see langword="false"/>, the value
    /// is unspecified and should be ignored.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a snapshot was available and the output parameters were populated;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The method is expected to be non-throwing. If an implementation experiences a transient
    /// failure or if no snapshot has been produced yet, it should return <see langword="false"/>.
    /// </para>
    /// <para>
    /// Callers should treat the returned JSON as vendor-specific and resiliently handle missing or
    /// additional fields. The timestamp is expressed in Coordinated Universal Time (UTC).
    /// </para>
    /// </remarks>
    bool TryGetSnapshot(out string json, out DateTime utcTimestamp);
}
