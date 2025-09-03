// <copyright file="RedisEndpointsCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using StackExchange.Redis;

namespace NetMetric.Redis.Collectors;

/// <summary>
/// Reports the number of discovered and reachable Redis endpoints.
/// </summary>
/// <remarks>
/// <para>
/// This collector emits two gauge metrics describing the topology health of Redis endpoints exposed by an <see cref="IRedisClient"/>:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><c>redis.endpoints.discovered_total</c> – Total number of endpoints returned by <see cref="IRedisClient.Endpoints()"/> at collection time.</description>
///   </item>
///   <item>
///     <description><c>redis.endpoints.reachable_total</c> – Number of endpoints considered <em>reachable</em>, where reachability is defined as receiving a non-empty response to <c>INFO server</c>.</description>
///   </item>
/// </list>
/// <para>
/// Reachability is determined by invoking <see cref="IRedisClient.InfoAsyncAt"/> with the section name <c>"server"</c> for each endpoint.
/// If the returned string is not null or empty, the endpoint is counted as reachable.
/// </para>
/// <para>
/// <strong>Behavior:</strong> The primary metric returned from <see cref="CollectAsync(System.Threading.CancellationToken)"/> is
/// <c>redis.endpoints.discovered_total</c> (as an <see cref="IGauge"/>). The companion gauge <c>redis.endpoints.reachable_total</c>
/// is updated as a side effect during collection.
/// </para>
/// <para>
/// <strong>Thread-safety:</strong> Instances are intended to be resolved once and reused by the metrics pipeline. The collector does not hold mutable shared state
/// beyond the underlying metric instruments, which are expected to be thread-safe per the <see cref="IMetricFactory"/> contract. The provided <see cref="IRedisClient"/> implementation
/// must be safe to call concurrently for <c>Endpoints()</c> and <c>InfoAsyncAt</c>.
/// </para>
/// <para>
/// <strong>Performance:</strong> The collection time scales linearly with the number of endpoints. Each endpoint triggers one <c>INFO server</c> call.
/// Consider the collection interval and Redis timeouts accordingly for large fleets.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Composition root (e.g., DI registration)
/// services.AddSingleton<IRedisClient, StackExchangeRedisClient>();
/// services.AddSingleton<IMetricFactory>(sp => /* your factory */);
/// services.AddSingleton<IMetricCollector, RedisEndpointsCollector>();
///
/// // Sample collector invocation
/// var collector = sp.GetRequiredService<IMetricCollector>();
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
/// var metric = await collector.CollectAsync(cts.Token);
///
/// // Emitted metrics:
/// //   redis.endpoints.discovered_total  <n>
/// //   redis.endpoints.reachable_total   <m>
/// ]]></code>
/// </example>
/// <seealso cref="IRedisClient"/>
/// <seealso cref="IMetricFactory"/>
/// <seealso cref="IGauge"/>
internal sealed class RedisEndpointsCollector : MetricCollectorBase
{
    private readonly IRedisClient _client;
    private readonly IGauge _discovered;
    private readonly IGauge _reachable;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisEndpointsCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metric instruments (gauges).</param>
    /// <param name="client">The Redis client used to enumerate endpoints and query <c>INFO</c>.</param>
    /// <remarks>
    /// This constructor creates two gauges:
    /// <list type="bullet">
    ///   <item><description><c>redis.endpoints.discovered_total</c> – Total endpoints discovered.</description></item>
    ///   <item><description><c>redis.endpoints.reachable_total</c> – Total endpoints with a successful <c>INFO server</c> response.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="factory"/> or <paramref name="client"/> is <see langword="null"/>.
    /// </exception>
    public RedisEndpointsCollector(IMetricFactory factory, IRedisClient client) : base(factory)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _discovered = Factory
            .Gauge("redis.endpoints.discovered_total", "Discovered endpoints (total)")
            .Build();
        _reachable = Factory
            .Gauge("redis.endpoints.reachable_total", "Reachable endpoints (INFO ok)")
            .Build();
    }

    /// <summary>
    /// Collects the number of discovered and reachable Redis endpoints asynchronously.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// A task that, when completed successfully, yields the primary metric instrument
    /// (<see cref="IGauge"/> for <c>redis.endpoints.discovered_total</c>) whose value has been updated
    /// for this collection. The companion gauge <c>redis.endpoints.reachable_total</c> is updated as a side effect.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The method first enumerates endpoints via <see cref="IRedisClient.Endpoints()"/>, then for each endpoint calls
    /// <see cref="IRedisClient.InfoAsyncAt"/> with the <c>"server"</c> section.
    /// A non-empty response marks the endpoint as reachable.
    /// </para>
    /// <para>
    /// The returned instrument can be exported or further processed by your metrics pipeline.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is signaled before or during the operation.</exception>
    /// <exception cref="TimeoutException">
    /// May be thrown by the underlying Redis client if an <c>INFO</c> command times out.
    /// </exception>
    /// <exception cref="RedisException">
    /// May be thrown by the underlying Redis client if the server is unavailable or the command fails.
    /// </exception>
    public override async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var eps = _client.Endpoints();

        _discovered.SetValue(eps.Count);

        var ok = 0;
        foreach (var ep in eps)
        {
            // Check if INFO 'server' returns a valid response to determine if the endpoint is reachable
            var info = await _client.InfoAsyncAt("server", ep, ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(info))
            {
                ok++;
            }
        }

        _reachable.SetValue(ok);

        return _discovered;
    }
}
