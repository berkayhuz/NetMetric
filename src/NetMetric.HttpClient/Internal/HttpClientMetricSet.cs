// <copyright file="HttpClientMetricSet.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.HttpClient.Internal;

/// <summary>
/// Provides cached, tag-scoped metric instruments for HTTP client telemetry.
/// </summary>
/// <remarks>
/// <para>
/// This type lazily creates and caches counters and bucket histograms keyed by the tuple
/// (<c>host</c>, <c>method</c>, <c>scheme</c>, and optionally <c>phase</c> or <c>status code</c>).
/// Reusing instruments avoids high-cardinality duplication and reduces allocation/registration
/// cost when the same tag combinations are observed repeatedly.
/// </para>
/// <para>
/// Instances of this class are thread-safe. All instrument lookups use
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> with <see cref="StringComparer.Ordinal"/> to achieve
/// deterministic keying and concurrent access without additional locks.
/// </para>
/// </remarks>
/// <seealso cref="IMetricFactory"/>
/// <seealso cref="IBucketHistogramMetric"/>
/// <seealso cref="ICounterMetric"/>
public sealed class HttpClientMetricSet
{
    private readonly IMetricFactory _factory;
    private readonly double[] _latencyBucketsMs;
    private readonly double[] _sizeBuckets;

    private readonly ConcurrentDictionary<string, IBucketHistogramMetric> _phase = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IBucketHistogramMetric> _size = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ICounterMetric> _total = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ICounterMetric> _redirects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ICounterMetric> _retries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ICounterMetric> _timeouts = new(StringComparer.Ordinal);

    /// <summary>
    /// Produces a canonical host tag by uppercasing the supplied value; returns <c>"UNKNOWN"</c> when <paramref name="host"/> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Provided as a helper for callers that want consistent tag casing; this class does not call it automatically.
    /// </remarks>
    private static string NormHost(string host) => host?.ToUpperInvariant() ?? "UNKNOWN";

    /// <summary>
    /// Produces a canonical HTTP method tag by uppercasing the supplied value; returns <c>"GET"</c> when <paramref name="method"/> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Provided as a helper for callers that want consistent tag casing; this class does not call it automatically.
    /// </remarks>
    private static string NormMethod(string method) => method?.ToUpperInvariant() ?? "GET";

    /// <summary>
    /// Produces a canonical scheme tag of <c>"HTTPS"</c> or <c>"HTTP"</c>; defaults to <c>"HTTP"</c>.
    /// </summary>
    /// <remarks>
    /// Provided as a helper for callers that want consistent tag casing; this class does not call it automatically.
    /// </remarks>
    private static string NormScheme(string scheme) => (scheme?.ToUpperInvariant()) switch
    {
        "HTTPS" => "HTTPS",
        _ => "HTTP"
    };

