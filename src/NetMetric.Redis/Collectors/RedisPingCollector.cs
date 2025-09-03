// <copyright file="RedisSlowlogCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Redis.Collectors;

/// <summary>
/// Collects and exposes the total length of the Redis <c>SLOWLOG</c> across all configured Redis nodes.
/// </summary>
/// <remarks>
/// <para>
/// This collector iterates over all Redis endpoints provided by the client and queries each node for its slowlog
/// length (via the client's <c>SlowlogLenAtAsync</c> API). Individual lengths are summed and emitted as a single
/// gauge metric.
/// </para>
/// <list type="bullet">
///   <item><description><c>redis.slowlog.len</c> — Total <c>SLOWLOG</c> length aggregated across all nodes.</description></item>
/// </list>
/// <para>
/// A <see langword="null"/> length from a node (e.g., a transiently unreachable node) is ignored to keep the
/// collection resilient.
/// </para>
/// <para><b>Performance</b>: the underlying Redis command is <c>SLOWLOG LEN</c>, which is constant-time and does not
/// fetch entries, so overhead is minimal even on large clusters.
/// </para>
/// </remarks>
internal sealed class RedisSlowlogCollector : MetricCollectorBase
{
    private readonly IGauge _len;
    private readonly IRedisClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisSlowlogCollector"/> class.
    /// </summary>
    /// <param name="factory">Metric factory used to create the output gauge.</param>
    /// <param name="client">Redis client used to enumerate endpoints and query per-node slowlog lengths.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> or <paramref name="client"/> is <see langword="null"/>.</exception>
    public RedisSlowlogCollector(IMetricFactory factory, IRedisClient client)
        : base(factory ?? throw new ArgumentNullException(nameof(factory)))
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _len = Factory.Gauge("redis.slowlog.len", "Slowlog length (sum of nodes)").Build();
    }

    /// <summary>
    /// Collects the aggregated Redis slowlog length across all configured endpoints.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe while the operation completes.</param>
    /// <returns>
    /// The gauge metric instance updated with the current aggregated slowlog length.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    public override async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        long total = 0;

        foreach (var ep in _client.Endpoints())
        {
            // Note: SlowlogLenAtAsync may return null for an endpoint that cannot be queried.
            var len = await _client.SlowlogLenAtAsync(ep, ct).ConfigureAwait(false);
            if (len is not null)
                total += len.Value;
        }

        _len.SetValue(total);
        return _len;
    }
}
