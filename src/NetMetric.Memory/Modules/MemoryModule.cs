// <copyright file="MemoryModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Collections.Immutable;
using NetMetric.Memory.Collectors;

namespace NetMetric.Memory.Modules;

/// <summary>
/// Represents the memory module that collects memory-related metrics from various sources.
/// This module can collect data from process memory, system memory, garbage collection statistics, and more.
/// </summary>
public sealed class MemoryModule : IModule, IModuleLifecycle
{
    /// <summary>
    /// The name of the memory module.
    /// </summary>
    public const string ModuleName = "NetMetric.Memory";

    /// <summary>
    /// Gets the name of the memory module.
    /// </summary>
    public string Name => ModuleName;

    private readonly ImmutableArray<IMetricCollector> _collectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryModule"/> class with the specified factory and options.
    /// </summary>
    /// <param name="factory">The factory used to create metric collectors.</param>
    /// <param name="options">Optional configuration options to control which collectors are enabled.</param>
    /// <exception cref="ArgumentNullException">Thrown if the factory is null.</exception>
    public MemoryModule(IMetricFactory factory, MemoryModuleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        options ??= new MemoryModuleOptions();

        var list = new List<IMetricCollector>(6);

        if (options.EnableProcess)
        {
            list.Add(new ProcessMemoryCollector(factory));
        }

        if (options.EnableSystem)
        {
            list.Add(new SystemMemoryCollector(factory, options));
        }

        if (options.EnableGc)
        {
            list.Add(new GcInfoCollector(factory));
        }

        _collectors = list.ToImmutableArray();
    }

    /// <summary>
    /// Gets the list of collectors used by the memory module.
    /// </summary>
    /// <returns>An enumeration of <see cref="IMetricCollector"/> instances.</returns>
    public IEnumerable<IMetricCollector> GetCollectors() => _collectors;

    /// <summary>
    /// Initializes the module and its collectors by invoking their <see cref="IModuleLifecycle.OnInit"/> method.
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
    /// Invokes the <see cref="IModuleLifecycle.OnBeforeCollect"/> method on each collector before data collection begins.
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
    /// Invokes the <see cref="IModuleLifecycle.OnAfterCollect"/> method on each collector after data collection is complete.
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
    /// Disposes of the memory module and all of its collectors.
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
