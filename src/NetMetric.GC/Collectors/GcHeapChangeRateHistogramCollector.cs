// <copyright file="GcHeapChangeRateHistogramCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector that calculates the heap change rate (Δheap/s) based on the heap size retrieved from the
/// System.Runtime counters. It produces two histograms: one for heap growth and one for heap shrinkage (absolute value).
/// The histograms use a Tumbling window of 60 seconds.
/// </summary>
public sealed class GcHeapChangeRateHistogramCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;
    private readonly IRuntimeGcMetricsSource _src;

    private bool _initialized;
    private double _lastHeapBytes;
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    // Bytes/s bounds: 32KB/s .. 512MB/s
    private const double KB = 1024d;
    private const double MB = 1024d * 1024d;

    private static readonly double[] _bounds = new double[]
    {
        32 * KB, 128 * KB, 512 * KB, 2 * MB, 8 * MB, 32 * MB, 128 * MB, 512 * MB
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="GcHeapChangeRateHistogramCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <param name="src">The source of runtime GC metrics that provides the current heap size.</param>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="factory"/> or <paramref name="src"/> is null.</exception>
    public GcHeapChangeRateHistogramCollector(IMetricFactory factory, IRuntimeGcMetricsSource src)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _src = src ?? throw new ArgumentNullException(nameof(src));
    }

    /// <summary>
    /// Collects the heap change rate (growth and shrinkage) asynchronously, and records them in separate histograms.
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// This method calculates the rate of heap size change (growth or shrinkage) by comparing the current heap size 
    /// to the previous one, and then observes the rate in the appropriate histogram (growth or shrinkage). 
    /// The histograms are defined with specific bounds for the rate (in bytes per second).
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Create two histograms for growth and shrinkage with a 60s tumbling window
        var grow = _factory.Histogram("runtime.gc.heap.growth.rate.hist", "Heap Growth Rate Histogram (bytes/s)").WithBounds(_bounds).WithWindow(MetricWindowPolicy.Tumbling(TimeSpan.FromSeconds(60))).Build();

        var shrink = _factory.Histogram("runtime.gc.heap.shrink.rate.hist", "Heap Shrink Rate Histogram (bytes/s)").WithBounds(_bounds).WithWindow(MetricWindowPolicy.Tumbling(TimeSpan.FromSeconds(60))).Build();

        // Retrieve the current heap size
        var cur = _src.CurrentHeapBytes();

        if (cur is null || !double.IsFinite(cur.Value))
        {
            return Task.FromResult<IMetric?>(grow); // No data, return empty histogram
        }

        if (!_initialized)
        {
            _initialized = true;
            _lastHeapBytes = cur.Value;
            _sw.Restart();

            return Task.FromResult<IMetric?>(grow); // First call, no rate calculation yet
        }

        var elapsedSec = Math.Max(_sw.Elapsed.TotalSeconds, 1e-6);
        var deltaBytes = cur.Value - _lastHeapBytes;
        var rate = deltaBytes / elapsedSec; // Positive for growth, negative for shrinkage

        _lastHeapBytes = cur.Value;
        _sw.Restart();

        if (double.IsFinite(rate))
        {
            if (rate >= 0)
            {
                if (rate > 0)
                {
                    grow.Observe(rate); // Observe in growth histogram if rate is positive
                }
            }
            else
            {
                var mag = Math.Abs(rate);

                if (mag > 0)
                {
                    shrink.Observe(mag); // Observe in shrinkage histogram if rate is negative
                }
            }
        }

        return Task.FromResult<IMetric?>(grow); // Return the growth histogram (or shrink, depending on your use case)
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
