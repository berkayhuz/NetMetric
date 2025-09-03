// <copyright file="ConfluentBuilderExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Confluent.Kafka;
using NetMetric.Kafka.Statistics;

namespace NetMetric.Kafka.DependencyInjection;

/// <summary>
/// Extension methods that integrate NetMetric statistics handling with Confluent.Kafka builders
/// (<see cref="ConsumerBuilder{TKey,TValue}"/> and <see cref="ProducerBuilder{TKey,TValue}"/>).
/// </summary>
/// <remarks>
/// <para>
/// These helpers register a <see cref="KafkaStatisticsSink"/> via the Confluent client
/// <c>statistics</c> callback so that raw librdkafka statistics (JSON) are captured
/// and made available to the rest of the NetMetric pipeline.
/// </para>
/// <para>
/// <b>Prerequisites</b><br/>
/// The Confluent client must have <c>statistics.interval.ms</c> set to a value &gt; 0 for statistics
/// to be emitted by librdkafka. If this property is not configured, the callback will never fire.
/// </para>
/// <para>
/// <b>Callback behavior</b><br/>
/// Calling these methods sets the underlying Confluent <c>StatisticsHandler</c>.
/// If a handler was previously set, it will be replaced.
/// </para>
/// </remarks>
/// <threadsafety>
/// The extension methods are thread-safe to call during builder configuration. The resulting
/// statistics callback is invoked by the Confluent client’s internal threads, and
/// <see cref="KafkaStatisticsSink"/> is designed to be concurrency-safe for writes.
/// </threadsafety>
/// <example>
/// <code language="csharp"><![CDATA[
/// using Confluent.Kafka;
/// using NetMetric.Kafka.Statistics;
/// using NetMetric.Kafka.DependencyInjection;
///
/// // Configure a consumer builder and attach NetMetric statistics handling.
/// var consumerConfig = new ConsumerConfig
/// {
///     BootstrapServers = "broker:9092",
///     GroupId = "payments-consumer",
///     // Enable librdkafka statistics every 10 seconds
///     StatisticsIntervalMs = 10_000
/// };
///
/// var sink = new KafkaStatisticsSink();
///
/// using var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig)
///     .WithNetMetricStatistics(sink)
///     .Build();
///
/// // Later, elsewhere in your app, a component can read the latest stats snapshot:
/// if (sink.TryGet(out var json, out var utc))
/// {
///     Console.WriteLine($"Stats @ {utc:o}: {json}");
/// }
/// ]]></code>
/// </example>
/// <seealso cref="KafkaStatisticsSink"/>
/// <seealso cref="ConsumerBuilder{TKey,TValue}"/>
/// <seealso cref="ProducerBuilder{TKey,TValue}"/>
public static class ConfluentBuilderExtensions
{
    /// <summary>
    /// Registers a NetMetric statistics handler on a <see cref="ConsumerBuilder{TKey,TValue}"/> so that
    /// librdkafka statistics (JSON) are forwarded to the provided <paramref name="sink"/>.
    /// </summary>
    /// <typeparam name="TKey">The Kafka record key type of the consumer.</typeparam>
    /// <typeparam name="TValue">The Kafka record value type of the consumer.</typeparam>
    /// <param name="builder">The consumer builder to extend.</param>
    /// <param name="sink">The sink that receives each statistics payload as it is emitted.</param>
    /// <returns>
    /// The same <see cref="ConsumerBuilder{TKey,TValue}"/> instance to enable fluent configuration.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> is <see langword="null"/> or
    /// <paramref name="sink"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method calls <see cref="ConsumerBuilder{TKey,TValue}.SetStatisticsHandler(System.Action{IConsumer{TKey,TValue},string})"/>
    /// and wires the callback to <see cref="KafkaStatisticsSink.Handle(string)"/>.
    /// </para>
    /// <para>
    /// Ensure <c>statistics.interval.ms</c> is configured (e.g., <c>10_000</c>) on the consumer
    /// <see cref="ConsumerConfig"/>; otherwise no statistics will be produced.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var sink = new KafkaStatisticsSink();
    ///
    /// using var consumer = new ConsumerBuilder<string, string>(consumerConfig)
    ///     .WithNetMetricStatistics(sink)
    ///     .Build();
    ///
    /// // Start consuming as usual; statistics will be pushed into 'sink'.
    /// consumer.Subscribe("orders");
    /// ]]></code>
    /// </example>
    public static ConsumerBuilder<TKey, TValue> WithNetMetricStatistics<TKey, TValue>(
        this ConsumerBuilder<TKey, TValue> builder, KafkaStatisticsSink sink)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sink);

        return builder.SetStatisticsHandler((_, json) => sink.Handle(json));
    }

    /// <summary>
    /// Registers a NetMetric statistics handler on a <see cref="ProducerBuilder{TKey,TValue}"/> so that
    /// librdkafka statistics (JSON) are forwarded to the provided <paramref name="sink"/>.
    /// </summary>
    /// <typeparam name="TKey">The Kafka record key type of the producer.</typeparam>
    /// <typeparam name="TValue">The Kafka record value type of the producer.</typeparam>
    /// <param name="builder">The producer builder to extend.</param>
    /// <param name="sink">The sink that receives each statistics payload as it is emitted.</param>
    /// <returns>
    /// The same <see cref="ProducerBuilder{TKey,TValue}"/> instance to enable fluent configuration.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> is <see langword="null"/> or
    /// <paramref name="sink"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method calls <see cref="ProducerBuilder{TKey,TValue}.SetStatisticsHandler(System.Action{IProducer{TKey,TValue},string})"/>
    /// and wires the callback to <see cref="KafkaStatisticsSink.Handle(string)"/>.
    /// </para>
    /// <para>
    /// Ensure <c>statistics.interval.ms</c> is configured (e.g., <c>10_000</c>) on the producer
    /// <see cref="ProducerConfig"/>; otherwise no statistics will be produced.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var sink = new KafkaStatisticsSink();
    ///
    /// using var producer = new ProducerBuilder<string, byte[]>(producerConfig)
    ///     .WithNetMetricStatistics(sink)
    ///     .Build();
    ///
    /// // Produce as usual; statistics will be pushed into 'sink'.
    /// await producer.ProduceAsync("audit", new Message<string, byte[]>{ Key = "k", Value = payload });
    /// ]]></code>
    /// </example>
    public static ProducerBuilder<TKey, TValue> WithNetMetricStatistics<TKey, TValue>(
        this ProducerBuilder<TKey, TValue> builder, KafkaStatisticsSink sink)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sink);

        return builder.SetStatisticsHandler((_, json) => sink.Handle(json));
    }
}
