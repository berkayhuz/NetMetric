// <copyright file="KafkaLagCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Kafka.Configurations;

namespace NetMetric.Kafka.Collectors;


/// <summary>
/// Collects Kafka consumer lag metrics for a specific consumer group and exposes them
/// as a multi-gauge metric.
/// </summary>
/// <remarks>
/// <para>
/// Each time <see cref="CollectAsync(System.Threading.CancellationToken)"/> is called, this collector queries
/// the configured <see cref="IKafkaLagProbe"/> for per-partition lag and publishes the values under the
/// metric id <c>kafka.consumer.lag</c>.
/// </para>
/// <para>
/// <b>Metric shape</b><br/>
/// The collector produces a multi-gauge with one time series per topic-partition. The following tags are set:
/// </para>
/// <list type="bullet">
///   <item><description><c>topic</c> — Kafka topic name.</description></item>
///   <item><description><c>partition</c> — Partition id as string.</description></item>
///   <item><description><c>group</c> — Consumer group id from <see cref="KafkaModuleOptions.ConsumerGroup"/>.</description></item>
/// </list>
/// <para>
/// When no lag data is available (empty probe result), a single series is emitted with <c>status=empty</c>
/// and value <c>0</c>.
/// </para>
/// <para>
/// <b>Cardinality control</b><br/>
/// To protect downstream systems, the number of time series can be limited via
/// <see cref="KafkaModuleOptions.MaxLagSeries"/>. When the value is greater than zero, only the first
/// <c>MaxLagSeries</c> entries returned by the probe are published.
/// </para>
/// <para>
/// <b>Thread-safety</b><br/>
/// Instances of this collector are typically registered as singletons and are safe to use concurrently when
/// called through the metric factory’s thread-safe implementations.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Registration
/// services.AddSingleton&lt;IMetricCollector, KafkaLagCollector&gt;();
///
/// // Options
/// var opts = new KafkaModuleOptions {
///     ConsumerGroup = "orders-service-consumers",
///     MaxLagSeries = 100
/// };
///
/// // Usage during scraping / scheduled collection
/// var metric = await collector.CollectAsync(ct);
/// </code>
/// </example>
public sealed class KafkaLagCollector : IMetricCollector
{
    private readonly IKafkaLagProbe _probe;
    private readonly IMetricFactory _factory;
    private readonly KafkaModuleOptions _opts;
    private readonly IMultiGauge _lag;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaLagCollector"/> class.
    /// </summary>
    /// <param name="probe">The Kafka lag probe used to retrieve per-partition lag data.</param>
    /// <param name="factory">The metric factory used to create and build metric instances.</param>
    /// <param name="opts">Module options that configure consumer group and cardinality limits.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="probe"/> or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    public KafkaLagCollector(IKafkaLagProbe probe, IMetricFactory factory, KafkaModuleOptions opts)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _opts = opts ?? new KafkaModuleOptions();

        _lag = _factory.MultiGauge("kafka.consumer.lag", "Kafka consumer lag")
            .WithResetOnGet(true)
            .Build();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaLagCollector"/> class with the specified factory, probe, and options.
    /// </summary>
    /// <param name="factory">The metric factory used to create and build metric instances.</param>
    /// <param name="probe">The Kafka lag probe used to retrieve per-partition lag data.</param>
    /// <param name="opts">Module options that configure consumer group and cardinality limits.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="factory"/> or <paramref name="probe"/> is <see langword="null"/>.
    /// </exception>
    public KafkaLagCollector(IMetricFactory factory, IKafkaLagProbe probe, KafkaModuleOptions opts) : this(probe, factory, opts) { }

    /// <summary>
    /// Gets the friendly name of this metric collector.
    /// </summary>
    public static string Name => "KafkaLagCollector";

    /// <summary>
    /// Collects Kafka consumer lag metrics asynchronously for the configured consumer group.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// The populated multi-gauge metric instance when a consumer group is configured; otherwise <see langword="null"/>.
    /// If the probe returns no data, a single series with tag <c>status=empty</c> and value <c>0</c> is emitted.
    /// </returns>
    /// <remarks>
    /// If <see cref="KafkaModuleOptions.ConsumerGroup"/> is <see langword="null"/>, empty, or whitespace,
    /// the method returns <see langword="null"/> without producing metrics.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    public async Task<IMetric?> CollectAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ConsumerGroup))
        {
            return null;
        }

        var map = await _probe.GetLagAsync(_opts.ConsumerGroup!, ct).ConfigureAwait(false);

        // Cardinality control: Limit the number of series if MaxLagSeries is set
        var series = (_opts.MaxLagSeries > 0 ? map.Take(_opts.MaxLagSeries) : map).ToArray();

        if (series.Length == 0)
        {
            _lag.SetValue(0, new Dictionary<string, string> { ["status"] = "empty" });

            return _lag;
        }

        // Set the lag for each partition
        foreach (var ((topic, partition), lag) in series)
        {
            ct.ThrowIfCancellationRequested();

            _lag.SetValue(lag, new Dictionary<string, string>
            {
                ["topic"] = topic,
                ["partition"] = partition.ToString(),
                ["group"] = _opts.ConsumerGroup!
            });
        }

        return _lag;
    }

    /// <summary>
    /// Creates a summary metric with the specified quantiles and optional tags.
    /// </summary>
    /// <param name="id">The unique identifier (metric id) for the summary metric.</param>
    /// <param name="name">The human-readable name for the summary metric.</param>
    /// <param name="quantiles">
    /// The quantiles to include (e.g., 0.5, 0.9, 0.99). If <see langword="null"/>, defaults to <c>[0.5, 0.9, 0.99]</c>.
    /// </param>
    /// <param name="tags">Optional key–value tags to associate with the metric series.</param>
    /// <param name="resetOnGet">
    /// Whether to reset the summary on each scrape/read. The flag is forwarded by the underlying factory if supported.
    /// </param>
    /// <returns>A built <see cref="ISummaryMetric"/> configured with the provided quantiles and tags.</returns>
    /// <remarks>
    /// This is an explicit <see cref="IMetricCollector"/> implementation intended for advanced scenarios
    /// where a summary metric needs to be created programmatically by the collector.
    /// </remarks>
    ISummaryMetric IMetricCollector.CreateSummary(string id, string name, IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
    {
        var q = quantiles?.ToArray() ?? new[] { 0.5, 0.9, 0.99 };
        var sb = _factory.Summary(id, name).WithQuantiles(q);

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
    /// Creates a bucketed histogram metric with the specified bucket upper bounds and optional tags.
    /// </summary>
    /// <param name="id">The unique identifier (metric id) for the histogram metric.</param>
    /// <param name="name">The human-readable name for the histogram metric.</param>
    /// <param name="bucketUpperBounds">
    /// The inclusive upper bounds for histogram buckets. If <see langword="null"/>, an empty bound set is used.
    /// </param>
    /// <param name="tags">Optional key–value tags to associate with the metric series.</param>
    /// <returns>A built <see cref="IBucketHistogramMetric"/> configured with the provided bounds and tags.</returns>
    /// <remarks>
    /// This is an explicit <see cref="IMetricCollector"/> implementation intended for advanced scenarios
    /// where a histogram metric needs to be created programmatically by the collector.
    /// </remarks>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
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
