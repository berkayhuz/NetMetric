// <copyright file="KafkaClientStatsCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Kafka.Configurations;

using TagList = NetMetric.Abstractions.TagList;

namespace NetMetric.Kafka.Collectors;

/// <summary>
/// Collects Kafka client statistics from an <see cref="IKafkaStatsSource"/> and
/// updates a set of derived metrics (<see cref="IGauge"/> and <see cref="ICounterMetric"/>).
/// </summary>
/// <remarks>
/// <para>
/// This collector consumes periodic snapshots exposed by <see cref="IKafkaStatsSource"/> and
/// projects commonly monitored Kafka signals such as throughput, byte rates, queue depth,
/// request latencies (p50/p95/p99), retry counts, and error counts.
/// </para>
/// <para>
/// Since the Confluent statistics JSON does not expose per-request sample latencies,
/// percentile latencies are published as independent <see cref="IGauge"/>s (e.g.,
/// <c>latency.p95</c>) rather than recording into a single summary. This avoids producing
/// misleading distributions that would otherwise be inferred from aggregate values only.
/// </para>
/// <para>
/// Metric IDs follow the pattern <c>kafka.&lt;clientType&gt;.&lt;clientId&gt;.&lt;name&gt;</c>
/// (for example, <c>kafka.producer.payments.tx.rate</c>). Optional base tags from
/// <see cref="KafkaModuleOptions"/> are attached to every metric instance created by this collector.
/// </para>
/// <threadsafety>
/// This type is not thread-safe. It maintains internal counters to compute increments for
/// retries and errors. Invoke <see cref="CollectAsync(System.Threading.CancellationToken)"/>
/// from a single, serialized collection loop.
/// </threadsafety>
/// <example>
/// <code>
/// // Registration
/// var opts = new KafkaModuleOptions
/// {
///     BaseTags = new Dictionary&lt;string, string&gt; { ["service"] = "orders", ["env"] = "prod" },
///     StatsStaleAfter = TimeSpan.FromSeconds(15),
///     Window = MetricWindowPolicy.Cumulative
/// };
///
/// IKafkaStatsSource source = new ConfluentKafkaStatsSource(...); // your implementation
/// IMetricFactory factory = metrics.CreateFactory();
///
/// var collector = new KafkaClientStatsCollector(factory, source, opts);
///
/// // Periodic collection (e.g., hosted service / timer)
/// await collector.CollectAsync(ct);
/// </code>
/// </example>
/// </remarks>
/// <seealso cref="IKafkaStatsSource"/>
/// <seealso cref="KafkaModuleOptions"/>
public sealed class KafkaClientStatsCollector : IMetricCollector
{
    private readonly IKafkaStatsSource _source;
    private readonly IMetricFactory _factory;
    private readonly KafkaModuleOptions _opts;

    private readonly IGauge _txRate;
    private readonly IGauge _rxRate;
    private readonly IGauge _txBytes;
    private readonly IGauge _rxBytes;
    private readonly IGauge _queueDepth;

    private readonly IGauge _latencyP50;
    private readonly IGauge _latencyP95;
    private readonly IGauge _latencyP99;

    private readonly IGauge _stale;               // 1: stale, 0: fresh
    private readonly ICounterMetric _retries;
    private readonly ICounterMetric _errors;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaClientStatsCollector"/> class.
    /// </summary>
    /// <param name="source">The Kafka statistics source to collect data from.</param>
    /// <param name="factory">The metric factory used to build metrics.</param>
    /// <param name="opts">Module options that control naming, tagging, and staleness thresholds.</param>
    /// <remarks>
    /// This overload delegates to <see cref="KafkaClientStatsCollector(IMetricFactory, IKafkaStatsSource, KafkaModuleOptions)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    public KafkaClientStatsCollector(IKafkaStatsSource source, IMetricFactory factory, KafkaModuleOptions opts) : this(factory, source, opts) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaClientStatsCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to build metrics.</param>
    /// <param name="source">The Kafka statistics source to collect data from.</param>
    /// <param name="opts">Module options that control naming, tagging, and staleness thresholds.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="factory"/> or <paramref name="source"/> is <see langword="null"/>.
    /// </exception>
    public KafkaClientStatsCollector(IMetricFactory factory, IKafkaStatsSource source, KafkaModuleOptions opts)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _opts = opts ?? new KafkaModuleOptions();

