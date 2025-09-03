// <copyright file="DnsModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.DNS.Modules;

/// <summary>
/// Represents a DNS module for collecting DNS metrics and handling DNS-related data.
/// This module implements the <see cref="IModule"/> and <see cref="IModuleLifecycle"/> interfaces to integrate into the NetMetric system.
/// </summary>
/// <remarks>
/// The <see cref="DnsModule"/> class is responsible for collecting DNS metrics and resolving DNS-related information.
/// It includes collectors for DNS resolver endpoints, DNS probe results, and DNS address families.
/// The module can be initialized, collected from, and disposed of using the lifecycle methods defined by the <see cref="IModuleLifecycle"/> interface.
/// </remarks>
public sealed class DnsModule : IModule, IModuleLifecycle
{
    private readonly IMetricFactory _factory;
    private readonly Options.DnsOptions _opts;

    /// <summary>
    /// Gets the name of the DNS module.
    /// </summary>
    public string Name => "dns";

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsModule"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics for the module.</param>
    /// <param name="opts">The configuration options for the DNS module.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> or <paramref name="opts"/> is <c>null</c>.</exception>
    public DnsModule(IMetricFactory factory, Options.DnsOptions opts)
    {
        (_factory, _opts) = (factory, opts);
    }

    /// <summary>
    /// Retrieves the collectors associated with the DNS module.
    /// </summary>
    /// <returns>A collection of <see cref="IMetricCollector"/> instances responsible for collecting DNS-related metrics.</returns>
    /// <remarks>
    /// The method returns collectors for DNS resolver endpoints, DNS probes, and DNS address families.
    /// The probe collectors are only returned if the <see cref="Options.DnsOptions.ProbeHostnames"/> list is not empty.
    /// </remarks>
    public IEnumerable<IMetricCollector> GetCollectors()
    {
        // Resolver listesi her zaman yayınlanabilir
        yield return new Collectors.DnsResolverEndpointsCollector(_factory, _opts);

        // Host bazlı probe'lar, ancak ProbeHostnames boş değilse
        if (_opts.ProbeHostnames.Count > 0)
        {
            yield return new Collectors.DnsProbeCollector(_factory, _opts);
            yield return new Collectors.DnsAddressFamilyCollector(_factory, _opts);
        }
    }

    // ---- Lifecycle ----

    /// <summary>
    /// Called during module initialization. This method can be used to perform any necessary setup or warm-up operations.
    /// </summary>
    public void OnInit() { }

    /// <summary>
    /// Called before the module starts collecting metrics.
    /// </summary>
    public void OnBeforeCollect() { }

    /// <summary>
    /// Called after the module has collected metrics.
    /// </summary>
    public void OnAfterCollect() { }

    /// <summary>
    /// Called when the module is being disposed of. This method can be used for cleanup operations.
    /// </summary>
    public void OnDispose() { }
}
