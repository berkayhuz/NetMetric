// <copyright file="RedisModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Options;

namespace NetMetric.Redis.Modules;

/// <summary>
/// Represents the Redis metrics module that wires up Redis-related collectors and exposes them
/// to the hosting monitoring pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="RedisModule"/> integrates Redis-specific collectors—such as ping latency,
/// server info, keyspace stats, and slowlog length—based on <see cref="RedisOptions"/>. It does
/// not perform collection itself; instead it composes and returns the concrete
/// <see cref="IMetricCollector"/>s that the hosting system will execute.
/// </para>
/// <para>
/// This module is designed to be lightweight and safely composable. It validates dependencies
/// in the constructor and defers any network operations to the underlying collectors.
/// </para>
/// <para>
/// Thread Safety: Instances are expected to be registered as singletons. The module itself
/// does not hold mutable shared state and is therefore thread-safe. Individual collectors
/// may keep their own state; refer to each collector’s documentation for details.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Composition root (e.g., in a DI setup)
/// services.Configure<RedisOptions>(cfg =>
/// {
///     cfg.EnableLatency = true;
///     cfg.EnableSlowlog = false;
/// });
///
/// services.AddSingleton<IMetricFactory, PromMetricFactory>();
/// services.AddSingleton<IRedisClient, StackExchangeRedisClient>();
/// services.AddSingleton<IModule>(sp =>
/// {
///     var factory = sp.GetRequiredService<IMetricFactory>();
///     var client  = sp.GetRequiredService<IRedisClient>();
///     var opts    = sp.GetRequiredService<IOptions<RedisOptions>>();
///     return new RedisModule(factory, client, opts);
/// });
///
/// // Later in the host:
/// var module = serviceProvider.GetRequiredService<IModule>() as RedisModule;
/// module!.OnInit();
/// foreach (var collector in module.GetCollectors())
/// {
///     // register the collector into the scheduler/runner
/// }
/// ]]></code>
/// </example>
/// <seealso cref="IModule"/>
/// <seealso cref="IModuleLifecycle"/>
/// <seealso cref="IRedisClient"/>
/// <seealso cref="RedisOptions"/>
public sealed class RedisModule : IModule, IModuleLifecycle
{
    private readonly IMetricFactory _factory;
    private readonly IRedisClient _client;
    private readonly RedisOptions _opts;

    /// <summary>
    /// Gets the logical module name used by the hosting system.
    /// </summary>
    /// <value>
    /// The constant string <c>"redis"</c>.
    /// </value>
    public string Name => "redis";

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisModule"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metric primitives (gauges, summaries, timers, etc.).</param>
    /// <param name="client">The Redis client used by collectors to interact with Redis endpoints.</param>
    /// <param name="opts">The options that control which collectors are enabled and how they behave.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="factory"/>, <paramref name="client"/>, or <paramref name="opts"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The constructor validates required dependencies and reads the actual options instance from
    /// <see cref="IOptions{TOptions}.Value"/> to guard against <see langword="null"/> values.
    /// </remarks>
    public RedisModule(IMetricFactory factory, IRedisClient client, IOptions<RedisOptions> opts)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(opts);

