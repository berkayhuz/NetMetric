// <copyright file="GcPauseDurationsBucketHistogramCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using GcSys = System.GC;

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector that captures the durations of the last few garbage collection (GC) pauses, in milliseconds, 
/// The pause durations are recorded in a bucket histogram with a 
/// 60-second Tumbling window.
/// </summary>
public sealed class GcPauseDurationsBucketHistogramCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;

    // Upper bounds for pause durations (in ms)
    private static readonly double[] _boundsMs = new[]
    {
        0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="GcPauseDurationsBucketHistogramCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> is null.</exception>
    public GcPauseDurationsBucketHistogramCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects GC pause durations asynchronously and records them in a bucket histogram.
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// This method collects the pause durations from the most recent garbage collections and records the
    /// durations in a bucket histogram with a Tumbling window of 60 seconds. The histogram uses predefined
    /// buckets to record the duration of each pause in milliseconds.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Create the histogram with a 60-second Tumbling window for pause durations
        var hist = _factory.Histogram("runtime.gc.pause.duration.hist", "GC Pause Duration Histogram (ms)").WithBounds(_boundsMs).WithWindow(MetricWindowPolicy.Tumbling(TimeSpan.FromSeconds(60))).Build();

        // Get the pause durations from the last few GCs
        var info = GcSys.GetGCMemoryInfo();
        var pauses = info.PauseDurations; // ReadOnlySpan<TimeSpan>

        if (!pauses.IsEmpty)
        {
            foreach (var ts in pauses)
            {
                ct.ThrowIfCancellationRequested();

                var ms = ts.TotalMilliseconds;

                // Defensive check: ensure the value is non-negative and finite
                if (ms >= 0 && double.IsFinite(ms))
                {
                    hist.Observe(ms);
                }
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
    // Sınıf içinde (field olarak) ekle:
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
