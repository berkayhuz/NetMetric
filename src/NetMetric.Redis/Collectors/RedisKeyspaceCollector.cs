// <copyright file="RedisKeyspaceCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Redis.Collectors;

/// <summary>
/// Collects Redis keyspace statistics per database and per node, including the total number of keys
/// and the number of keys that have a time-to-live (TTL) set.
/// </summary>
/// <remarks>
/// <para>
/// This collector queries each configured Redis endpoint and parses the output of the
/// <c>INFO all</c> and <c>INFO keyspace</c> commands to produce keyspace metrics.
/// For every database (e.g., <c>db0</c>, <c>db1</c>), it emits two sibling metrics:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><c>redis.keys.total</c> — The total number of keys in the database.</description>
///   </item>
///   <item>
///     <description><c>redis.expires.total</c> — The number of keys that have a TTL in the database.</description>
///   </item>
/// </list>
/// <para>
/// Each metric is tagged with:
/// </para>
/// <list type="table">
///   <listheader>
///     <term>Tag</term>
///     <description>Description</description>
///   </listheader>
///   <item>
///     <term><c>db</c></term>
///     <description>The Redis database name (e.g., <c>db0</c>).</description>
///   </item>
///   <item>
///     <term><c>node</c></term>
///     <description>The endpoint string identifying the Redis node.</description>
///   </item>
///   <item>
///     <term><c>role</c></term>
///     <description>The node role as reported by <c>INFO all</c> (e.g., <c>master</c>, <c>replica</c>).</description>
///   </item>
/// </list>
/// </remarks>
/// <example>
/// <para><strong>Basic usage</strong></para>
/// <code language="csharp"><![CDATA[
/// var factory = /* resolve IMetricFactory */;
/// var client  = /* resolve IRedisClient   */;
///
/// var collector = new RedisKeyspaceCollector(factory, client);
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
///
/// // Collect metrics once (e.g., inside a scheduled job)
/// var metric = await collector.CollectAsync(cts.Token);
/// // The returned IMultiGauge will include siblings for each db/node.
/// ]]></code>
/// </example>
internal sealed class RedisKeyspaceCollector : MetricCollectorBase
{
    private readonly IRedisClient _client;
    private readonly IMultiGauge _mg;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisKeyspaceCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metric objects.</param>
    /// <param name="client">The Redis client used to query endpoints and execute INFO commands.</param>
    /// <remarks>
    /// The constructor creates an <see cref="IMultiGauge"/> with <c>WithResetOnGet(true)</c> so that each collection
    /// represents a fresh snapshot of keyspace values.
    /// </remarks>
    public RedisKeyspaceCollector(IMetricFactory factory, IRedisClient client) : base(factory)
    {
        _client = client;
        _mg = Factory
            .MultiGauge("redis.keyspace", "Redis keyspace per DB/node")
            .WithResetOnGet(true)
            .Build();
    }

