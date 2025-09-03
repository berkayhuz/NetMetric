// <copyright file="GcAllocationRateHistogramCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using GcSys = System.GC;

namespace NetMetric.GC.Collectors;

/// <summary>
/// A collector that calculates the allocation rate (in bytes per second) based on the difference
/// in <see cref="GcSys.GetTotalAllocatedBytes"/> and writes the values to a bucket histogram.
/// The windowing policy is Tumbling, with a 60-second window, making it easier to observe sudden spikes in allocation rate.
/// </summary>
public sealed class GcAllocationRateHistogramCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;

    // State for the first measurement
    private long _lastAllocatedBytes;
    private bool _initialized;

    private readonly Stopwatch _sw = Stopwatch.StartNew();

    // Upper bounds for bytes per second (modifiable as needed)
    // 64KB/s, 256KB/s, 1MB/s, 4MB/s, 16MB/s, 64MB/s, 256MB/s, 1GB/s
    private const double KB = 1024d;
    private const double MB = 1024d * 1024d;
    private static readonly double[] _bounds = new double[]
    {
        64 * KB, 256 * KB, 1 * MB, 4 * MB, 16 * MB, 64 * MB, 256 * MB, 1024 * MB
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="GcAllocationRateHistogramCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> is null.</exception>
    public GcAllocationRateHistogramCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects the garbage collection allocation rate histogram asynchronously.
    /// </summary>
    /// <param name="ct">A cancellation token to observe for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, with the collected metric.</returns>
    /// <remarks>
    /// This method calculates the allocation rate as the change in allocated bytes divided by the time
    /// elapsed since the last collection. The allocation rate is then written to a histogram with a tumbling
    /// window of 60 seconds. Negative rates (e.g., due to system resets) are ignored.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var hist = _factory.Histogram("runtime.gc.alloc.rate.hist", "Allocation Rate Histogram (bytes/s)").WithBounds(_bounds).WithWindow(MetricWindowPolicy.Tumbling(TimeSpan.FromSeconds(60))).Build();

        var nowAllocated = GcSys.GetTotalAllocatedBytes(precise: false);

        if (!_initialized)
        {
            _initialized = true;
            _lastAllocatedBytes = nowAllocated;
            _sw.Restart();

            return Task.FromResult<IMetric?>(hist);
        }

        var elapsedSec = Math.Max(_sw.Elapsed.TotalSeconds, 1e-6);
        var delta = nowAllocated - _lastAllocatedBytes;
        var rate = delta / elapsedSec; // bytes/s

        _lastAllocatedBytes = nowAllocated;
        _sw.Restart();

        if (rate >= 0 && double.IsFinite(rate))
        {
            hist.Observe(rate);
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
