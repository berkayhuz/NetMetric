// <copyright file="DnsAddressFamilyCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.DNS.Modules;

namespace NetMetric.DNS.Collectors;

/// <summary>
/// Collector that gathers DNS address family breakdown, specifically the number of IPv4 and IPv6 addresses
/// for a list of hostnames configured in the <see cref="Options.DnsOptions.ProbeHostnames"/> setting.
/// This collector performs DNS lookups and categorizes the results based on the address family (IPv4 or IPv6).
/// </summary>
/// <remarks>
/// The collector performs DNS resolution for each configured hostname and counts the occurrences of IPv4 and IPv6 addresses.
/// It stores the results in a <see cref="IMultiGauge"/> metric with siblings for each address family (IPv4 and IPv6).
/// </remarks>
internal sealed class DnsAddressFamilyCollector : DnsCollectorBase
{
    private readonly IMultiGauge _gauge;

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsAddressFamilyCollector"/> class.
    /// </summary>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to create the <see cref="IMultiGauge"/> for the DNS address family breakdown.</param>
    /// <param name="options">The configuration options for the DNS collector, including the list of hostnames and resolve timeout.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> or <paramref name="options"/> is <c>null</c>.</exception>
    public DnsAddressFamilyCollector(IMetricFactory factory, Options.DnsOptions options) : base(factory, options)
    {
        _gauge = factory.MultiGauge("dns.resolve.address.family", "DNS Address Family Breakdown").WithInitialCapacity(64).WithResetOnGet(true).Build();
    }

    /// <summary>
    /// Asynchronously collects the DNS address family data (IPv4 and IPv6 breakdown) for the configured hostnames.
    /// </summary>
    /// <param name="ct">The cancellation token used to cancel the operation if requested.</param>
    /// <returns>A task that represents the asynchronous operation. The result is an <see cref="IMetric"/> containing the DNS address family data.</returns>
    /// <remarks>
    /// For each hostname in <see cref="Options.DnsOptions.ProbeHostnames"/>, this method performs DNS resolution and counts the number
    /// of IPv4 and IPv6 addresses. The results are stored as siblings in the <see cref="IMultiGauge"/>.
    /// If DNS resolution fails or times out, the error is silently ignored.
    /// </remarks>
    public override async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        _gauge.Clear();

        foreach (var host in Options.ProbeHostnames)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                cts.CancelAfter(Options.ResolveTimeout);

                // Perform DNS resolution for the given hostname
                var addrs = await Dns.GetHostAddressesAsync(host, cts.Token).ConfigureAwait(false);

                // Count the number of IPv4 and IPv6 addresses
                var v4 = addrs.Count(a => a.AddressFamily == AddressFamily.InterNetwork);
                var v6 = addrs.Count(a => a.AddressFamily == AddressFamily.InterNetworkV6);

                // If IPv4 addresses are found, add to the gauge
                if (v4 > 0)
                {
                    _gauge.AddSibling(
                        id: "dns.resolve.address.family",
                        name: "DNS Address Family",
                        value: v4,
                        tags: new Dictionary<string, string> { ["family"] = "ipv4", ["host"] = host }
                    );
                }

                // If IPv6 addresses are found and IPv6 is enabled, add to the gauge
                if (v6 > 0 && Options.EnableIPv6)
                {
                    _gauge.AddSibling(
                        id: "dns.resolve.address.family",
                        name: "DNS Address Family",
                        value: v6,
                        tags: new Dictionary<string, string> { ["family"] = "ipv6", ["host"] = host }
                    );
                }
            }
            catch
            {
                throw;
            }
        }

        return _gauge;
    }
}
