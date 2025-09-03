// <copyright file="RabbitMqModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.RabbitMQ.Modules;

/// <summary>
/// Provides the RabbitMQ metrics module for the NetMetric framework, wiring together
/// one or more RabbitMQ-specific <see cref="IMetricCollector"/> implementations based on
/// the supplied <see cref="RabbitMqModuleOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// This module is the composition root for RabbitMQ metric collection. At construction time it
/// inspects <see cref="RabbitMqModuleOptions"/> and conditionally instantiates the following collectors:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="ConnectionHealthCollector"/> — emits a gauge representing connection health and status tags.</description></item>
///   <item><description><see cref="ChannelCountCollector"/> — tracks open channel count and negotiated maximums.</description></item>
///   <item><description><see cref="QueueDepthCollector"/> — reports current message depth for configured queues.</description></item>
///   <item><description><see cref="PublishConfirmLatencyCollector"/> — measures publisher-confirm round-trip latency (ms).</description></item>
///   <item><description><see cref="ConsumerRateCollector"/> — estimates per-queue consumer throughput (messages/s).</description></item>
/// </list>
/// <para>
/// The actual set of active collectors depends on the boolean flags and queue list provided in
/// <see cref="RabbitMqModuleOptions"/>. If a feature is disabled or the prerequisite configuration
/// (e.g., <see cref="RabbitMqModuleOptions.QueueNames"/>) is missing, the corresponding collector is omitted.
/// </para>
/// <threadsafety>
/// Instances of <see cref="RabbitMqModule"/> are immutable after construction. All public members are
/// safe to call concurrently from multiple threads assuming the injected dependencies are thread-safe.
/// </threadsafety>
/// </remarks>
/// <example>
/// The following example shows how to register the RabbitMQ module with NetMetric and enable a subset of collectors:
/// <code language="csharp"><![CDATA[
/// var services = new ServiceCollection();
///
/// services.AddNetMetric(builder =>
/// {
///     builder.AddModule(new RabbitMqModule(
///         factory: builder.MetricFactory,
///         options: new RabbitMqModuleOptions
///         {
///             MetricPrefix = "rabbitmq",
///             EnableConnectionHealth = true,
///             EnableChannelCount = true,
///             EnableQueueDepth = true,
///             QueueNames = new[] { "orders", "billing" },
///             EnablePublishConfirmLatency = true,
///             EnableConsumerRate = false
///         },
///         provider: new DefaultRabbitMqConnectionProvider(connectionString)));
/// });
/// ]]></code>
/// </example>
/// <seealso cref="ConnectionHealthCollector"/>
/// <seealso cref="ChannelCountCollector"/>
/// <seealso cref="QueueDepthCollector"/>
/// <seealso cref="PublishConfirmLatencyCollector"/>
/// <seealso cref="ConsumerRateCollector"/>
public sealed class RabbitMqModule : IModule, IModuleLifecycle, IDisposable
{
    /// <summary>
    /// The canonical module name used by the NetMetric runtime and logs to identify the RabbitMQ module.
    /// </summary>
    public const string ModuleName = "NetMetric.RabbitMQ";

    /// <summary>
    /// Gets the module name reported to the NetMetric runtime.
    /// </summary>
    /// <value>The constant value <c>"NetMetric.RabbitMQ"</c>.</value>
    public string Name => ModuleName;

