// <copyright file="GcDetailedMemoryCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using GcSys = System.GC;

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector that tracks detailed memory information from the garbage collector, including heap size,
/// fragmented memory, available memory, memory load, and the high memory load threshold. 
/// It also reports the garbage collection latency mode.
/// </summary>
public sealed class GcDetailedMemoryCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GcDetailedMemoryCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> is null.</exception>
    public GcDetailedMemoryCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects detailed memory metrics from the garbage collector asynchronously.
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// This method collects memory-related metrics, such as the heap size, fragmented memory, 
    /// total available memory, memory load, high memory load threshold, and memory load ratio. 
    /// It also reports the current garbage collection latency mode.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Get detailed GC memory info
        var info = GcSys.GetGCMemoryInfo();

        double highThresh = info.HighMemoryLoadThresholdBytes;
        double loadBytes = info.MemoryLoadBytes;
        double ratio = (highThresh > 0) ? loadBytes / highThresh : 0d;

        // Create multi-gauge metric for GC memory details
        var mg = _factory.MultiGauge("runtime.gc.memory.detailed", "GC Memory Detailed").Build();

        mg.AddSibling("runtime.gc.memory.heap.size.bytes", "Heap Size (bytes)", info.HeapSizeBytes);
        mg.AddSibling("runtime.gc.memory.fragmented.bytes", "Fragmented (bytes)", info.FragmentedBytes);
        mg.AddSibling("runtime.gc.memory.total.available.bytes", "Total Available (bytes)", info.TotalAvailableMemoryBytes);
        mg.AddSibling("runtime.gc.memory.load.bytes", "Memory Load (bytes)", info.MemoryLoadBytes);
        mg.AddSibling("runtime.gc.memory.high_load_threshold.bytes", "High Load Threshold (bytes)", info.HighMemoryLoadThresholdBytes);
        mg.AddSibling("runtime.gc.memory.load.ratio", "Memory Load Ratio", ratio);

        // Add latency mode flag
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
