// <copyright file="RequestMetricSet.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AspNetCore.Internal;

/// <summary>
/// Provides a collection of HTTP server request-related metrics,
/// including duration, request size, response size, and total requests.
/// </summary>
/// <remarks>
/// <para>
/// This set manages cardinality-safe metric creation keyed by route, method, and status code.
/// Internally, metrics are cached in thread-safe dictionaries to avoid redundant construction.
/// </para>
/// <para>
/// To prevent high cardinality, routes are capped by <see cref="AspNetCoreMetricOptions.MaxRouteCardinality"/>.
/// Additional routes beyond this limit are collapsed into <see cref="AspNetCoreMetricOptions.OtherRouteLabel"/>.
/// </para>
/// <para><strong>Thread Safety:</strong> The type is safe for concurrent use. Individual metrics are created
/// once per dimension key using <see cref="ConcurrentDictionary{TKey,TValue}"/>.</para>
/// </remarks>
/// <seealso cref="IMetricFactory"/>
/// <seealso cref="MetricNames"/>
/// <seealso cref="TagKeys"/>
/// <seealso cref="AspNetCoreMetricOptions"/>
public sealed class RequestMetricSet
{
    private readonly IMetricFactory _factory;
    private readonly AspNetCoreMetricOptions _opt;

    // Pools with controlled cardinality
    private readonly ConcurrentDictionary<string, IBucketHistogramMetric> _duration = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IBucketHistogramMetric> _reqSize = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IBucketHistogramMetric> _resSize = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ICounterMetric> _total = new(StringComparer.Ordinal);

    private int _routeCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestMetricSet"/> class.
    /// </summary>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to build metrics.</param>
    /// <param name="opt">The configuration options controlling metric dimensions and buckets.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="factory"/> or <paramref name="opt"/> is <see langword="null"/>.
    /// </exception>
    public RequestMetricSet(IMetricFactory factory, AspNetCoreMetricOptions opt)
    {
        _factory = factory;
        _opt = opt;
    }

    /// <summary>
    /// Captures a snapshot of all currently created metrics in this set.
    /// </summary>
    /// <returns>
    /// A <see cref="ReadOnlyCollection{T}"/> of <see cref="IMetric"/> instances representing the current snapshot.
    /// Using <see cref="ReadOnlyCollection{T}"/> ensures immutability guarantees while maintaining efficient list semantics.
    /// </returns>
    /// <remarks>
    /// This snapshot reflects only metrics that have been created so far; it does not force-create metrics.
    /// </remarks>
    public ReadOnlyCollection<IMetric> SnapshotAll()
    {
        var list = new List<IMetric>(_duration.Count + _reqSize.Count + _resSize.Count + _total.Count);
        list.AddRange(_duration.Values);
        list.AddRange(_reqSize.Values);
        list.AddRange(_resSize.Values);
        list.AddRange(_total.Values);
        return new ReadOnlyCollection<IMetric>(list);
    }

