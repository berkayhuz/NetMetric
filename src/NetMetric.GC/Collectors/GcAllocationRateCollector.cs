// <copyright file="GcAllocationRateCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using GcSys = System.GC;

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector for measuring the rate of garbage collection allocation in bytes per second.
/// This collector tracks the total allocated bytes by the garbage collector and calculates
/// the allocation rate over time.
/// </summary>
public sealed class GcAllocationRateCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    private long _lastAllocatedBytes;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="GcAllocationRateCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> is null.</exception>
    public GcAllocationRateCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects the garbage collection allocation rate asynchronously.
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// The allocation rate is calculated as the change in allocated bytes divided by the time
    /// elapsed since the last collection. The rate is updated each time this method is called.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var gauge = _factory.Gauge("runtime.gc.alloc.rate.bytes_per_sec", "Allocation Rate (bytes/s)").Build();
        var nowAllocated = GcSys.GetTotalAllocatedBytes(precise: false);

        if (!_initialized)
        {
            _initialized = true;
            _lastAllocatedBytes = nowAllocated;
            _sw.Restart();

            gauge.SetValue(0);

            return Task.FromResult<IMetric?>(gauge);
        }

        var elapsedSec = Math.Max(_sw.Elapsed.TotalSeconds, 1e-6);
        var delta = nowAllocated - _lastAllocatedBytes;
        var rate = delta / elapsedSec;

        _lastAllocatedBytes = nowAllocated;
        _sw.Restart();

        if (rate >= 0 && double.IsFinite(rate))
        {
            gauge.SetValue(rate);
        }
        else
        {
            gauge.SetValue(0);
        }

        return Task.FromResult<IMetric?>(gauge);
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
        var q = quantiles?.ToArray() ?? DefaultQuantiles;

        return _factory
            .Summary(id, name)
            .WithQuantiles(q)
            .Build();
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
