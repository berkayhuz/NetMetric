// <copyright file="GcPauseDurationsSummaryCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector that captures garbage collection pause durations and computes summary statistics, including average, 
/// P95, P99, max, and sample count, in milliseconds. It uses to collect 
/// pause durations and provides them in a multi-gauge metric.
/// </summary>
public sealed class GcPauseDurationsSummaryCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GcPauseDurationsSummaryCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> is null.</exception>
    public GcPauseDurationsSummaryCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects garbage collection pause durations and computes summary statistics (average, P95, P99, max, count) asynchronously.
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// This method retrieves the pause durations from the garbage collector, computes the average, P95, P99, 
    /// maximum pause durations, and the total number of samples. The results are returned as a multi-gauge metric.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var mg = _factory.MultiGauge("runtime.gc.pause.duration", "GC Pause Duration (ms)").Build();

#if NET6_0_OR_GREATER
        // Retrieve pause durations from the GC memory info
        var info = System.GC.GetGCMemoryInfo();
        var pauses = info.PauseDurations;

        if (!pauses.IsEmpty)
        {
            var arr = new double[pauses.Length];

            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = pauses[i].TotalMilliseconds;
            }

            Array.Sort(arr);

            double avg = 0;

            foreach (var x in arr)
            {
                avg += x;
            }

            avg /= arr.Length;

            mg.AddSibling("runtime.gc.pause.duration.avg.ms", "Avg pause (ms)", avg);
            mg.AddSibling("runtime.gc.pause.duration.p95.ms", "P95 pause (ms)", Percentile(arr, 95));
            mg.AddSibling("runtime.gc.pause.duration.p99.ms", "P99 pause (ms)", Percentile(arr, 99));
            mg.AddSibling("runtime.gc.pause.duration.max.ms", "Max pause (ms)", arr[^1]);
            mg.AddSibling("runtime.gc.pause.duration.count", "Samples", arr.Length);
        }
        else
#endif
        {
            mg.AddSibling("runtime.gc.pause.duration.count", "Samples", 0);
        }

        return Task.FromResult<IMetric?>(mg);
    }

    /// <summary>
    /// Calculates the specified percentile from a sorted array of values.
    /// </summary>
    /// <param name="sortedAsc">The sorted array of values (ascending).</param>
    /// <param name="p">The percentile to calculate (e.g., 95 for P95).</param>
    /// <returns>The value at the specified percentile.</returns>
    private static double Percentile(double[] sortedAsc, int p)
    {
        ArgumentNullException.ThrowIfNull(sortedAsc);

        if (sortedAsc.Length == 0)
        {
            return 0;
        }

        var rank = (p / 100.0) * (sortedAsc.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);

        if (lo == hi)
        {
            return sortedAsc[lo];
        }

        var f = rank - lo;

        return sortedAsc[lo] + (sortedAsc[hi] - sortedAsc[lo]) * f;
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
        // quantiles null ise DefaultQuantiles kullanılır, array değilse ToArray ile snapshot alınır
        var q = quantiles as double[] ?? quantiles?.ToArray() ?? DefaultQuantiles;

        var builder = _factory.Summary(id, name).WithQuantiles(q);

        if (tags is not null)
        {
            foreach (var kv in tags)
            {
                builder.WithTag(kv.Key, kv.Value);
            }
        }

        // resetOnGet şu an desteklenmiyorsa yok sayılır
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