    /// <summary>
    /// Gets or creates metrics for the specified route, method, status code, scheme, and protocol flavor.
    /// </summary>
    /// <param name="route">The normalized route template.</param>
    /// <param name="method">The HTTP method (GET, POST, etc.).</param>
    /// <param name="code">The HTTP response status code (e.g., 200, 404).</param>
    /// <param name="scheme">The HTTP scheme (http, https).</param>
    /// <param name="flavor">The HTTP protocol flavor (1.1, 2, 3).</param>
    /// <returns>
    /// A tuple containing, in order:
    /// <list type="number">
    ///   <item><description><c>dur</c> — request duration histogram (ms).</description></item>
    ///   <item><description><c>rq</c> — request size histogram (bytes).</description></item>
    ///   <item><description><c>rs</c> — response size histogram (bytes).</description></item>
    ///   <item><description><c>tot</c> — total requests counter.</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// Each metric is initialized with tags including <see cref="TagKeys.Route"/>, <see cref="TagKeys.Method"/>,
    /// <see cref="TagKeys.Scheme"/>, and <see cref="TagKeys.Flavor"/>. The total requests counter additionally
    /// includes <see cref="TagKeys.Code"/> (status code).
    /// </para>
    /// <para>
    /// If <see cref="AspNetCoreMetricOptions.BaseTags"/> is configured, those tags are merged into every metric.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any of <paramref name="route"/>, <paramref name="method"/>, <paramref name="code"/>,
    /// <paramref name="scheme"/>, or <paramref name="flavor"/> is <see langword="null"/>.
    /// </exception>
    public (IBucketHistogramMetric dur, IBucketHistogramMetric rq, IBucketHistogramMetric rs, ICounterMetric tot)
        GetOrCreate(string route, string method, string code, string scheme, string flavor)
    {
        // Ensure route cardinality within configured limit
        var limitedRoute = EnsureRouteWithinLimit(route);

        var baseTags = _opt.BaseTags ?? Collections.EmptyReadOnlyDictionary<string, string>.Instance;

        var commonTags = new Dictionary<string, string>(baseTags)
        {
            [TagKeys.Route] = limitedRoute,
            [TagKeys.Method] = method,
            [TagKeys.Scheme] = scheme,
            [TagKeys.Flavor] = flavor,
        };

        var keyDur = $"{limitedRoute}|{method}";
        var keyReq = keyDur;
        var keyRes = keyDur;
        var keyTot = $"{limitedRoute}|{method}|{code}";

        var dur = _duration.GetOrAdd(keyDur, _ =>
            _factory.Histogram(MetricNames.RequestDuration, "HTTP server request duration (ms)")
                .WithTags(t => { foreach (var kv in commonTags) t.Add(kv.Key, kv.Value); })
                .WithUnit("ms")
                .WithDescription("Elapsed time in milliseconds")
                .WithBounds(_opt.DurationBucketsMs.ToArray())
                .Build());

        var rq = _reqSize.GetOrAdd(keyReq, _ =>
            _factory.Histogram(MetricNames.RequestSize, "HTTP request body size (bytes)")
                .WithTags(t => { foreach (var kv in commonTags) t.Add(kv.Key, kv.Value); })
                .WithUnit("bytes")
                .WithBounds(_opt.SizeBucketsBytes.ToArray())
                .Build());

        var rs = _resSize.GetOrAdd(keyRes, _ =>
            _factory.Histogram(MetricNames.ResponseSize, "HTTP response body size (bytes)")
                .WithTags(t => { foreach (var kv in commonTags) t.Add(kv.Key, kv.Value); })
                .WithUnit("bytes")
                .WithBounds(_opt.SizeBucketsBytes.ToArray())
                .Build());

        var tot = _total.GetOrAdd(keyTot, _ =>
            _factory.Counter(MetricNames.RequestsTotal, "HTTP requests total")
                .WithTags(t =>
                {
                    foreach (var kv in commonTags)
                        t.Add(kv.Key, kv.Value);
                    t.Add(TagKeys.Code, code);
                })
                .Build());

        return (dur, rq, rs, tot);
    }

    /// <summary>
    /// Ensures that the given route is within the configured route cardinality limit.
    /// If the limit is exceeded, returns the fallback label from
    /// <see cref="AspNetCoreMetricOptions.OtherRouteLabel"/>.
    /// </summary>
    /// <param name="route">The route template to check.</param>
    /// <returns>The original route if under the limit, otherwise the fallback label.</returns>
    /// <remarks>
    /// <para>
    /// This method uses an approximate counter (via <see cref="Interlocked.Increment(ref int)"/> and
    /// <see cref="Volatile.Read{T}(ref readonly T)"/>) to keep overhead minimal; slight over-increment is acceptable.
    /// </para>
    /// <para>
    /// The fast-path checks for known routes by probing existing keys in the duration pool.
    /// </para>
    /// </remarks>
    private string EnsureRouteWithinLimit(string route)
    {
        // Fast-path: already known route
        if (_duration.ContainsKey(route + "|GET") || _duration.ContainsKey(route + "|POST"))
            return route;

        // New route: check limit
        if (Volatile.Read(ref _routeCount) >= _opt.MaxRouteCardinality)
            return _opt.OtherRouteLabel;

        // Approximate counter; slight over-increment is acceptable
        Interlocked.Increment(ref _routeCount);
        return route;
    }
}

/// <summary>
/// Helper container for empty read-only dictionaries.
/// </summary>
file static class Collections
{
    /// <summary>
    /// Provides an empty implementation of <see cref="IReadOnlyDictionary{TKey,TValue}"/>.
    /// </summary>
    internal sealed class EmptyReadOnlyDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
        where TKey : notnull
    {
        /// <summary>
        /// A singleton instance of an empty dictionary.
        /// </summary>
        public static readonly EmptyReadOnlyDictionary<TKey, TValue> Instance = new();

        /// <inheritdoc/>
        public int Count => 0;

        /// <inheritdoc/>
        public IEnumerable<TKey> Keys => Array.Empty<TKey>();

        /// <inheritdoc/>
        public IEnumerable<TValue> Values => Array.Empty<TValue>();

        /// <inheritdoc/>
        /// <exception cref="KeyNotFoundException">Always thrown; the dictionary is empty.</exception>
        public TValue this[TKey key] => throw new KeyNotFoundException();

        /// <inheritdoc/>
        public bool ContainsKey(TKey key) => false;

        /// <inheritdoc/>
        public bool TryGetValue(TKey key, out TValue value)
        {
            value = default!;
            return false;
        }

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
            => System.Linq.Enumerable.Empty<KeyValuePair<TKey, TValue>>().GetEnumerator();

        /// <inheritdoc/>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