        _factory = factory;
        _client = client;
        _opts = opts.Value;
    }

    /// <summary>
    /// Wraps a metric collector with instrumentation that records self-metrics (duration, errors,
    /// last success, last duration, etc.) under a stable name.
    /// </summary>
    /// <param name="name">A short, stable identifier for the collector (e.g., <c>"ping"</c> or <c>"slowlog"</c>).</param>
    /// <param name="inner">The inner <see cref="IMetricCollector"/> to instrument.</param>
    /// <returns>
    /// An <see cref="InstrumentedCollector"/> that decorates <paramref name="inner"/> with self-metrics.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="name"/> or <paramref name="inner"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// This helper centralizes the application of consistent instrumentation across all collectors
    /// produced by the module.
    /// </remarks>
    private InstrumentedCollector Wrap(string name, IMetricCollector inner)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(inner);

        return new InstrumentedCollector(_factory, name, inner);
    }

    /// <summary>
    /// Produces the set of Redis-related metric collectors configured for this module.
    /// </summary>
    /// <returns>
    /// An enumerable sequence of <see cref="IMetricCollector"/> instances, including endpoint visibility,
    /// ping latency, server info, keyspace metrics, and—depending on <see cref="RedisOptions"/>—latency and slowlog collectors.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The returned sequence is lazily constructed. Each collector is wrapped by
    /// <see cref="InstrumentedCollector"/> to expose self-metrics for observability.
    /// </para>
    /// <para>
    /// Enabled Collectors (always):
    /// <list type="bullet">
    /// <item><description><c>endpoints</c>: <see cref="RedisEndpointsCollector"/></description></item>
    /// <item><description><c>ping</c>: <see cref="RedisPingCollector"/></description></item>
    /// <item><description><c>info</c>: <see cref="RedisInfoCollector"/></description></item>
    /// <item><description><c>keyspace</c>: <see cref="RedisKeyspaceCollector"/></description></item>
    /// </list>
    /// Conditionally Enabled:
    /// <list type="bullet">
    /// <item><description><c>latency</c>: <see cref="RedisLatencyCollector"/> when <see cref="RedisOptions.EnableLatency"/> is <see langword="true"/>.</description></item>
    /// <item><description><c>slowlog</c>: <see cref="RedisSlowlogCollector"/> when <see cref="RedisOptions.EnableSlowlog"/> is <see langword="true"/>.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var module = new RedisModule(factory, client, Options.Create(new RedisOptions
    /// {
    ///     EnableLatency = true,
    ///     EnableSlowlog = true
    /// }));
    ///
    /// foreach (var collector in module.GetCollectors())
    /// {
    ///     scheduler.Register(collector); // integrate with your collection runner
    /// }
    /// ]]></code>
    /// </example>
    public IEnumerable<IMetricCollector> GetCollectors()
    {
        // Endpoint visibility (self-probe)
        yield return Wrap("endpoints", new RedisEndpointsCollector(_factory, _client));

        // Redis metrics
        yield return Wrap("ping", new RedisPingCollector(_factory, _client));
        yield return Wrap("info", new RedisInfoCollector(_factory, _client));

        if (_opts.EnableLatency)
            yield return Wrap("latency", new RedisLatencyCollector(_factory, _client));

        yield return Wrap("keyspace", new RedisKeyspaceCollector(_factory, _client));

        if (_opts.EnableSlowlog)
            yield return Wrap("slowlog", new RedisSlowlogCollector(_factory, _client));
    }

    /// <summary>
    /// Called once when the module is initialized by the hosting system.
    /// </summary>
    /// <remarks>
    /// The default implementation is a no-op. Override or extend at the hosting layer if
    /// initialization side-effects are required.
    /// </remarks>
    public void OnInit()
    {
    }

    /// <summary>
    /// Called immediately before a collection cycle begins.
    /// </summary>
    /// <remarks>
    /// The default implementation is a no-op. Hosting systems may invoke this hook for
    /// lifecycle-aware collectors or coordinated pre-collection steps.
    /// </remarks>
    public void OnBeforeCollect()
    {
    }

    /// <summary>
    /// Called immediately after a collection cycle completes.
    /// </summary>
    /// <remarks>
    /// The default implementation is a no-op. Hosting systems may invoke this hook for
    /// post-processing, checkpointing, or emitting additional module-level signals.
    /// </remarks>
    public void OnAfterCollect()
    {
    }

    /// <summary>
    /// Called when the module is being disposed by the hosting system.
    /// </summary>
    /// <remarks>
    /// The default implementation is a no-op. Disposal of network resources is handled by individual
    /// collectors and their dependencies (e.g., <see cref="IRedisClient"/>).
    /// </remarks>
    public void OnDispose()
    {
    }
}
