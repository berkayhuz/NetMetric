// <copyright file="GcPauseCountersCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector that captures GC pause time as a percentage of total time, including average, P95, and P99 percentiles.
/// It also collects information on the current heap size and the total number of collections for each GC generation (Gen0, Gen1, Gen2).
/// </summary>
public sealed class GcPauseCountersCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;
    private readonly IRuntimeGcMetricsSource _src;

    /// <summary>
    /// Initializes a new instance of the <see cref="GcPauseCountersCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <param name="src">The source of runtime GC metrics that provides time-in-GC percentage and heap information.</param>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="factory"/> or <paramref name="src"/> is null.</exception>
    public GcPauseCountersCollector(IMetricFactory factory, IRuntimeGcMetricsSource src)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _src = src ?? throw new ArgumentNullException(nameof(src));
    }

    /// <summary>
    /// Collects GC pause statistics asynchronously, including average time in GC, 95th and 99th percentiles, 
    /// heap size, and GC collection counts for each generation (Gen0, Gen1, Gen2).
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// This method calculates the average, P95, and P99 percentiles of the time spent in GC as a percentage of total time,
    /// collects the current heap size, and the total GC collection counts for each generation. The results are returned
    /// as a multi-gauge metric.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var samples = _src.SnapshotTimeInGcPercent();

        double avg = 0, p95 = 0, p99 = 0;

        if (samples.Length > 0)
        {
            var copy = samples.ToArray();

            Array.Sort(copy);

            avg = samples.Average();

            p95 = Percentile(copy, 95);
            p99 = Percentile(copy, 99);
        }

        var mg = _factory.MultiGauge("runtime.gc.pause", "GC Pause (time-in-gc %)").Build();

        mg.AddSibling("runtime.gc.pause.avg.percent", "Avg time-in-gc (%)", avg);
        mg.AddSibling("runtime.gc.pause.p95.percent", "P95 time-in-gc (%)", p95);
        mg.AddSibling("runtime.gc.pause.p99.percent", "P99 time-in-gc (%)", p99);

        var heap = _src.CurrentHeapBytes();

        if (heap is double hb)
        {
            mg.AddSibling("runtime.gc.heap.size.bytes", "Heap Size (bytes)", hb);
        }

        var (g0, g1, g2) = _src.CurrentGenCounts();

        if (g0 is double d0)
        {
            mg.AddSibling("runtime.gc.collections.gen0.total", "Gen0 GC Count (total)", d0);
        }

        if (g1 is double d1)
        {
            mg.AddSibling("runtime.gc.collections.gen1.total", "Gen1 GC Count (total)", d1);
        }

        if (g2 is double d2)
        {
            mg.AddSibling("runtime.gc.collections.gen2.total", "Gen2 GC Count (total)", d2);
        }

        return Task.FromResult<IMetric?>(mg);
    }

    /// <summary>
    /// Calculates the specified percentile from a sorted array of values.
    /// </summary>
    /// <param name="sorted">The sorted array of values.</param>
    /// <param name="p">The percentile to calculate (e.g., 95 for P95).</param>
    /// <returns>The value at the specified percentile.</returns>
    private static double Percentile(double[] sorted, int p)
    {
        ArgumentNullException.ThrowIfNull(sorted);

        if (sorted.Length == 0)
        {
            return 0;
        }

        var rank = (p / 100.0) * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);

        if (lo == hi)
        {
            return sorted[lo];
        }

        var frac = rank - lo;

        return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
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
        // Use provided quantiles or fall back to default quantiles
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
