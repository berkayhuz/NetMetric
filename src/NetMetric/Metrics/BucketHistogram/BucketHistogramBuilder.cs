// <copyright file="BucketHistogramBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.BucketHistogram;

/// <summary>
/// Builder for creating <see cref="IBucketHistogramMetric"/> instruments with configurable
/// bucket strategies (linear, exponential, or explicit bounds) and windowing policy.
/// </summary>
/// <remarks>
/// <para>
/// This builder follows the fluent pattern and is typically obtained from
/// <see cref="IMetricFactory.Histogram(string, string)"/>.
/// You can choose a bucket strategy via <see cref="Linear(double, double, int)"/>,
/// <see cref="Exponential(double, double, int)"/>, or <see cref="WithBounds(double[])"/>.
/// Finally, call <see cref="Build"/> to materialize a thread-safe
/// <see cref="BucketHistogramMetric"/>.
/// </para>
/// <para>
/// Tags added through the base <see cref="InstrumentBuilderBase{TMetric}"/> APIs are merged with
/// global and resource tags and then sanitized according to <see cref="MetricOptions"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example: histogram for request durations with linear buckets and a 1-minute tumbling window
/// var metric = factory.Histogram("svc.req.duration", "Request Duration (ms)")
///     .WithUnit("ms")
///     .WithDescription("Latency distribution of incoming requests")
///     .WithTag("endpoint", "/api/orders")
///     .WithWindow(MetricWindowPolicy.Tumbling(TimeSpan.FromMinutes(1)))
///     .Linear(start: 0, width: 25, count: 20) // 25ms steps up to 500ms
///     .Build();
///
/// metric.Observe(42);
/// metric.Observe(230);
/// </code>
/// </example>
internal sealed class BucketHistogramBuilder : InstrumentBuilderBase<IBucketHistogramMetric>, IBucketHistogramBuilder
{
    private double[]? _bounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="BucketHistogramBuilder"/> class.
    /// </summary>
    /// <param name="id">Stable unique identifier for the metric (e.g., "service.request.duration").</param>
    /// <param name="name">Human-readable metric name (e.g., "Request Duration").</param>
    /// <param name="options">Metric configuration options that influence tags, limits, and defaults.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> or <paramref name="name"/> is null or whitespace.
    /// </exception>
    public BucketHistogramBuilder(string id, string name, MetricOptions options) : base(id, name, options) { }

    /// <summary>
    /// Configures bucket boundaries using a linear sequence where each bucket has the same width.
    /// </summary>
    /// <param name="start">The starting offset for buckets. The first upper bound is <c>start + width</c>.</param>
    /// <param name="width">The width of each bucket (must be finite; can be fractional).</param>
    /// <param name="count">The number of buckets to create (must be &gt; 0).</param>
    /// <returns>The current <see cref="IBucketHistogramBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// Internally delegates to <see cref="BucketHistogramMetric.LinearBuckets(double, double, int)"/>.
    /// The generated upper bounds are sorted ascending by construction.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Buckets: (0,10], (10,20], ..., (90,100]
    /// var h = factory.Histogram("io.read.size", "Read Size (KB)")
    ///                .Linear(start: 0, width: 10, count: 10)
    ///                .Build();
    /// </code>
    /// </example>
    public IBucketHistogramBuilder Linear(double start, double width, int count)
    {
        _bounds = BucketHistogramMetric.LinearBuckets(start, width, count);
        return this;
    }

    /// <summary>
    /// Configures bucket boundaries using an exponential sequence where each bound grows by a constant factor.
    /// </summary>
    /// <param name="start">The first upper bound (must be &gt; 0).</param>
    /// <param name="factor">The multiplicative growth factor between successive bounds (must be &gt; 1).</param>
    /// <param name="count">The number of buckets to create (must be &gt; 0).</param>
    /// <returns>The current <see cref="IBucketHistogramBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// Internally delegates to <see cref="BucketHistogramMetric.ExponentialBuckets(double, double, int)"/>.
    /// Useful for latency or size distributions with long tails.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Buckets: 1, 2, 4, 8, 16, 32 (upper bounds)
    /// var h = factory.Histogram("svc.payload.size", "Payload Size (KB)")
    ///                .Exponential(start: 1, factor: 2, count: 6)
    ///                .Build();
    /// </code>
    /// </example>
    public IBucketHistogramBuilder Exponential(double start, double factor, int count)
    {
        _bounds = BucketHistogramMetric.ExponentialBuckets(start, factor, count);
        return this;
    }

    /// <summary>
    /// Configures bucket boundaries using explicit upper bounds.
    /// </summary>
    /// <param name="bounds">One or more bucket upper bounds. They need not be pre-sorted; sorting is applied at build-time.</param>
    /// <returns>The current <see cref="IBucketHistogramBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// Use this method when you need full control over bucket edges (e.g., SLO-aligned buckets).
    /// Non-finite values (NaN, ±Infinity) will be rejected at metric construction.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Custom, SLO-aligned buckets (ms)
    /// var h = factory.Histogram("http.server.duration", "HTTP Server Duration")
    ///                .WithBounds(50, 100, 200, 400, 1000)
    ///                .Build();
    /// </code>
    /// </example>
    public IBucketHistogramBuilder WithBounds(params double[] bounds)
    {
        _bounds = bounds;
        return this;
    }

    /// <summary>
    /// Finalizes the configuration and creates a new <see cref="IBucketHistogramMetric"/> instance.
    /// </summary>
    /// <returns>
    /// A fully constructed <see cref="BucketHistogramMetric"/> with merged and sanitized tags,
    /// selected window policy, and configured bucket bounds.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no bucket strategy has been selected (i.e., bounds are not provided).
    /// </exception>
    /// <remarks>
    /// <para>
    /// If a concrete <see cref="MetricWindowPolicy"/> is provided via
    /// <see cref="InstrumentBuilderBase{TMetric}.WithWindow(IMetricWindowPolicy)"/>,
    /// it is used as-is. Otherwise:
    /// <list type="bullet">
    /// <item><description>If <see cref="IMetricWindowPolicy.Kind"/> is <c>Tumbling</c>, a tumbling policy is constructed with the specified period.</description></item>
    /// <item><description>For any other case, a cumulative policy is used.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Tag materialization merges local tags with global and resource tags from <see cref="MetricOptions"/>,
    /// then applies limits via <see cref="TagSanitizer"/>. See <see cref="InstrumentBuilderBase{TMetric}.MaterializeTags"/> for details.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var h = factory.Histogram("db.query.duration", "DB Query Duration")
    ///                .WithTags(t =&gt; t.Add("db.system", "postgres"))
    ///                .WithWindow(MetricWindowPolicy.Cumulative)
    ///                .Linear(0, 5, 40)  // 5ms steps up to 200ms
    ///                .Build();
    /// </code>
    /// </example>
    public override IBucketHistogramMetric Build()
    {
        var tags = MaterializeTags();

        // Resolve window policy:
        //   - If Window is already a MetricWindowPolicy instance, use it as-is
        //   - Else if the kind is Tumbling, construct a tumbling policy from the period
        //   - Otherwise fall back to cumulative
        var window = Window is MetricWindowPolicy mp
                   ? mp
                   : Window is { Kind: MetricWindowKind.Tumbling } w ? MetricWindowPolicy.Tumbling(w.Period)
                   : MetricWindowPolicy.Cumulative;

        return new BucketHistogramMetric(
            Id,
            Name,
            _bounds ?? throw new InvalidOperationException("Bounds required"),
            tags,
            window,
            clock: null);
    }
}
