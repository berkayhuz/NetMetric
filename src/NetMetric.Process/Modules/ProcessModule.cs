// <copyright file="ProcessModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Process.Abstractions;
using NetMetric.Process.Configuration;

namespace NetMetric.Process.Modules;

/// <summary>
/// The ProcessModule is responsible for collecting various process-related metrics, including CPU usage, memory usage,
/// thread count, and uptime. It implements the <see cref="IModule"/> and <see cref="IModuleLifecycle"/> interfaces.
/// </summary>
public sealed class ProcessModule : IModule, IModuleLifecycle
{
    /// <summary>
    /// The name of the module.
    /// </summary>
    public const string ModuleName = "NetMetric.Process";

    /// <summary>
    /// Gets the name of the module.
    /// </summary>
    public string Name => ModuleName;

    private readonly ImmutableArray<IMetricCollector> _collectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessModule"/> class.
    /// The constructor sets up the metric collectors based on the provided options.
    /// </summary>
    /// <param name="factory">The factory used to create metric instruments.</param>
    /// <param name="proc">The process information provider used to retrieve process metrics.</param>
    /// <param name="options">The options used to configure the collection of process metrics.</param>
    public ProcessModule(IMetricFactory factory, IProcessInfoProvider proc, ProcessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(proc);

        options ??= new ProcessOptions();

        var list = new List<IMetricCollector>(4);

        if (options.EnableCpu)
        {
            list.Add(new ProcessCpuCollector(factory, proc, options));
        }

        if (options.EnableMemory)
        {
            list.Add(new ProcessMemoryCollector(factory, proc, options));
        }

        if (options.EnableThreads)
        {
            list.Add(new ProcessThreadCollector(factory, proc, options));
        }

        if (options.EnableUptime)
        {
            list.Add(new ProcessUptimeCollector(factory, proc, options));
        }

        _collectors = list.ToImmutableArray();
    }

    /// <summary>
    /// Gets the collection of metric collectors for this module.
    /// </summary>
    /// <returns>An enumerable collection of <see cref="IMetricCollector"/> instances.</returns>
    public IEnumerable<IMetricCollector> GetCollectors() => _collectors;

    /// <summary>
    /// Called during module initialization. This method is currently empty.
    /// </summary>
    public void OnInit()
    {
    }

    /// <summary>
    /// Called before collecting metrics. This method is currently empty.
    /// </summary>
    public void OnBeforeCollect()
    {
    }

    /// <summary>
    /// Called after collecting metrics. This method is currently empty.
    /// </summary>
    public void OnAfterCollect()
    {
    }

    /// <summary>
    /// Called when disposing of the module. This method is currently empty.
    /// </summary>
    public void OnDispose()
    {
    }
}
