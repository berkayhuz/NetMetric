// <copyright file="KafkaStatsModels.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kafka.Configurations;

/// <summary>
/// Provides constant keys used to identify Kafka producer and consumer
/// statistics within vendor-provided JSON payloads.
/// </summary>
/// <remarks>
/// <para>
/// Kafka client libraries such as <c>Confluent.Kafka</c> often emit runtime
/// statistics as JSON objects. These statistics are typically grouped by
/// client role (e.g., <c>"producer"</c>, <c>"consumer"</c>).  
/// </para>
/// <para>
/// The <see cref="KafkaStatKeys"/> class centralizes those keys so that code
/// accessing statistics can avoid magic strings, improving maintainability
/// and reducing the likelihood of typos.
/// </para>
/// <para>
/// This class is static and designed for extension if additional top-level
/// fields are introduced in future versions of the Kafka vendor's schema.
/// </para>
/// </remarks>
/// <example>
/// Example usage when reading statistics from a JSON payload:
/// <code language="csharp">
/// using System.Text.Json;
/// using NetMetric.Kafka.Configurations;
///
/// string rawStats = GetStatsFromKafkaClient(); // vendor-provided JSON string
/// using var doc = JsonDocument.Parse(rawStats);
///
/// if (doc.RootElement.TryGetProperty(KafkaStatKeys.Producer, out var producerElement))
/// {
///     // Access producer-related metrics
///     var msgRate = producerElement.GetProperty("msg_rate").GetDouble();
///     Console.WriteLine($"Producer message rate: {msgRate}");
/// }
///
/// if (doc.RootElement.TryGetProperty(KafkaStatKeys.Consumer, out var consumerElement))
/// {
///     // Access consumer-related metrics
///     var lag = consumerElement.GetProperty("lag").GetInt32();
///     Console.WriteLine($"Consumer lag: {lag}");
/// }
/// </code>
/// </example>
public static class KafkaStatKeys
{
    /// <summary>
    /// The JSON key under which vendor libraries expose producer statistics.  
    /// Typically present at the root level of the statistics JSON.
    /// </summary>
    public const string Producer = "producer";

    /// <summary>
    /// The JSON key under which vendor libraries expose consumer statistics.  
    /// Typically present at the root level of the statistics JSON.
    /// </summary>
    public const string Consumer = "consumer";
}
