// <copyright file="KafkaClientStatsSnapshot.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kafka.Abstractions;

/// <summary>
/// Represents a typed snapshot of Kafka client statistics, used by collectors.
/// </summary>
/// <param name="Timestamp">The timestamp when the snapshot was taken.</param>
/// <param name="BrokerName">The name of the Kafka broker.</param>
/// <param name="TxMsgsPerSec">The transmission rate of messages per second (TX) for this Kafka client.</param>
/// <param name="RxMsgsPerSec">The reception rate of messages per second (RX) for this Kafka client.</param>
/// <param name="TxBytesPerSec">The transmission rate of bytes per second (TX) for this Kafka client.</param>
/// <param name="RxBytesPerSec">The reception rate of bytes per second (RX) for this Kafka client.</param>
/// <param name="QueueDepth">The depth of the Kafka queue.</param>
/// <param name="BatchSizeAvg">The average batch size in the Kafka producer/consumer.</param>
/// <param name="LatencyAvgMs">The average latency (in milliseconds) for Kafka requests.</param>
/// <param name="LatencyP95Ms">The 95th percentile latency (in milliseconds) for Kafka requests.</param>
/// <param name="LatencyP99Ms">The 99th percentile latency (in milliseconds) for Kafka requests.</param>
/// <param name="RetriesTotal">The total number of retries performed by this Kafka client.</param>
/// <param name="ErrorsTotal">The total number of errors encountered by this Kafka client.</param>
public sealed record KafkaClientStatsSnapshot(
    DateTimeOffset Timestamp,
    string BrokerName,
    double TxMsgsPerSec,
    double RxMsgsPerSec,
    double TxBytesPerSec,
    double RxBytesPerSec,
    double QueueDepth,
    double BatchSizeAvg,
    double LatencyAvgMs,
    double LatencyP95Ms,
    double LatencyP99Ms,
    long RetriesTotal,
    long ErrorsTotal);
