// <copyright file="CpuModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Modules;

/// <summary>
/// Represents the CPU module that wires up metric collectors (CPU usage, load, frequency, etc.)
/// based on the provided <see cref="CpuModuleOptions"/>.
/// Implements lifecycle hooks for initializing and disposing collectors.
/// </summary>
public sealed class CpuModule : IModule, IModuleLifecycle
{
    /// <summary>
    /// The constant name for the CPU module.
    /// </summary>
    public const string ModuleName = "NetMetric.CPU";

    /// <summary>
    /// Gets the name of the module.
    /// </summary>
    public string Name => ModuleName;

    /// <summary>
    /// Immutable collection of registered metric collectors.
    /// </summary>
    private readonly ImmutableArray<IMetricCollector> _collectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuModule"/> class.
    /// Builds the set of collectors based on the provided options.
    /// </summary>
    /// <param name="factory">The metric factory used to create collectors.</param>
    /// <param name="options">The options to configure the collectors. If null, default options will be used.</param>
    public CpuModule(IMetricFactory factory, CpuModuleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        options ??= new CpuModuleOptions();

        var list = new List<IMetricCollector>(8);

        // Core collectors (process/system CPU, core count, affinity, model, features)
        if (options.EnableCore)
        {
            list.Add(new ProcessCpuUsageCollector(factory));
            list.Add(new SystemCpuUsageCollector(factory));
            list.Add(new CpuCoreCountCollector(factory));
            list.Add(new CpuAffinityCollector(factory));
            list.Add(new CpuModelNameCollector(factory));
            list.Add(new CpuFeaturesCollector(factory));
        }

        // Per-core usage collectors
        if (options.EnablePerCore)
            list.Add(new PerCoreCpuUsageCollector(factory));

        // System load average
        if (options.EnableLoadAverage)
            list.Add(new CpuLoadAverageCollector(factory));

        // CPU frequency information
        if (options.EnableFrequency)
            list.Add(new CpuFrequencyCollector(factory));

        // Thermal sensors and fan speed
        if (options.EnableThermalAndFan)
            list.Add(new CpuThermalAndFanCollector(factory));

        // All processes CPU usage
        if (options.EnableAllProcesses)
            list.Add(new AllProcessesCpuUsageCollector(factory));

        // Current process threads CPU usage
        if (options.EnableThreads)
            list.Add(new CurrentProcessThreadCpuCollector(factory));

        // Freeze the list as an immutable array
        _collectors = list.ToImmutableArray();
    }

    /// <summary>
    /// Exposes the registered metric collectors to the metrics engine.
    /// </summary>
    /// <returns>An <see cref="IEnumerable{IMetricCollector}"/> of collectors.</returns>
    public IEnumerable<IMetricCollector> GetCollectors() => _collectors;

    /// <summary>
    /// Lifecycle hook for initialization.
    /// This method will be called during module initialization.
    /// </summary>
    public void OnInit()
    {
        foreach (var c in _collectors)
        {
            if (c is IModuleLifecycle lc)
            {
                lc.OnInit();
            }
        }
    }

    /// <summary>
    /// Lifecycle hook called before each collection round.
    /// This method will be called before collecting metrics.
    /// </summary>
    public void OnBeforeCollect()
    {
        foreach (var c in _collectors)
        {
            if (c is IModuleLifecycle lc)
            {
                lc.OnBeforeCollect();
            }
        }
    }

    /// <summary>
    /// Lifecycle hook called after each collection round.
    /// This method will be called after collecting metrics.
    /// </summary>
    public void OnAfterCollect()
    {
        foreach (var c in _collectors)
        {
            if (c is IModuleLifecycle lc)
            {
                lc.OnAfterCollect();
            }
        }
    }

    /// <summary>
    /// Lifecycle hook for cleanup.
    /// This method will be called during module disposal.
    /// </summary>
    public void OnDispose()
    {
        foreach (var c in _collectors)
        {
            if (c is IModuleLifecycle lc)
            {
                lc.OnDispose();
            }
            if (c is IDisposable d)
            {
                d.Dispose();
            }
        }
    }
}
