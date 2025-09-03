// <copyright file="GcCountsCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using GcSys = System.GC;

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector that tracks the number of garbage collection (GC) events for each generation (Gen0, Gen1, Gen2).
/// </summary>
public sealed class GcCountsCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GcCountsCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> is null.</exception>
    public GcCountsCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects the garbage collection counts for each generation asynchronously.
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// This method collects the count of garbage collection events for each generation (Gen0, Gen1, Gen2) 
    /// using <see cref="GcSys.CollectionCount"/> and reports them as sibling gauges in a multi-gauge metric.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Create a multi-gauge metric for GC collections
        var mg = _factory.MultiGauge("runtime.gc.collections", "GC Collections").Build();

        // Add siblings for Gen0, Gen1, and Gen2 collection counts
        mg.AddSibling("runtime.gc.collections.gen0", "Gen0 Collections", GcSys.CollectionCount(0));
        mg.AddSibling("runtime.gc.collections.gen1", "Gen1 Collections", GcSys.CollectionCount(1));
        mg.AddSibling("runtime.gc.collections.gen2", "Gen2 Collections", GcSys.CollectionCount(2));

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
    public ISummaryMetric CreateSummary(string id, string name, IEnumerable<double> quantiles, IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
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
    /// Creates a bucket histogram metric for tracking distributions of data with predefined bucket bounds.
    /// </summary>
    /// <param name="id">The unique identifier for the metric.</param>
    /// <param name="name">The name of the metric.</param>
    /// <param name="bucketUpperBounds">The upper bounds of the histogram buckets.</param>
    /// <param name="tags">Optional tags to associate with the metric.</param>
    /// <returns>A <see cref="IBucketHistogramMetric"/> representing the bucket histogram metric.</returns>
    public IBucketHistogramMetric CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
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
