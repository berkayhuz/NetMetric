// <copyright file="RedisPingCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Redis.Collectors;

/// <summary>
/// Collects Redis ping latency and connection status metrics.
/// </summary>
/// <remarks>
/// <para>
/// This collector sends a Redis <c>PING</c> command to the server and tracks:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
/// <c>redis.ping.latency_ms</c>: The round-trip latency of the Redis <c>PING</c> command, measured in milliseconds.
///     </description>
///   </item>
///   <item>
///     <description>
/// <c>redis.connection.ok</c>: A gauge representing whether the Redis connection is responsive:
/// <c>1</c> for active, <c>0</c> for inactive.
///     </description>
///   </item>
/// </list>
/// <para>
/// This collector is useful for monitoring Redis availability and latency health checks.
/// </para>
/// </remarks>
/// <example>
/// The following example demonstrates how to configure and use the <see cref="RedisPingCollector"/> 
/// within a metrics pipeline:
/// <code>
/// IMetricFactory factory = new PrometheusMetricFactory();
/// IRedisClient client = new StackExchangeRedisClient("localhost:6379");
///
/// var pingCollector = new RedisPingCollector(factory, client);
/// 
/// // Collect metrics asynchronously
/// IMetric? metrics = await pingCollector.CollectAsync();
///
/// if (metrics is not null)
/// {
///     Console.WriteLine("Redis ping metrics collected successfully.");
/// }
/// </code>
/// </example>
internal sealed class RedisPingCollector : MetricCollectorBase
{
    private readonly ITimerMetric _timer;
    private readonly IGauge _ok;
    private readonly IRedisClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisPingCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metric objects.</param>
    /// <param name="client">The Redis client used to send <c>PING</c> commands to the Redis server.</param>
    public RedisPingCollector(IMetricFactory factory, IRedisClient client) : base(factory)
    {
        _client = client;
        _timer = Factory.Timer("redis.ping.latency_ms", "Redis ping latency (ms)")
                        .WithHistogramCapacity(1024)
                        .Build();
        _ok = Factory.Gauge("redis.connection.ok", "Redis connection ok (1/0)").Build();
    }

    /// <summary>
    /// Collects the Redis ping latency and connection status asynchronously.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// A task representing the asynchronous collection operation.  
    /// The result contains the timer metric with recorded latency and a gauge indicating connection status.
    /// </returns>
    /// <remarks>
    /// <para>
    /// If the Redis server responds to the <c>PING</c> command, <c>redis.connection.ok</c> will be set to <c>1</c>.  
    /// If the server does not respond, it will be set to <c>0</c>.
    /// </para>
    /// </remarks>
    public override async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        using (_timer.StartMeasurement())
        {
            var ok = await _client.PingAsync(ct).ConfigureAwait(false);
            _ok.SetValue(ok ? 1 : 0);
        }
        return _timer;
    }
}
