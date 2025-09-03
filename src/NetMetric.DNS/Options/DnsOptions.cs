// <copyright file="DnsOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.DNS.Options;

/// <summary>
/// Configuration options for the DNS module, which defines various settings for DNS resolution and probes.
/// This class allows fine-grained control over how DNS probes are performed, including timeout settings, concurrency limits, and IPv6 support.
/// </summary>
/// <remarks>
/// This class holds the options required to configure the DNS module's behavior, including which hostnames to probe, the timeout for each DNS resolution, 
/// the maximum concurrency for DNS resolution requests, and whether IPv6 should be considered during the resolution process.
/// </remarks>
public sealed class DnsOptions
{
    /// <summary>
    /// Gets or sets the list of hostnames to probe for DNS resolution.
    /// If the list is empty, the associated collectors will not operate.
    /// </summary>
    /// <remarks>
    /// Example: <c>["example.com", "microsoft.com"]</c>. If this list is empty, no DNS probe collectors will be active.
    /// </remarks>
    public IReadOnlyList<string> ProbeHostnames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the maximum time to wait for a DNS resolution to complete for each hostname.
    /// </summary>
    /// <remarks>
    /// The timeout value is used to ensure that DNS resolution does not block indefinitely. The default is set to 2 seconds.
    /// </remarks>
    public TimeSpan ResolveTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the maximum number of concurrent DNS resolution requests that can be made.
    /// </summary>
    /// <remarks>
    /// This value ensures that a limited number of parallel DNS queries are executed to avoid overwhelming the system.
    /// The default value is half of the processor count, with a minimum of 1.
    /// </remarks>
    public int MaxConcurrency { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>
    /// Gets or sets a value indicating whether IPv6 should be used during DNS resolution.
    /// </summary>
    /// <remarks>
    /// If set to <c>true</c>, both IPv4 and IPv6 will be attempted during the DNS resolution. If set to <c>false</c>, only IPv4 will be used.
    /// The default value is <c>true</c>.
    /// </remarks>
    public bool EnableIPv6 { get; init; } = true;
}
