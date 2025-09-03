// <copyright file="KafkaModuleOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kafka.Configurations;

/// <summary>
/// Provides configuration options for the Kafka metrics module.
/// </summary>
/// <remarks>
/// <para>
/// These options control how Kafka-related metrics are collected, tagged, and emitted.
/// They can be bound from configuration sources (for example, <c>appsettings.json</c> or
/// environment variables) or set programmatically when registering the Kafka module.
/// </para>
/// <para>
/// <b>Typical scenarios</b>
/// </para>
/// <list type="bullet">
///   <item><description>Attach base tags (for example, <c>cluster=prod</c>, <c>region=eu-west-1</c>) for scoping and filtering.</description></item>
///   <item><description>Enable and configure the consumer lag collector.</description></item>
///   <item><description>Tune sampling intervals and staleness thresholds for probes.</description></item>
///   <item><description>Limit metric cardinality for per-partition lag via <see cref="MaxLagSeries"/>.</description></item>
/// </list>
/// <para>
/// Unless otherwise noted, properties are immutable after initialization and may be safely shared across threads.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // appsettings.json
/// // {
/// //   "NetMetric:Kafka": {
/// //     "EnableLagCollector": true,
/// //     "ConsumerGroup": "orders-consumer",
/// //     "SamplePeriod": "00:00:05",
/// //     "StatsStaleAfter": "00:00:20",
/// //     "MaxLagSeries": 100,
/// //     "BaseTags": {
/// //       "cluster": "prod",
/// //       "region": "eu-west-1"
/// //     }
/// //   }
/// // }
///
/// // Program.cs
/// builder.Services.Configure&lt;KafkaModuleOptions&gt;(builder.Configuration.GetSection("NetMetric:Kafka"));
///
/// // Or configure programmatically
/// builder.Services.PostConfigure&lt;KafkaModuleOptions&gt;(o =&gt; new KafkaModuleOptions {
///     BaseTags = new Dictionary&lt;string,string&gt; {
///         ["cluster"] = "prod",
///         ["region"] = "eu-west-1"
///     },
///     EnableLagCollector = true,
///     ConsumerGroup = "orders-consumer",
///     SamplePeriod = TimeSpan.FromSeconds(5),
///     StatsStaleAfter = TimeSpan.FromSeconds(20),
///     MaxLagSeries = 100
/// });
/// </code>
/// </example>
/// <seealso cref="IKafkaLagProbe"/>
/// <seealso cref="IMetricWindowPolicy"/>
public sealed class KafkaModuleOptions
{
    /// <summary>
    /// Gets or sets the base tags that are appended to all Kafka metrics emitted by this module.
    /// </summary>
    /// <remarks>
    /// Tags can be used for scoping, grouping, or filtering metrics (for example, cluster name or environment).
    /// If <see langword="null"/>, no global tags are applied.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? BaseTags { get; init; }

    /// <summary>
    /// Gets or sets the metric windowing policy used for throughput summaries (if enabled).
    /// </summary>
    /// <remarks>
    /// This policy defines how data points are aggregated over time. If <see langword="null"/>,
    /// the default windowing policy is applied by the module.
    /// </remarks>
    public IMetricWindowPolicy? Window { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the consumer lag collector is enabled.
    /// </summary>
    /// <remarks>
    /// When enabled, an implementation of <see cref="IKafkaLagProbe"/> must be available in the dependency injection (DI) container.
    /// </remarks>
    public bool EnableLagCollector { get; init; }

    /// <summary>
    /// Gets or sets the consumer group to monitor when <see cref="EnableLagCollector"/> is enabled.
    /// </summary>
    /// <remarks>
    /// If not set, the lag collector does not emit any metrics. This value should match the Kafka consumer group identifier.
    /// </remarks>
    public string? ConsumerGroup { get; init; }

    /// <summary>
    /// Gets or sets the interval between consecutive metric collection operations.
    /// </summary>
    /// <value>
    /// The default is <c>00:00:10</c> (10 seconds).
    /// </value>
    public TimeSpan SamplePeriod { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the duration after which collected statistics are considered stale.
    /// </summary>
    /// <value>
    /// The default is <c>00:00:30</c> (30 seconds).
    /// </value>
    /// <remarks>
    /// If the age of the last snapshot exceeds this threshold, the metrics are flagged as stale by the collector.
    /// </remarks>
    public TimeSpan StatsStaleAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum number of per-partition lag series to emit.
    /// </summary>
    /// <value>
    /// The default is <c>200</c>.
    /// </value>
    /// <remarks>
    /// This limit helps prevent cardinality explosions by bounding the number of topic/partition lag time series.
    /// Excess partitions beyond this limit may be truncated according to the module's selection policy.
    /// </remarks>
    public int MaxLagSeries { get; init; } = 200;
}