    /// <summary>
    /// Initializes a new instance of <see cref="HttpClientMetricSet"/> with the supplied factory and bucket boundaries.
    /// </summary>
    /// <param name="factory">The metric factory used to build instruments.</param>
    /// <param name="latencyBucketsMs">Bucket boundaries (in milliseconds) to use for latency histograms.</param>
    /// <param name="sizeBuckets">Bucket boundaries (in bytes) to use for size histograms.</param>
    /// <remarks>
    /// If <paramref name="latencyBucketsMs"/> or <paramref name="sizeBuckets"/> are evenly spaced, a linear histogram is configured
    /// (when supported by the implementation); otherwise, explicit bounds are applied.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public HttpClientMetricSet(IMetricFactory factory, double[] latencyBucketsMs, double[] sizeBuckets)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _latencyBucketsMs = latencyBucketsMs ?? throw new ArgumentNullException(nameof(latencyBucketsMs));
        _sizeBuckets = sizeBuckets ?? throw new ArgumentNullException(nameof(sizeBuckets));
    }

    /// <summary>
    /// Applies bucket configuration to a histogram builder. Uses linear buckets when bounds are evenly spaced; otherwise applies explicit bounds.
    /// </summary>
    /// <param name="b">The instrument builder to configure.</param>
    /// <param name="bounds">Bucket boundaries to apply.</param>
    /// <returns>The same builder instance, potentially with bucket configuration applied.</returns>
    /// <remarks>
    /// If <paramref name="b"/> does not implement <see cref="IBucketHistogramBuilder"/>, the builder is returned unchanged.
    /// </remarks>
    private static IInstrumentBuilder<IBucketHistogramMetric> ApplyBounds(
        IInstrumentBuilder<IBucketHistogramMetric> b, double[] bounds)
    {
        if (bounds is { Length: >= 2 } && b is IBucketHistogramBuilder hb)
        {
            // Prefer linear buckets when evenly spaced to reduce metadata size.
            var step = bounds[1] - bounds[0];
            if (step > 0)
            {
                bool linear = true;
                for (int i = 2; i < bounds.Length; i++)
                {
                    var d = bounds[i] - bounds[i - 1];
                    if (Math.Abs(d - step) > 1e-9)
                    {
                        linear = false;
                        break;
                    }
                }

                if (linear)
                    return hb.Linear(bounds[0], step, bounds.Length);
            }

            // Fallback to explicit bounds when not linear or step is invalid.
            return hb.WithBounds(bounds);
        }

        return b;
    }

    /// <summary>
    /// Gets (or creates) the histogram for measuring per-phase HTTP client latency in milliseconds.
    /// </summary>
    /// <param name="host">Destination host tag value (as supplied by the caller).</param>
    /// <param name="method">HTTP method tag value (as supplied by the caller).</param>
    /// <param name="scheme">URL scheme tag value (as supplied by the caller).</param>
    /// <param name="phase">
    /// Protocol/transport phase name (for example: <c>dns</c>, <c>connect</c>, <c>tls</c>, <c>request_body</c>,
    /// <c>response_headers</c>, <c>response_body</c>, <c>total</c>).
    /// </param>
    /// <returns>
    /// A cached <see cref="IBucketHistogramMetric"/> tagged with <c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>, and <c>phase</c>.
    /// </returns>
    /// <remarks>
    /// The metric name is <see cref="HttpClientMetricNames.PhaseDuration"/> and the unit is <c>ms</c>. Buckets come from <see cref="_latencyBucketsMs"/>.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var h = set.GetPhase(host, method, scheme, "dns");
    /// h.Observe(3.2); // 3.2 ms DNS resolution time
    /// </code>
    /// </example>
    public IBucketHistogramMetric GetPhase(string host, string method, string scheme, string phase)
    {
        var key = $"{host}|{method}|{scheme}|{phase}";
        return _phase.GetOrAdd(key, _ =>
            ApplyBounds(
                _factory.Histogram(HttpClientMetricNames.PhaseDuration, "HTTP client phase duration (ms)")
                    .WithUnit("ms")
                    .WithTags(t =>
                    {
                        t.Add(HttpClientTagKeys.Host, host);
                        t.Add(HttpClientTagKeys.Method, method);
                        t.Add(HttpClientTagKeys.Scheme, scheme);
                        t.Add(HttpClientTagKeys.Phase, phase);
                    }),
                _latencyBucketsMs
            ).Build());
    }

    /// <summary>
    /// Gets (or creates) the histogram for measuring HTTP response body size in bytes.
    /// </summary>
    /// <param name="host">Destination host tag value (as supplied by the caller).</param>
    /// <param name="method">HTTP method tag value (as supplied by the caller).</param>
    /// <param name="scheme">URL scheme tag value (as supplied by the caller).</param>
    /// <returns>
    /// A cached <see cref="IBucketHistogramMetric"/> tagged with <c>http.host</c>, <c>http.method</c>, and <c>url.scheme</c>.
    /// </returns>
    /// <remarks>
    /// The metric name is <see cref="HttpClientMetricNames.ResponseSize"/> and the unit is <c>bytes</c>. Buckets come from <see cref="_sizeBuckets"/>.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var sz = set.GetSize(host, method, scheme);
    /// sz.Observe(10240); // 10 KB body
    /// </code>
    /// </example>
    public IBucketHistogramMetric GetSize(string host, string method, string scheme)
    {
        var key = $"{host}|{method}|{scheme}";
        return _size.GetOrAdd(key, _ =>
            ApplyBounds(
                _factory.Histogram(HttpClientMetricNames.ResponseSize, "HTTP client response size (bytes)")
                    .WithUnit("bytes")
                    .WithTags(t =>
                    {
                        t.Add(HttpClientTagKeys.Host, host);
                        t.Add(HttpClientTagKeys.Method, method);
                        t.Add(HttpClientTagKeys.Scheme, scheme);
                    }),
                _sizeBuckets
            ).Build());
    }

    /// <summary>
    /// Gets (or creates) the counter for total HTTP requests, partitioned by status code.
    /// </summary>
    /// <param name="host">Destination host tag value (as supplied by the caller).</param>
    /// <param name="method">HTTP method tag value (as supplied by the caller).</param>
    /// <param name="scheme">URL scheme tag value (as supplied by the caller).</param>
    /// <param name="code">HTTP status code tag value (e.g., <c>"200"</c>, <c>"404"</c>).</param>
    /// <returns>
    /// A cached <see cref="ICounterMetric"/> tagged with <c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>, and <c>http.status_code</c>.
    /// </returns>
    /// <remarks>The metric name is <see cref="HttpClientMetricNames.RequestsTotal"/>.</remarks>
    /// <example>
    /// <code language="csharp">
    /// set.GetTotal(host, method, scheme, "200").Increment();
    /// </code>
    /// </example>
    public ICounterMetric GetTotal(string host, string method, string scheme, string code)
    {
        var key = $"{host}|{method}|{scheme}|{code}";
        return _total.GetOrAdd(key, _ =>
            _factory.Counter(HttpClientMetricNames.RequestsTotal, "HTTP client requests total")
                .WithTags(t =>
                {
                    t.Add(HttpClientTagKeys.Host, host);
                    t.Add(HttpClientTagKeys.Method, method);
                    t.Add(HttpClientTagKeys.Scheme, scheme);
                    t.Add(HttpClientTagKeys.StatusCode, code);
                })
                .Build());
    }

    /// <summary>
    /// Gets (or creates) the counter for HTTP redirects.
    /// </summary>
    /// <param name="host">Destination host tag value (as supplied by the caller).</param>
    /// <param name="method">HTTP method tag value (as supplied by the caller).</param>
    /// <param name="scheme">URL scheme tag value (as supplied by the caller).</param>
    /// <returns>
    /// A cached <see cref="ICounterMetric"/> tagged with <c>http.host</c>, <c>http.method</c>, and <c>url.scheme</c>.
    /// </returns>
    /// <remarks>The metric name is <see cref="HttpClientMetricNames.RedirectsTotal"/>.</remarks>
    public ICounterMetric GetRedirects(string host, string method, string scheme)
    {
        var key = $"{host}|{method}|{scheme}";
        return _redirects.GetOrAdd(key, _ =>
            _factory.Counter(HttpClientMetricNames.RedirectsTotal, "HTTP client redirects total")
                .WithTags(t =>
                {
                    t.Add(HttpClientTagKeys.Host, host);
                    t.Add(HttpClientTagKeys.Method, method);
                    t.Add(HttpClientTagKeys.Scheme, scheme);
                })
                .Build());
    }

    /// <summary>
    /// Gets (or creates) the counter for HTTP retries.
    /// </summary>
    /// <param name="host">Destination host tag value (as supplied by the caller).</param>
    /// <param name="method">HTTP method tag value (as supplied by the caller).</param>
    /// <param name="scheme">URL scheme tag value (as supplied by the caller).</param>
    /// <returns>
    /// A cached <see cref="ICounterMetric"/> tagged with <c>http.host</c>, <c>http.method</c>, and <c>url.scheme</c>.
    /// </returns>
    /// <remarks>The metric name is <see cref="HttpClientMetricNames.RetriesTotal"/>.</remarks>
    public ICounterMetric GetRetries(string host, string method, string scheme)
    {
        var key = $"{host}|{method}|{scheme}";
        return _retries.GetOrAdd(key, _ =>
            _factory.Counter(HttpClientMetricNames.RetriesTotal, "HTTP client retries total")
                .WithTags(t =>
                {
                    t.Add(HttpClientTagKeys.Host, host);
                    t.Add(HttpClientTagKeys.Method, method);
                    t.Add(HttpClientTagKeys.Scheme, scheme);
                })
                .Build());
    }

    /// <summary>
    /// Gets (or creates) the counter for HTTP timeouts.
    /// </summary>
    /// <param name="host">Destination host tag value (as supplied by the caller).</param>
    /// <param name="method">HTTP method tag value (as supplied by the caller).</param>
    /// <param name="scheme">URL scheme tag value (as supplied by the caller).</param>
    /// <returns>
    /// A cached <see cref="ICounterMetric"/> tagged with <c>http.host</c>, <c>http.method</c>, and <c>url.scheme</c>.
    /// </returns>
    /// <remarks>The metric name is <see cref="HttpClientMetricNames.TimeoutsTotal"/>.</remarks>
    public ICounterMetric GetTimeouts(string host, string method, string scheme)
    {
        var key = $"{host}|{method}|{scheme}";
        return _timeouts.GetOrAdd(key, _ =>
            _factory.Counter(HttpClientMetricNames.TimeoutsTotal, "HTTP client timeouts total")
                .WithTags(t =>
                {
                    t.Add(HttpClientTagKeys.Host, host);
                    t.Add(HttpClientTagKeys.Method, method);
                    t.Add(HttpClientTagKeys.Scheme, scheme);
                })
                .Build());
    }
}