        var baseTags = _opts.BaseTags ?? new Dictionary<string, string>();
        var idPrefix = $"kafka.{_source.ClientType}.{_source.ClientId}";

        _txRate = _factory.Gauge($"{idPrefix}.tx.rate", "Kafka TX msgs/sec").WithTags(t => ApplyBase(t, baseTags)).Build();
        _rxRate = _factory.Gauge($"{idPrefix}.rx.rate", "Kafka RX msgs/sec").WithTags(t => ApplyBase(t, baseTags)).Build();
        _txBytes = _factory.Gauge($"{idPrefix}.tx.bytes", "Kafka TX bytes/sec").WithTags(t => ApplyBase(t, baseTags)).Build();
        _rxBytes = _factory.Gauge($"{idPrefix}.rx.bytes", "Kafka RX bytes/sec").WithTags(t => ApplyBase(t, baseTags)).Build();
        _queueDepth = _factory.Gauge($"{idPrefix}.queue.depth", "Kafka queue depth").WithTags(t => ApplyBase(t, baseTags)).Build();

        _latencyP50 = _factory.Gauge($"{idPrefix}.latency.p50", "Kafka request latency p50 (ms)").WithTags(t => ApplyBase(t, baseTags)).Build();
        _latencyP95 = _factory.Gauge($"{idPrefix}.latency.p95", "Kafka request latency p95 (ms)").WithTags(t => ApplyBase(t, baseTags)).Build();
        _latencyP99 = _factory.Gauge($"{idPrefix}.latency.p99", "Kafka request latency p99 (ms)").WithTags(t => ApplyBase(t, baseTags)).Build();

        _stale = _factory.Gauge($"{idPrefix}.stale", "Kafka stats staleness (1=stale, 0=fresh)").WithTags(t => ApplyBase(t, baseTags)).Build();

