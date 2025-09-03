// <copyright file="KafkaModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Kafka.Configurations;

namespace NetMetric.Kafka.Modules;

/// <summary>
/// Integrates Kafka client statistics sources with the NetMetric framework by wiring
/// client statistics and optional consumer-lag collectors into the module system.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="KafkaModule"/> is the entry point for Kafka-related metrics. It is responsible for:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>Registering collectors for Kafka client statistics via one or more <see cref="IKafkaStatsSource"/> instances.</description>
///   </item>
///   <item>
///     <description>Optionally registering a <see cref="KafkaLagCollector"/> when lag collection is enabled in <see cref="KafkaModuleOptions"/> and a <see cref="IKafkaLagProbe"/> is provided.</description>
///   </item>
///   <item>
///     <description>Participating in module lifecycle notifications via <see cref="IModuleLifecycle"/> to allow custom initialization or cleanup.</description>
///   </item>
/// </list>
/// <para>
/// This module is typically configured once during application startup. The set of sources and options are fixed
/// for the lifetime of the module instance.
/// </para>
/// </remarks>
/// <threadsafety>
/// This type is thread-safe for concurrent use after construction. All dependencies are captured
/// as immutable fields, and <see cref="GetCollectors"/> creates new collector instances per call without
/// shared mutable state.
/// </threadsafety>
/// <example>
/// <para><b>Basic usage</b> — register client statistics collectors from one or more sources:</para>
/// <code language="csharp">
/// var module = new KafkaModule(
///     sources: new[] { new MyKafkaStatsSource(clientId: "orders-producer", clientType: "producer") },
///     factory: metricFactory,
///     options: new KafkaModuleOptions());
///
/// foreach (var collector in module.GetCollectors())
/// {
///     await collector.CollectAsync(CancellationToken.None);
/// }
/// </code>
/// <para><b>Enabling consumer lag collection</b> — add a lag probe and set options:</para>
/// <code language="csharp">
/// var module = new KafkaModule(
///     sources: new[] { new MyKafkaStatsSource("orders-consumer", "consumer") },
///     factory: metricFactory,
///     options: new KafkaModuleOptions {
///         EnableLagCollector = true,
///         ConsumerGroup = "orders-consumer",
///         // other tagging/limit options...
///     },
///     lagProbe: new MyKafkaLagProbe());
///
/// foreach (var collector in module.GetCollectors())
/// {
///     await collector.CollectAsync(CancellationToken.None);
/// }
/// </code>
/// </example>
/// <seealso cref="IKafkaStatsSource"/>
/// <seealso cref="IKafkaLagProbe"/>
/// <seealso cref="KafkaLagCollector"/>
/// <seealso cref="KafkaClientStatsCollector"/>
/// <seealso cref="KafkaModuleOptions"/>
public sealed class KafkaModule : IModule, IModuleLifecycle
{
    private readonly IEnumerable<IKafkaStatsSource> _sources;
    private readonly IKafkaLagProbe? _lagProbe;
    private readonly IMetricFactory _factory;
    private readonly KafkaModuleOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaModule"/> class.
    /// </summary>
    /// <param name="sources">
    /// The Kafka statistics sources from which metrics will be collected.
    /// If <see langword="null"/>, an empty set is used.
    /// </param>
    /// <param name="factory">The metric factory used to create collectors and instruments.</param>
    /// <param name="options">Configuration options that control the behavior of the Kafka module.</param>
    /// <param name="lagProbe">
    /// Optional probe for collecting consumer lag metrics. Provide a value when
    /// <see cref="KafkaModuleOptions.EnableLagCollector"/> is <see langword="true"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="factory"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public KafkaModule(IEnumerable<IKafkaStatsSource> sources, IMetricFactory factory, KafkaModuleOptions options, IKafkaLagProbe? lagProbe = null)
    {
        _sources = sources ?? Array.Empty<IKafkaStatsSource>();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _lagProbe = lagProbe;
    }

    /// <summary>
    /// Gets the logical name of the Kafka module.
    /// </summary>
    /// <value>The constant string <c>"NetMetric.Kafka"</c>.</value>
    public string Name => "NetMetric.Kafka";

    /// <summary>
    /// Creates and returns the metric collectors registered with this module.
    /// </summary>
    /// <returns>
    /// An enumerable of <see cref="IMetricCollector"/> instances representing:
    /// <list type="bullet">
    ///   <item><description>One <see cref="KafkaClientStatsCollector"/> per configured <see cref="IKafkaStatsSource"/>.</description></item>
    ///   <item><description>A single <see cref="KafkaLagCollector"/> when lag collection is enabled and a <see cref="IKafkaLagProbe"/> is provided.</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Each call returns fresh collector instances. Collectors are lightweight and can be
    /// created per-scrape or cached by the caller, depending on hosting patterns.
    /// </remarks>
    public IEnumerable<IMetricCollector> GetCollectors()
    {
        // Collect metrics from Kafka statistics sources
        foreach (var s in _sources)
        {
            yield return new KafkaClientStatsCollector(s, _factory, _options);
        }

        // Optionally collect lag metrics if enabled
        if (_options.EnableLagCollector && _lagProbe is not null)
            yield return new KafkaLagCollector(_lagProbe, _factory, _options);
    }

    /// <summary>
    /// Performs initialization logic for the module.
    /// </summary>
    /// <remarks>
    /// Called once when the module is registered. Override this hook to allocate resources
    /// or perform startup validation. The default implementation is a no-op.
    /// </remarks>
    public void OnInit() { }

    /// <summary>
    /// Invoked immediately before metrics are collected.
    /// </summary>
    /// <remarks>
    /// Use this hook to refresh transient state or reset counters prior to a scrape.
    /// The default implementation is a no-op.
    /// </remarks>
    public void OnBeforeCollect() { }

    /// <summary>
    /// Invoked immediately after metrics are collected.
    /// </summary>
    /// <remarks>
    /// Use this hook to release temporary resources or trigger post-collection actions.
    /// The default implementation is a no-op.
    /// </remarks>
    public void OnAfterCollect() { }

    /// <summary>
    /// Performs cleanup when the module is disposed or deregistered.
    /// </summary>
    /// <remarks>
    /// Called once when the host application shuts down or the module is removed.
    /// The default implementation is a no-op.
    /// </remarks>
    public void OnDispose() { }
}
