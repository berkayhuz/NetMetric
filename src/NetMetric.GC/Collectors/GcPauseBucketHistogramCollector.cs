// <copyright file="GcPauseBucketHistogramCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector that captures the time spent in garbage collection (GC) as a percentage of total time.
/// It writes the percentage samples to a bucket histogram with a 60-second Tumbling window.
/// The collector uses System.Runtime EventCounters to capture these samples.
/// </summary>
public sealed class GcPauseBucketHistogramCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;
    private readonly IRuntimeGcMetricsSource _src;

    // Bucket upper bounds for time-in-GC in percentage (le), configurable if needed.
    private static readonly double[] _bounds = new double[] { 0.1, 0.5, 1, 2, 5, 10, 20, 50 };

    /// <summary>
    /// Initializes a new instance of the <see cref="GcPauseBucketHistogramCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <param name="src">The source of runtime GC metrics that provides time-in-GC percentage samples.</param>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="factory"/> or <paramref name="src"/> is null.</exception>
    public GcPauseBucketHistogramCollector(IMetricFactory factory, IRuntimeGcMetricsSource src)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _src = src ?? throw new ArgumentNullException(nameof(src));
    }

    /// <summary>
    /// Collects the time spent in garbage collection as a percentage of total time and records it in a bucket histogram.
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// This method collects the time-in-GC percentage samples from EventCounters, writes them to a histogram, and
    /// rolls over every 60 seconds (Tumbling window). The samples are recorded in the predefined percentage buckets.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Create a histogram with a 60-second Tumbling window
        var hist = _factory.Histogram("runtime.gc.pause.hist", "GC Pause Histogram (time-in-gc %)").WithBounds(_bounds).WithWindow(MetricWindowPolicy.Tumbling(TimeSpan.FromSeconds(60))).Build();

        // Capture time-in-GC percentage samples
        var samples = _src.SnapshotTimeInGcPercent();

        if (samples.Length > 0)
        {
            // Write the percentage samples (0 to 100%) to the histogram
            for (int i = 0; i < samples.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                hist.Observe(samples[i]);
            }
        }

        return Task.FromResult<IMetric?>(hist);
    }

    /// <summary>
    /// Creates a summary metric for tracking statistical summaries of a given data series.
    /// </summary>
    /// <param name="id">The unique identifier for the metric.</param>
    /// <param name="name">The name of the metric.</param>
    /// <param name="quantiles">The quantiles for the summary (e.g., 0.5, 0.9, 0.99).</param>
    /// <param name="tags">Optional tags to associate with the metric.</param>
    /// <param name="resetOnGet">Indicates whether the summary should reset on retrieval.</param>
    /// <returns>A <see cref="ISummaryMetric"/> representing the summary metric.</returns>
    public ISummaryMetric CreateSummary(
    string id,
    string name,
    IEnumerable<double> quantiles,
    IReadOnlyDictionary<string, string>? tags,
    bool resetOnGet)
    {
        var q = quantiles as double[] ?? quantiles?.ToArray() ?? DefaultQuantiles;

        var builder = _factory.Summary(id, name).WithQuantiles(q);

        if (tags is not null)
        {
            foreach (var kv in tags)
            {
                builder.WithTag(kv.Key, kv.Value);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates a bucket histogram metric for tracking distributions of data with predefined bucket bounds.
    /// </summary>
    /// <param name="id">The unique identifier for the metric.</param>
    /// <param name="name">The name of the metric.</param>
    /// <param name="bucketUpperBounds">The upper bounds of the histogram buckets.</param>
    /// <param name="tags">Optional tags to associate with the metric.</param>
    /// <returns>A <see cref="IBucketHistogramMetric"/> representing the bucket histogram metric.</returns>
    public IBucketHistogramMetric CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
    {
        return _factory.Histogram(id, name).WithBounds(bucketUpperBounds?.ToArray() ?? Array.Empty<double>()).Build();
    }
}
