// <copyright file="MetricCollectorBase.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Redis.Collectors;

/// <summary>
/// Provides a minimal, reusable base for metric collectors, exposing a shared <see cref="IMetricFactory"/>
/// and convenience helpers for creating common metric types.
/// </summary>
/// <remarks>
/// <para>
/// Derive from this class to implement a concrete collector that gathers metrics from a specific source
/// (e.g., Redis, RabbitMQ, Kafka) and emits them via <see cref="IMetricFactory"/>.
/// </para>
/// <para>
/// This base type also implements <see cref="IMetricCollector.CreateSummary(string,string,System.Collections.Generic.IEnumerable{double},System.Collections.Generic.IReadOnlyDictionary{string,string},bool)"/>
/// and <see cref="IMetricCollector.CreateBucketHistogram(string,string,System.Collections.Generic.IEnumerable{double},System.Collections.Generic.IReadOnlyDictionary{string,string})"/> using the configured
/// factory defaults. Arguments such as <c>quantiles</c>, <c>bucketUpperBounds</c>, <c>tags</c>, and <c>resetOnGet</c>
/// are ignored by the default implementations, allowing the <see cref="IMetricFactory"/> to determine canonical
/// settings for your deployment. If you need per-metric customization, prefer configuring the factory or overriding
/// this behavior in a more specialized base class.
/// </para>
/// <para>
/// Thread-safety: Implementations of <see cref="CollectAsync(System.Threading.CancellationToken)"/> should be safe
/// for concurrent invocation unless explicitly documented otherwise. The base class itself is stateless aside from
/// the injected factory reference.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// internal sealed class RedisPingCollector : MetricCollectorBase
/// {
///     private readonly IRedisClient _client;
///
///     public RedisPingCollector(IMetricFactory factory, IRedisClient client)
///         : base(factory)
///     {
///         _client = client;
///     }
///
///     public override async Task<IMetric?> CollectAsync(CancellationToken ct = default)
///     {
///         var start = Stopwatch.GetTimestamp();
///         var ok = await _client.PingAsync(ct).ConfigureAwait(false);
///         var elapsedMs = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - start)
///                                .TotalMilliseconds;
///
///         // Example using factory directly
///         var gauge = Factory
///             .Gauge("redis.ping.last_duration_ms", "Last Redis PING duration in milliseconds")
///             .WithTag("component", "redis")
///             .Build();
///
///         gauge.SetValue(elapsedMs);
///         return gauge;
///     }
/// }
/// ]]></code>
/// </example>
internal abstract class MetricCollectorBase : IMetricCollector
{
    /// <summary>
    /// Gets the metric factory used by derived collectors to create metrics.
    /// </summary>
    /// <remarks>
    /// The factory encapsulates your system's metric naming, labeling, and export conventions.
    /// Prefer using this property rather than creating metrics directly, so all emitted metrics
    /// conform to project standards.
    /// </remarks>
    protected IMetricFactory Factory { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricCollectorBase"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metric objects.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    protected MetricCollectorBase(IMetricFactory factory) => Factory = factory;

    /// <summary>
    /// Performs the collection operation and returns the resulting metric (or <see langword="null"/> when there is nothing to emit).
    /// </summary>
    /// <param name="ct">A token that can be used to observe cancellation requests.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result is the collected <see cref="IMetric"/> instance,
    /// or <see langword="null"/> if this cycle produced no metric updates.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Implementations should avoid throwing for expected transient conditions (e.g., temporary connectivity issues).
    /// Prefer surfacing such states via dedicated status/health metrics and honoring <paramref name="ct"/>.
    /// </para>
    /// <para>
    /// If your collector updates multiple metrics atomically, consider returning an aggregate metric (e.g., a multi-gauge)
    /// or <see langword="null"/> if updates were already pushed via side-effect.
    /// </para>
    /// </remarks>
    public abstract Task<IMetric?> CollectAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a summary metric using factory defaults.
    /// </summary>
    /// <param name="id">The unique identifier of the summary metric (e.g., <c>redis.op.duration_ms</c>).</param>
    /// <param name="name">A human-friendly metric name or description.</param>
    /// <param name="quantiles">
    /// The requested quantiles. <b>Note:</b> The base implementation does not apply these values; the configured
    /// <see cref="IMetricFactory"/> determines quantiles and objectives.
    /// </param>
    /// <param name="tags">
    /// Optional key/value tags. <b>Note:</b> The base implementation does not attach tags here; add tags via
    /// your factory configuration or a specialized implementation if required.
    /// </param>
    /// <param name="resetOnGet">
    /// Whether the summary should reset on scrape. <b>Note:</b> Ignored by the base implementation; factory policy applies.
    /// </param>
    /// <returns>A new <see cref="ISummaryMetric"/> built via the current <see cref="IMetricFactory"/>.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Using the base helper; quantiles/tags come from factory configuration
    /// var summary = ((IMetricCollector)this).CreateSummary(
    ///     "redis.pipeline.duration_ms",
    ///     "Redis pipeline duration in milliseconds",
    ///     quantiles: new[] { 0.5, 0.9, 0.99 },
    ///     tags: null,
    ///     resetOnGet: true);
    ///
    /// summary.Observe(12.3);
    /// ]]></code>
    /// </example>
    ISummaryMetric IMetricCollector.CreateSummary(
        string id,
        string name,
        IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags,
        bool resetOnGet)
    {
        return Factory.Summary(id, name).Build();
    }

    /// <summary>
    /// Creates a bucket histogram metric using factory defaults.
    /// </summary>
    /// <param name="id">The unique identifier of the histogram metric (e.g., <c>redis.fetch.bytes</c>).</param>
    /// <param name="name">A human-friendly metric name or description.</param>
    /// <param name="bucketUpperBounds">
    /// The histogram bucket upper bounds. <b>Note:</b> The base implementation does not apply these values; the
    /// <see cref="IMetricFactory"/> determines the bucket configuration.
    /// </param>
    /// <param name="tags">
    /// Optional key/value tags. <b>Note:</b> The base implementation does not attach tags here; add tags via
    /// your factory configuration or a specialized implementation if required.
    /// </param>
    /// <returns>A new <see cref="IBucketHistogramMetric"/> built via the current <see cref="IMetricFactory"/>.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Using the base helper; bucket layout comes from factory configuration
    /// var hist = ((IMetricCollector)this).CreateBucketHistogram(
    ///     "redis.response.size_bytes",
    ///     "Redis response size",
    ///     bucketUpperBounds: new[] { 256d, 512d, 1024d, 2048d },
    ///     tags: null);
    ///
    /// hist.Observe(780);
    /// ]]></code>
    /// </example>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id,
        string name,
        IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
    {
        return Factory.Histogram(id, name).Build();
    }
}