    private readonly ImmutableArray<IMetricCollector> _collectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqModule"/> class.
    /// </summary>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to create metric instruments.</param>
    /// <param name="options">The options that control which collectors are enabled and how they are configured. If <c>null</c>, defaults are used.</param>
    /// <param name="provider">The RabbitMQ connection provider used by collectors to access the broker.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="factory"/> is <c>null</c> or when <paramref name="provider"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The constructor performs only composition and does not initiate any network calls. Collectors are instantiated
    /// eagerly so that misconfiguration surfaces early during application startup.
    /// </para>
    /// <para>
    /// The following option flags influence which collectors are included:
    /// </para>
    /// <list type="table">
    ///   <listheader>
    ///     <term>Option</term><description>Effect</description>
    ///   </listheader>
    ///   <item><term><see cref="RabbitMqModuleOptions.EnableConnectionHealth"/></term><description>Adds <see cref="ConnectionHealthCollector"/>.</description></item>
    ///   <item><term><see cref="RabbitMqModuleOptions.EnableChannelCount"/></term><description>Adds <see cref="ChannelCountCollector"/>.</description></item>
    ///   <item><term><see cref="RabbitMqModuleOptions.EnableQueueDepth"/></term><description>Adds <see cref="QueueDepthCollector"/> if <see cref="RabbitMqModuleOptions.QueueNames"/> is non-empty.</description></item>
    ///   <item><term><see cref="RabbitMqModuleOptions.EnablePublishConfirmLatency"/></term><description>Adds <see cref="PublishConfirmLatencyCollector"/>.</description></item>
    ///   <item><term><see cref="RabbitMqModuleOptions.EnableConsumerRate"/></term><description>Adds <see cref="ConsumerRateCollector"/> if <see cref="RabbitMqModuleOptions.QueueNames"/> is non-empty.</description></item>
    /// </list>
    /// </remarks>
    public RabbitMqModule(
        IMetricFactory factory,
        RabbitMqModuleOptions? options,
        IRabbitMqConnectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(provider);

        options ??= new RabbitMqModuleOptions();

        var prefix = string.IsNullOrWhiteSpace(options.MetricPrefix) ? "rabbitmq" : options.MetricPrefix!;
        var list = new List<IMetricCollector>(capacity: 8);

        if (options.EnableConnectionHealth)
        {
            list.Add(new ConnectionHealthCollector(factory, provider));
        }

        if (options.EnableChannelCount)
        {
            list.Add(new ChannelCountCollector(factory, provider));
        }

        if (options.EnableQueueDepth && options.QueueNames is { Count: > 0 })
        {
            list.Add(new QueueDepthCollector(factory, provider, options.QueueNames));
        }

        if (options.EnablePublishConfirmLatency)
        {
            list.Add(new PublishConfirmLatencyCollector(factory, provider, prefix));
        }

        if (options.EnableConsumerRate && options.QueueNames is { Count: > 0 })
        {
            list.Add(new ConsumerRateCollector(factory, provider, prefix, options.QueueNames));
        }

        _collectors = list.ToImmutableArray();
    }

    /// <summary>
    /// Returns the concrete set of <see cref="IMetricCollector"/> instances composed by this module.
    /// </summary>
    /// <returns>
    /// An enumeration of collectors in a stable order. The returned sequence is safe to iterate multiple
    /// times and reflects the collectors that were enabled at construction.
    /// </returns>
    /// <example>
    /// You can enumerate the collectors to register them with a scheduler:
    /// <code language="csharp"><![CDATA[
    /// foreach (var collector in rabbitMqModule.GetCollectors())
    /// {
    ///     scheduler.Register(collector);
    /// }
    /// ]]></code>
    /// </example>
    public IEnumerable<IMetricCollector> GetCollectors() => _collectors;

    /// <summary>
    /// Called by the hosting runtime when the module is initialized.
    /// </summary>
    /// <remarks>
    /// This implementation is a no-op because collectors do not require additional initialization beyond construction.
    /// </remarks>
    public void OnInit() { }

    /// <summary>
    /// Called immediately before a collection cycle begins.
    /// </summary>
    /// <remarks>
    /// This implementation is a no-op. Override or extend in derived modules if you need per-cycle prework.
    /// </remarks>
    public void OnBeforeCollect() { }

    /// <summary>
    /// Called immediately after a collection cycle completes.
    /// </summary>
    /// <remarks>
    /// This implementation is a no-op. Override or extend in derived modules if you need per-cycle cleanup.
    /// </remarks>
    public void OnAfterCollect() { }

    /// <summary>
    /// Called by the hosting runtime when the module is being disposed.
    /// </summary>
    /// <remarks>
    /// This implementation is a no-op to avoid double disposal, as the module does not own disposable resources
    /// beyond what <see cref="Dispose"/> may release in future revisions.
    /// </remarks>
    public void OnDispose() { }

    /// <summary>
    /// Releases resources held by the module.
    /// </summary>
    /// <remarks>
    /// The current implementation does not own unmanaged resources and therefore performs no action.
    /// This method exists to satisfy <see cref="IDisposable"/> and to allow future extensions without
    /// breaking the public surface.
    /// </remarks>
    public void Dispose() { }
}
