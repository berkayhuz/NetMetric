// <copyright file="SystemIoModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SystemIO.Modules;

/// <summary>
/// Represents a metrics collection module for System I/O, providing both 
/// process-level and system-level throughput collectors depending on configuration.
/// </summary>
public sealed class SystemIoModule : IModule
{
    private readonly IMetricFactory _factory;
    private readonly SystemIoModuleOptions _opt;
    private readonly IProcessIoReader _proc;
    private readonly ISystemIoReader? _sys;

    /// <summary>
    /// Gets the name of the module.
    /// </summary>
    public string Name => "NetMetric.SystemIO";

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemIoModule"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metric instances.</param>
    /// <param name="opt">The options for configuring the module behavior, such as enabling process and system metrics.</param>
    /// <param name="proc">The process I/O reader used to collect process-level I/O metrics.</param>
    /// <param name="sys">The system I/O reader used to collect system-level I/O metrics, or <c>null</c> if system metrics are disabled.</param>
    public SystemIoModule(IMetricFactory factory, SystemIoModuleOptions opt, IProcessIoReader proc, ISystemIoReader? sys)
    {
        _factory = factory;
        _opt = opt;
        _proc = proc;
        _sys = sys;
    }

    /// <summary>
    /// Gets the collection of metric collectors defined by this module based on the configured options.
    /// </summary>
    /// <returns>
    /// An enumerable collection of <see cref="IMetricCollector"/> instances, 
    /// including process I/O throughput and system disk throughput collectors as applicable.
    /// </returns>
    public IEnumerable<IMetricCollector> GetCollectors()
    {
        // Return process I/O throughput collector if enabled in options
        if (_opt.EnableProcess)
        {
            yield return new ProcessIoThroughputCollector(_factory, _proc);
        }

        // Return system disk throughput collector if enabled in options and system I/O reader is available
        if (_opt.EnableSystem && _sys is not null)
        {
            yield return new SystemDiskThroughputCollector(_factory, _sys);
        }
    }
}
