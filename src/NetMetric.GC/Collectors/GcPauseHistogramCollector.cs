// <copyright file="GcPauseHistogramCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector that captures the time spent in garbage collection (GC) as a percentage of total time, 
/// and records the samples in a bucket histogram. The histogram divides the samples into buckets and tracks
/// the cumulative count of samples within each bucket. The collector uses
/// and provides the results in a multi-gauge metric format.
/// </summary>
public sealed class GcPauseHistogramCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;
    private readonly IRuntimeGcMetricsSource _src;

    // Upper bounds for time-in-GC in percentage (le), configurable if needed.
    private static readonly double[] _bounds = new double[] { 0.1, 0.5, 1, 2, 5, 10, 20, 50 };

    /// <summary>
    /// Initializes a new instance of the <see cref="GcPauseHistogramCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <param name="src">The source of runtime GC metrics that provides time-in-GC percentage samples.</param>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="factory"/> or <paramref name="src"/> is null.</exception>
    public GcPauseHistogramCollector(IMetricFactory factory, IRuntimeGcMetricsSource src)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _src = src ?? throw new ArgumentNullException(nameof(src));
    }

    /// <summary>
    /// Collects the time spent in garbage collection as a percentage of total time and records it in a bucket histogram.
    /// The histogram is divided into predefined percentage buckets.
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// This method retrieves the time-in-GC percentage samples from,
    /// sorts them, and then distributes them into predefined buckets based on their values. The histogram 
    /// tracks the cumulative count of samples that fall within each bucket. It also records the total count 
    /// and sum of all samples, and the histogram uses a Tumbling window of 60 seconds for observation.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var samples = _src.SnapshotTimeInGcPercent();
        var n = samples.Length;
        var mg = _factory.MultiGauge("runtime.gc.pause.histogram", "GC Pause Histogram (time-in-gc %)").Build();

        if (n == 0)
        {
            // If no samples, all buckets are zero and sample count is zero.
            foreach (var b in _bounds)
            {
                mg.AddSibling("runtime.gc.pause.bucket", "time-in-gc <= bound (%)", 0, new Dictionary<string, string> { ["le"] = b.ToString("G", System.Globalization.CultureInfo.InvariantCulture) });
            }

            mg.AddSibling("runtime.gc.pause.count", "time-in-gc sample count", 0);
            mg.AddSibling("runtime.gc.pause.sum.percent", "sum of samples (%)", 0);

            return Task.FromResult<IMetric?>(mg);
        }

        // Sort the samples to calculate cumulative counts
        Array.Sort(samples);

        double sum = 0;

        foreach (var v in samples)
        {
            sum += v;
        }

        // Populate histogram buckets
        int idx = 0;

        for (int i = 0; i < _bounds.Length; i++)
        {
            var bound = _bounds[i];

            while (idx < n && samples[idx] <= bound)
            {
                idx++;
            }

            var cumCount = idx; // Cumulative count for each bucket

            mg.AddSibling("runtime.gc.pause.bucket", "time-in-gc <= bound (%)", cumCount, new Dictionary<string, string> { ["le"] = bound.ToString("G", System.Globalization.CultureInfo.InvariantCulture) });
        }

        // For the "infinity" bucket (samples greater than the largest bound)
        mg.AddSibling("runtime.gc.pause.bucket", "time-in-gc <= bound (%)", n, new Dictionary<string, string> { ["le"] = "+Inf" });

        // Add helpers: total sample count and total sum for averaging and other metrics
        mg.AddSibling("runtime.gc.pause.count", "time-in-gc sample count", n);
        mg.AddSibling("runtime.gc.pause.sum.percent", "sum of samples (%)", sum);

        return Task.FromResult<IMetric?>(mg);
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

        // resetOnGet parametresi şu an desteklenmiyorsa yok sayılıyor
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