        _retries = _factory.Counter($"{idPrefix}.retries", "Kafka retries total").WithTags(t => ApplyBase(t, baseTags)).Build();
        _errors = _factory.Counter($"{idPrefix}.errors", "Kafka errors total").WithTags(t => ApplyBase(t, baseTags)).Build();
    }

    /// <summary>
    /// Gets a human-readable name of this collector instance.
    /// </summary>
    /// <remarks>
    /// The format is <c>KafkaClientStats(&lt;ClientId&gt;)</c> to help disambiguate multiple
    /// collectors when registered for several clients.
    /// </remarks>
    public string Name => $"KafkaClientStats({_source.ClientId})";

    /// <summary>
    /// Collects metrics asynchronously from the Kafka statistics source and updates the exposed instruments.
    /// </summary>
    /// <param name="ct">A token to observe for cancellation requests.</param>
    /// <returns>
    /// The representative metric updated during this cycle (currently the p95 latency gauge),
    /// or <see langword="null"/> if no snapshot is available from the source.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method:
    /// </para>
    /// <list type="number">
    ///   <item><description>Obtains the current snapshot via <c>_source.TryGetSnapshot()</c>.</description></item>
    ///   <item><description>Evaluates staleness using <see cref="KafkaModuleOptions.StatsStaleAfter"/> and sets the <c>.stale</c> gauge to 1 or 0.</description></item>
    ///   <item><description>Publishes throughput, byte rates, queue depth, and latency percentiles as gauges.</description></item>
    ///   <item><description>Computes and records <em>increments</em> for retries and errors by diffing the last observed totals.</description></item>
    /// </list>
    /// <para>
    /// Consumers that need to react to a specific instrument can use the returned metric,
    /// but should not rely on the choice of representative metric remaining stable across versions.
    /// </para>
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct)
    {
        var s = _source.TryGetSnapshot();
        if (s is null)
        {
            return Task.FromResult<IMetric?>(null);
        }

        var now = DateTimeOffset.UtcNow;
        var isStale = (now - s.Timestamp) > _opts.StatsStaleAfter;

        _stale.SetValue(isStale ? 1 : 0);

        _txRate.SetValue(s.TxMsgsPerSec);
        _rxRate.SetValue(s.RxMsgsPerSec);
        _txBytes.SetValue(s.TxBytesPerSec);
        _rxBytes.SetValue(s.RxBytesPerSec);
        _queueDepth.SetValue(s.QueueDepth);

        _latencyP50.SetValue(s.LatencyAvgMs);
        _latencyP95.SetValue(s.LatencyP95Ms);
        _latencyP99.SetValue(s.LatencyP99Ms);

        _retries.Increment(Math.Max(0, s.RetriesTotal - _lastRetries));
        _errors.Increment(Math.Max(0, s.ErrorsTotal - _lastErrors));
        _lastRetries = s.RetriesTotal; _lastErrors = s.ErrorsTotal;

        // Representative metric: p95
        return Task.FromResult<IMetric?>(_latencyP95);
    }

    private long _lastRetries, _lastErrors;

    private static void ApplyBase(TagList t, IReadOnlyDictionary<string, string> baseTags)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(baseTags);

        foreach (var kv in baseTags)
        {
            t.Add(kv.Key, kv.Value);
        }
    }

    /// <summary>
    /// Creates and configures a summary metric (<see cref="ISummaryMetric"/>) with the specified quantiles and tags.
    /// </summary>
    /// <param name="id">The metric identifier (stable, dot-separated).</param>
    /// <param name="name">A descriptive display name for the metric.</param>
    /// <param name="quantiles">
    /// The quantiles to include (e.g., <c>0.5</c>, <c>0.95</c>, <c>0.99</c>). If <see langword="null"/>,
    /// defaults to <c>{ 0.5, 0.9, 0.99 }</c>.
    /// </param>
    /// <param name="tags">Optional tags to attach to the metric instance.</param>
    /// <param name="resetOnGet">Whether the summary should reset after every read.</param>
    /// <returns>A built <see cref="ISummaryMetric"/> ready to observe values.</returns>
    /// <remarks>
    /// The summary window is taken from <see cref="KafkaModuleOptions.Window"/>; if not provided,
    /// <see cref="MetricWindowPolicy.Cumulative"/> is used.
    /// </remarks>
    ISummaryMetric IMetricCollector.CreateSummary(string id, string name, IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
    {
        var q = quantiles?.ToArray() ?? new[] { 0.5, 0.9, 0.99 };
        var sb = _factory.Summary(id, name).WithQuantiles(q).WithWindow(_opts.Window ?? MetricWindowPolicy.Cumulative);

        if (tags is not null)
        {
            foreach (var kv in tags)
            {
                sb.WithTag(kv.Key, kv.Value);
            }
        }

        return sb.Build();
    }

    /// <summary>
    /// Creates and configures a bucket histogram metric (<see cref="IBucketHistogramMetric"/>).
    /// </summary>
    /// <param name="id">The metric identifier (stable, dot-separated).</param>
    /// <param name="name">A descriptive display name for the metric.</param>
    /// <param name="bucketUpperBounds">
    /// The (ascending) upper bounds for histogram buckets. If <see langword="null"/>, an empty bound set is used.
    /// </param>
    /// <param name="tags">Optional tags to attach to the metric instance.</param>
    /// <returns>A built <see cref="IBucketHistogramMetric"/> ready to observe values.</returns>
    /// <remarks>
    /// Bounds are applied as provided. Ensure they are strictly increasing and meaningful for the measured unit.
    /// </remarks>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(string id, string name,
        IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
    {
        var bounds = bucketUpperBounds?.ToArray() ?? Array.Empty<double>();
        var hb = _factory.Histogram(id, name).WithBounds(bounds);

        if (tags is not null)
        {
            foreach (var kv in tags)
            {
                hb.WithTag(kv.Key, kv.Value);
            }
        }

        return hb.Build();
    }
}
