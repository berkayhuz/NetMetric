// <copyright file="RedisInfoCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Redis.Collectors;

/// <summary>
/// Collects core Redis server information across all configured endpoints and exposes them as gauges.
/// </summary>
/// <remarks>
/// <para>This collector queries each known Redis endpoint via <see cref="IRedisClient"/> and parses the raw output of the
/// <c>INFO all</c> command to produce a small set of normalized metrics.</para>
/// <list type="bullet">
///   <item><description><c>redis.clients.connected</c>: Sum of <c>connected_clients</c> across all endpoints.</description></item>
/// </list>
/// <para>The parser operates on <see cref="System.ReadOnlySpan{T}"/> of <see cref="char"/> to minimize allocations and iterates
/// line-by-line, ignoring empty and comment lines (those starting with <c>#</c>). Lines are expected in the <c>key:value</c> form;
/// non-conforming lines are skipped defensively.</para>
/// <para>Metric values are written to their respective <see cref="IGauge"/> instances at the end of a collection pass, ensuring a
/// consistent snapshot across all gauges.</para>
/// </remarks>
/// <threadsafety>
/// Instances are safe to use concurrently provided the underlying <see cref="IMetricFactory"/> and <see cref="IRedisClient"/>
/// implementations are thread-safe. This type itself does not maintain mutable shared state beyond metric instruments.
/// </threadsafety>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Using dependency injection
/// var services = new ServiceCollection();
/// // ... register IMetricFactory and IRedisClient ...
/// var provider = services.BuildServiceProvider();
///
/// var factory = provider.GetRequiredService<IMetricFactory>();
/// var redisClient = provider.GetRequiredService<IRedisClient>();
///
/// var collector = new RedisInfoCollector(factory, redisClient);
/// await collector.CollectAsync(CancellationToken.None);
///
/// // Exported gauges:
/// //   redis.clients.connected
/// //   redis.uptime.seconds
/// //   redis.mem.used_bytes
/// //   redis.mem.rss_bytes
/// ]]></code>
/// </example>
/// <seealso cref="MetricCollectorBase"/>
/// <seealso cref="IRedisClient"/>
internal sealed class RedisInfoCollector : MetricCollectorBase
{
    private readonly IGauge _clients;
    private readonly IGauge _uptimeSec;
    private readonly IGauge _memUsed;
    private readonly IGauge _memRss;
    private readonly IRedisClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisInfoCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create and build gauge instruments.</param>
    /// <param name="client">The Redis client used to enumerate endpoints and execute <c>INFO</c> commands.</param>
    /// <remarks>
    /// The constructor eagerly creates the backing <see cref="IGauge"/> instruments using the provided <paramref name="factory"/>.
    /// Metric names and help text are stable and follow a <c>redis.*</c> naming convention suitable for most monitoring backends.
    /// </remarks>
    public RedisInfoCollector(IMetricFactory factory, IRedisClient client) : base(factory)
    {
        _client = client;
        _clients = Factory.Gauge("redis.clients.connected", "Connected clients").Build();
        _uptimeSec = Factory.Gauge("redis.uptime.seconds", "Uptime seconds (min of nodes)").Build();
        _memUsed = Factory.Gauge("redis.mem.used_bytes", "Used memory (bytes, sum)").Build();
        _memRss = Factory.Gauge("redis.mem.rss_bytes", "RSS memory (bytes, sum)").Build();
    }

    /// <summary>
    /// Collects Redis client count, uptime, and memory utilization metrics from all endpoints asynchronously.
    /// </summary>
    /// <param name="ct">A token to observe for cancellation while querying endpoints and parsing results.</param>
    /// <returns>
    /// A task representing the asynchronous operation, whose result is the primary metric
    /// (the <c>redis.clients.connected</c> gauge) updated as part of this collection pass.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For each endpoint returned by <see cref="IRedisClient.Endpoints"/>, this method invokes
    /// <see cref="IRedisClient.InfoAsyncAt"/> with the section <c>"all"</c>.
    /// If an endpoint returns an empty or null payload, it is skipped without affecting other endpoints.
    /// </para>
    /// <para>
    /// After iterating all endpoints, gauges are updated in the following order: clients, uptime, used memory, RSS memory.
    /// If no endpoint produced an uptime value, the uptime gauge is set to <c>0</c>.
    /// </para>
    /// <para>
    /// This method is resilient to partially malformed <c>INFO</c> lines; only recognized keys are parsed, and numeric
    /// conversions are performed using <see cref="System.Globalization.CultureInfo.InvariantCulture"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown if the operation is canceled via <paramref name="ct"/> while awaiting responses from Redis.
    /// </exception>
    public override async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        long clients = 0, used = 0, rss = 0;
        long minUptime = long.MaxValue;

        foreach (var ep in _client.Endpoints())
        {
            var info = await _client.InfoAsyncAt("all", ep, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(info))
            {
                continue;
            }

            var span = info.AsSpan();

            int start = 0;

            for (int i = 0; i <= span.Length; i++)
            {
                if (i == span.Length || span[i] == '\n')
                {
                    var line = span.Slice(start, i - start).Trim();

                    start = i + 1;

                    if (line.IsEmpty || line[0] == '#')
                    {
                        continue;
                    }

                    int colon = line.IndexOf(':');

                    if (colon <= 0)
                    {
                        continue;
                    }

                    var key = line.Slice(0, colon);
                    var val = line.Slice(colon + 1);

                    if (key.SequenceEqual("connected_clients"))
                    {
                        if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cc))
                        {
                            clients += cc;
                        }
                    }
                    else if (key.SequenceEqual("uptime_in_seconds"))
                    {
                        if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var up))
                        {
                            minUptime = Math.Min(minUptime, up);
                        }
                    }
                    else if (key.SequenceEqual("used_memory"))
                    {
                        if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var um))
                        {
                            used += um;
                        }
                    }
                    else if (key.SequenceEqual("used_memory_rss"))
                    {
                        if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ur))
                        {
                            rss += ur;
                        }
                    }
                }
            }
        }

        _clients.SetValue(clients);
        _uptimeSec.SetValue(minUptime == long.MaxValue ? 0 : minUptime);
        _memUsed.SetValue(used);
        _memRss.SetValue(rss);

        return _clients;
    }
}
