// <copyright file="DnsServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMetric.DNS.Modules;
using NetMetric.DNS.Options;

namespace NetMetric.DNS.DependencyInjection;

/// <summary>
/// Extension methods for configuring the DNS module in the <see cref="IServiceCollection"/>.
/// These methods provide easy integration of DNS functionality into a .NET application, 
/// allowing for customization of DNS-related options and the registration of necessary services.
/// </summary>
/// <remarks>
/// The <see cref="DnsServiceCollectionExtensions"/> class provides methods to add and configure the DNS module to the application's dependency injection (DI) container.
/// It offers an optional configuration step for <see cref="DnsOptions"/> and registers the necessary services for the DNS module.
/// </remarks>
public static class DnsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the DNS module to the service collection with optional configuration for <see cref="DnsOptions"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the DNS services to.</param>
    /// <param name="configure">An optional action to configure <see cref="DnsOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with DNS services added.</returns>
    /// <remarks>
    /// This method registers the DNS module and its related options. If a configuration action is provided, 
    /// it is applied to the <see cref="DnsOptions"/> before registration. The method also ensures that 
    /// the <see cref="DnsModule"/> is added to the DI container as a singleton service.
    /// </remarks>
    public static IServiceCollection AddNetMetricDns(this IServiceCollection services, Action<DnsOptions>? configure = null)
    {
        services.AddOptions<DnsOptions>();

        if (configure is not null)
        {
            services.PostConfigure(configure);
        }

        services.TryAddSingleton<DnsModule>();

        return services;
    }
}