    /// <summary>
    /// Collects Redis keyspace metrics asynchronously for all configured endpoints.
    /// </summary>
    /// <param name="ct">A token that can be used to observe cancellation requests.</param>
    /// <returns>
    /// A task that, when completed successfully, returns the populated <see cref="IMetric"/> instance
    /// (specifically, the <see cref="IMultiGauge"/> created by this collector).
    /// </returns>
    /// <remarks>
    /// <para>
    /// For each endpoint, this method:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>Queries <c>INFO all</c> to determine the node <c>role</c> and derive the <c>node</c> tag.</description>
    ///   </item>
    ///   <item>
    ///     <description>Queries <c>INFO keyspace</c> and parses lines in the form <c>dbN:keys=K,expires=E,...</c>.</description>
    ///   </item>
    ///   <item>
    ///     <description>Emits two metrics per database: <c>redis.keys.total</c> and <c>redis.expires.total</c>.</description>
    ///   </item>
    /// </list>
    /// <para>
    /// Lines that do not match the expected format are skipped. Endpoints that return empty results are also skipped.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    public override async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        foreach (var ep in _client.Endpoints())
        {
            var infoAll = await _client.InfoAsyncAt("all", ep, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(infoAll))
            {
                continue;
            }

            // Attempts to retrieve the node role (e.g., "master", "replica"). Falls back to "unknown".
            var role = TryFindValue(infoAll.AsSpan(), "role").ToString() ?? "unknown";
            var nodeName = ep.ToString();

            var ks = await _client.InfoAsyncAt("keyspace", ep, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(ks))
            {
                continue;
            }

            var span = ks.AsSpan();
            int start = 0;

            for (int i = 0; i <= span.Length; i++)
            {
                if (i == span.Length || span[i] == '\n')
                {
                    var line = span.Slice(start, i - start).Trim();
                    start = i + 1;

                    if (line.IsEmpty || !line.StartsWith("db"))
                    {
                        continue;
                    }

                    int colon = line.IndexOf(':');
                    if (colon <= 0)
                    {
                        continue;
                    }

                    var db = line.Slice(0, colon).ToString();
                    var stats = line.Slice(colon + 1);

                    long keys = GetNum(stats, "keys");
                    long expires = GetNum(stats, "expires");

                    ArgumentNullException.ThrowIfNull(db);
                    ArgumentNullException.ThrowIfNull(nodeName);
                    ArgumentNullException.ThrowIfNull(role);

                    var tags = new Dictionary<string, string>(3)
                    {
                        ["db"] = db,
                        ["node"] = nodeName,
                        ["role"] = role,
                    };

                    _mg.AddSibling("redis.keys.total", "Total keys", keys, tags);
                    _mg.AddSibling("redis.expires.total", "Keys with TTL", expires, tags);
                }
            }
        }
        return _mg;
    }

    /// <summary>
    /// Extracts a numeric value from a comma-separated list of <c>key=value</c> pairs.
    /// </summary>
    /// <param name="csv">The span containing the <c>key=value</c> pairs (e.g., <c>keys=42,expires=7,avg_ttl=...</c>).</param>
    /// <param name="wantKey">The key whose numeric value should be retrieved (e.g., <c>"keys"</c> or <c>"expires"</c>).</param>
    /// <returns>
    /// The parsed <see cref="long"/> value if found and valid; otherwise <c>0</c>.
    /// </returns>
    /// <remarks>
    /// This method is resilient to extra spaces and to missing keys. If the key is not present or the value
    /// cannot be parsed as an integer, it returns <c>0</c>.
    /// </remarks>
    private static long GetNum(ReadOnlySpan<char> csv, ReadOnlySpan<char> wantKey)
    {
        int i = 0;

        while (i < csv.Length)
        {
            // Parse key
            int eqRel = csv.Slice(i).IndexOf('=');
            if (eqRel < 0)
            {
                break;
            }

            var key = csv.Slice(i, eqRel).Trim();

            // Parse value
            int vStart = i + eqRel + 1;
            int commaRel = csv.Slice(vStart).IndexOf(',');

            ReadOnlySpan<char> val = commaRel < 0 ? csv.Slice(vStart) : csv.Slice(vStart, commaRel);

            i = commaRel < 0 ? csv.Length : vStart + commaRel + 1;

            if (key.SequenceEqual(wantKey))
            {
                if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
                {
                    return num;
                }

                return 0;
            }
        }
        return 0;
    }

    /// <summary>
    /// Searches the <c>INFO all</c> payload for a line in the form <c>key:value</c> and returns the value portion.
    /// </summary>
    /// <param name="infoAll">The span containing the full <c>INFO all</c> response.</param>
    /// <param name="wantKey">The key to look for (e.g., <c>"role"</c>).</param>
    /// <returns>
    /// A <see cref="ReadOnlySpan{T}"/> containing the value associated with <paramref name="wantKey"/>,
    /// or an empty span if the key is not found.
    /// </returns>
    /// <remarks>
    /// Lines that start with <c>#</c> (section headers) are ignored. Whitespace is trimmed from both key and value.
    /// </remarks>
    private static ReadOnlySpan<char> TryFindValue(ReadOnlySpan<char> infoAll, ReadOnlySpan<char> wantKey)
    {
        int start = 0;
        for (int i = 0; i <= infoAll.Length; i++)
        {
            if (i == infoAll.Length || infoAll[i] == '\n')
            {
                var line = infoAll.Slice(start, i - start).Trim();
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

                if (key.SequenceEqual(wantKey))
                {
                    return line.Slice(colon + 1).Trim();
                }
            }
        }

        // Not found: return an empty span
        return null;
    }
}
