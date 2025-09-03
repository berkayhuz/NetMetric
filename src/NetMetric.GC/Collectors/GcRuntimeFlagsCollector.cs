// <copyright file="GcRuntimeFlagsCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector that captures runtime garbage collection (GC) settings and flags, including:
/// - Server GC mode
/// - Large Object Heap (LOH) compaction mode
/// - Latency mode
/// </summary>
public sealed class GcRuntimeFlagsCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GcRuntimeFlagsCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> is null.</exception>
    public GcRuntimeFlagsCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects runtime garbage collection (GC) flags asynchronously and records them in a multi-gauge metric.
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// This method collects information about:
    /// - Whether Server GC is enabled.
    /// - The current Large Object Heap (LOH) compaction mode.
    /// - The current GC latency mode.
    /// The collected flags are returned as a multi-gauge metric.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var mg = _factory.MultiGauge("runtime.gc.flags", "GC Runtime Flags").Build();

        // Collect and record the Server GC flag
        mg.AddSibling("runtime.gc.server.enabled", "Server GC Enabled", GCSettings.IsServerGC ? 1 : 0);

        // Collect and record the Large Object Heap (LOH) compaction mode flag
        var loh = GCSettings.LargeObjectHeapCompactionMode.ToString();

        mg.AddSibling("runtime.gc.loh.compaction.flag", "LOH Compaction Mode Flag", 1, new Dictionary<string, string> { ["mode"] = loh });

        // Collect and record the Latency mode flag
        var latency = GCSettings.LatencyMode.ToString();

        mg.AddSibling("runtime.gc.latency.mode.flag", "Latency Mode Flag", 1, new Dictionary<string, string> { ["mode"] = latency });

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
