// <copyright file="GcModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.GC.Modules;

/// <summary>
/// The <see cref="GcModule"/> class provides garbage collection (GC) related metrics for monitoring, including 
/// collectors for GC counts, memory usage, allocation rates, pause times, and heap change rates. It implements 
/// <see cref="IModule"/> and <see cref="IModuleLifecycle"/> to integrate with the NetMetric system for metric collection.
/// </summary>
public sealed class GcModule : IModule, IModuleLifecycle
{
    /// <summary>
    /// The name of the module, which is "gc".
    /// </summary>
    public string Name => "gc";

    private readonly IMetricFactory _factory;
    private readonly IRuntimeGcMetricsSource _src;

    /// <summary>
    /// Initializes a new instance of the <see cref="GcModule"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <param name="src">The source of runtime GC metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> or <paramref name="src"/> is null.</exception>
    public GcModule(IMetricFactory factory, IRuntimeGcMetricsSource src)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _src = src ?? throw new ArgumentNullException(nameof(src));
    }

    /// <summary>
    /// Returns the collection of metric collectors associated with the GC module.
    /// </summary>
    /// <returns>A collection of <see cref="IMetricCollector"/> instances for GC-related metrics.</returns>
    public IEnumerable<IMetricCollector> GetCollectors() => new IMetricCollector[]
    {
        new Collectors.GcCountsCollector(_factory),
        new Collectors.GcMemoryCollector(_factory),
        new Collectors.GcDetailedMemoryCollector(_factory),
        new Collectors.GcAllocationRateCollector(_factory),
        new Collectors.GcRuntimeFlagsCollector(_factory),
        new Collectors.GcPauseCountersCollector(_factory, _src),
        new Collectors.GcPauseHistogramCollector(_factory, _src),
        new Collectors.GcPauseBucketHistogramCollector(_factory, _src),
        new Collectors.GcAllocationRateHistogramCollector(_factory),
        new Collectors.GcRuntimeFlagsCollector(_factory),
        new Collectors.GcHeapChangeRateHistogramCollector(_factory, _src),
        new Collectors.GcHeapGrowthRateHistogramCollector(_factory, _src),
        new Collectors.GcHeapShrinkRateHistogramCollector(_factory, _src),
        new Collectors.GcPauseDurationsBucketHistogramCollector(_factory),
        new Collectors.GcPauseDurationsSummaryCollector(_factory)
    };

    /// <summary>
    /// Lifecycle method called during the initialization phase.
    /// </summary>
    public void OnInit()
    {
    }

    /// <summary>
    /// Lifecycle method called before metric collection starts.
    /// </summary>
    public void OnBeforeCollect()
    {
    }

    /// <summary>
    /// Lifecycle method called after metric collection finishes.
    /// </summary>
    public void OnAfterCollect()
    {
    }

    /// <summary>
    /// Lifecycle method called during the disposal phase.
    /// </summary>
    public void OnDispose()
    {
        (_src as IDisposable)?.Dispose();
    }
}
