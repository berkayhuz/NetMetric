// <copyright file="GcOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.GC.Options;

/// <summary>
/// A class representing configuration options for garbage collection (GC) metrics collection, including feature toggles, 
/// histogram bounds, and sampling intervals. These options control which GC-related metrics are enabled and their 
/// configuration for collecting and reporting GC statistics such as pause times, heap growth, allocation rates, and more.
/// </summary>
public sealed class GcOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to enable event counter collectors for pause percentage and heap change collectors.
    /// </summary>
    public bool EnableEventCounterCollectors { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable the allocation rate histogram collector.
    /// </summary>
    public bool EnableAllocationRateHistogram { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable the pause bucket histogram collector.
    /// </summary>
    public bool EnablePauseBucketHistogram { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable the heap growth histogram collector.
    /// </summary>
    public bool EnableHeapGrowthHistogram { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable the heap shrink histogram collector.
    /// </summary>
    public bool EnableHeapShrinkHistogram { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable the pause durations histogram collector.
    /// </summary>
    public bool EnablePauseDurationsHistogram { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable the pause durations summary collector.
    /// </summary>
    public bool EnablePauseDurationsSummary { get; set; } = true;

    private double[] _pausePercentBounds = { 0.1, 0.5, 1, 2, 5, 10, 20, 50 };

    /// <summary>
    /// Gets or sets the bounds for pause percentages. These bounds define the bucket ranges for time-in-GC percentage metrics.
    /// </summary>
    public IReadOnlyList<double> PausePercentBounds
    {
        get => _pausePercentBounds;
        set => _pausePercentBounds = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    private double[] _allocationRateBounds = new double[]
    {
        64 * 1024d, 256 * 1024d, 1 * 1024d * 1024d, 4 * 1024d * 1024d,
        16 * 1024d * 1024d, 64 * 1024d * 1024d, 256 * 1024d * 1024d, 1024d * 1024d * 1024d
    };


    /// <summary>
    /// Gets or sets the bounds for allocation rate in bytes per second. These bounds define the bucket ranges for allocation rate histograms.
    /// </summary>
    public IReadOnlyList<double> AllocationRateBounds
    {
        get => _allocationRateBounds;
        set => _allocationRateBounds = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    private double[] _heapRateBounds = new double[]
    {
        32 * 1024d, 128 * 1024d, 512 * 1024d, 2 * 1024d * 1024d, 8 * 1024d * 1024d,
        32 * 1024d * 1024d, 128 * 1024d * 1024d, 512 * 1024d * 1024d
    };

    /// <summary>
    /// Gets or sets the bounds for heap rate in bytes per second (for heap growth and shrink).
    /// These bounds define the bucket ranges for heap rate histograms.
    /// </summary>
    public IReadOnlyList<double> HeapRateBounds
    {
        get => _heapRateBounds;
        set => _heapRateBounds = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    private double[] _pauseDurationBoundsMs = new double[]
    {
        0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000
    };

    /// <summary>
    /// Gets or sets the bounds for pause durations in milliseconds.
    /// These bounds define the bucket ranges for pause duration histograms.
    /// </summary>
    public IReadOnlyList<double> PauseDurationBoundsMs
    {
        get => _pauseDurationBoundsMs;
        set => _pauseDurationBoundsMs = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the time window for histograms. The default value is 60 seconds.
    /// </summary>
    public TimeSpan HistogramWindow { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the sampling interval for system runtime event counters in seconds. The default value is 1 second.
    /// </summary>
    public int EventCounterIntervalSec { get; set; } = 1;
}
