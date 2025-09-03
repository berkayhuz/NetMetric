// <copyright file="DnsResolverEndpointsCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.DNS.Modules;

namespace NetMetric.DNS.Collectors;

/// <summary>
/// Collector that collects DNS resolver endpoints (DNS server addresses) from the network interfaces of the system.
/// This collector retrieves the DNS addresses configured for each network interface and reports them as metrics.
/// </summary>
/// <remarks>
/// The collector queries the system's network interfaces and extracts the DNS addresses from each interface's IP properties.
/// It records these DNS resolver endpoints and tags them with the corresponding network interface name.
/// The results are stored in a multi-gauge metric, where each DNS resolver endpoint is a sibling of the gauge.
/// </remarks>
internal sealed class DnsResolverEndpointsCollector : DnsCollectorBase
{
    private readonly IMultiGauge _resolvers;

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsResolverEndpointsCollector"/> class.
    /// </summary>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to create the multi-gauge metric for DNS resolver endpoints.</param>
    /// <param name="options">The configuration options for the DNS collector, including DNS resolve settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> or <paramref name="options"/> is <c>null</c>.</exception>
    public DnsResolverEndpointsCollector(IMetricFactory factory, Options.DnsOptions options) : base(factory, options)
    {
        _resolvers = factory.MultiGauge("dns.resolver.endpoints", "DNS Resolver Endpoints").WithInitialCapacity(32).WithResetOnGet(true).Build();
    }

    /// <summary>
    /// Asynchronously collects the DNS resolver endpoints for all network interfaces on the system.
    /// </summary>
    /// <param name="ct">The cancellation token to cancel the operation if requested.</param>
    /// <returns>A task that represents the asynchronous operation. The result is an <see cref="IMetric"/> containing the collected resolver endpoints.</returns>
    /// <remarks>
    /// This method retrieves the DNS addresses from all network interfaces on the system. It iterates over each interface, and for each DNS address found, 
    /// it adds a sibling to the <see cref="IMultiGauge"/> with the corresponding network interface name and DNS address as tags.
    /// </remarks>
    public override Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        _resolvers.Clear();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ct.IsCancellationRequested)
                break;

            var ipProps = ni.GetIPProperties();

            foreach (var addr in ipProps.DnsAddresses)
            {
                _resolvers.AddSibling(
                    id: "dns.resolver.endpoint",
                    name: "DNS Resolver Endpoint",
                    value: 1,
                    tags: new Dictionary<string, string>
                    {
                        ["resolver"] = addr.ToString(),
                        ["ifname"] = ni.Name
                    });
            }
        }

        return Task.FromResult<IMetric?>(_resolvers);
    }
}
