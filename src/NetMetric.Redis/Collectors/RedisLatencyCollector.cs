// <copyright file="RedisLatencyCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Redis.Collectors;

/// <summary>
/// Collects and publishes latency metrics for Redis operations based on the server’s latest latency samples.
/// </summary>
/// <remarks>
/// This collector queries each configured Redis endpoint using the <c>LATENCY LATEST</c> command and records
/// observed operation latencies into a summary metric (<c>redis.op.latency_ms</c>) configured with the quantiles
/// 0.50 (median), 0.90 (90th percentile), and 0.99 (99th percentile).
/// </remarks>
internal sealed class RedisLatencyCollector : MetricCollectorBase
{
    private readonly IRedisClient _client;
    private readonly ISummaryMetric _summary;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisLatencyCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metric instruments.</param>
    /// <param name="client">The Redis client used to enumerate endpoints and retrieve latency samples.</param>
    public RedisLatencyCollector(IMetricFactory factory, IRedisClient client)
        : base(factory)
    {
        _client = client;
        _summary = Factory
            .Summary("redis.op.latency_ms", "Redis operation latency (ms)")
            .WithQuantiles(0.5, 0.9, 0.99)
            .Build();
    }

    /// <summary>
    /// Collects latency metrics from all Redis endpoints asynchronously and records them into the configured summary metric.
    /// </summary>
    /// <param name="ct">A token that can be used to observe cancellation requests.</param>
    /// <returns>
    /// A task whose result is the populated summary metric (<see cref="ISummaryMetric"/>).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    public override async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        foreach (var ep in _client.Endpoints())
        {
            ct.ThrowIfCancellationRequested();

            var latest = await _client.LatencyLatestAtAsync(ep, ct).ConfigureAwait(false);

            foreach (var (_, ms) in latest)
            {
                _summary.Record(ms);
            }
        }

        return _summary;
    }
}
