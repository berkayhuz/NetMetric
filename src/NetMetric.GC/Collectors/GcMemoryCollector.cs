// <copyright file="GcMemoryCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using GcSys = System.GC;

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector that captures a snapshot of the garbage collector (GC) memory statistics, including total memory,
/// allocated memory, and the current GC latency mode. This class collects data asynchronously and provides it in
/// a multi-gauge metric format.
/// </summary>
public sealed class GcMemoryCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GcMemoryCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> is null.</exception>
    public GcMemoryCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects a snapshot of the garbage collector's memory statistics asynchronously.
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// This method collects the total memory used by the garbage collector, the total allocated memory in the
    /// process, and the current GC latency mode. The collected data is returned as a multi-gauge metric.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Create a multi-gauge metric for GC memory snapshot
        var mg = _factory.MultiGauge("runtime.gc.memory", "GC Memory Snapshot").Build();

        // Add the total memory, allocated memory, and GC latency mode flag as siblings
        mg.AddSibling("runtime.gc.heap.total.bytes", "GC Heap Total (bytes)", GcSys.GetTotalMemory(false));
        mg.AddSibling("runtime.gc.allocated.total.bytes", "Total Allocated (bytes)", GcSys.GetTotalAllocatedBytes(false), new Dictionary<string, string> { ["note"] = "lifetime-process" });

        var mode = GCSettings.LatencyMode.ToString();

        mg.AddSibling("runtime.gc.latency.mode.flag", "GC Latency Mode Flag", 1, new Dictionary<string, string> { ["mode"] = mode });

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
    public ISummaryMetric CreateSummary(string id, string name, IEnumerable<double> quantiles,
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
