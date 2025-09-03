// <copyright file="NetMetricGcServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using NetMetric.Abstractions;
using NetMetric.GC.Modules;
using NetMetric.GC.Runtime;

namespace NetMetric.GC.DependencyInjection;

/// <summary>
/// Extension methods to register the GC module and related services with the dependency injection container.
/// This class provides a method to add the GC module to the NetMetric system after the core services have been set up.
/// </summary>
public static class NetMetricGcServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GC module and related services with the <see cref="IServiceCollection"/>.
    /// This method sets up the <see cref="IRuntimeGcMetricsSource"/> and <see cref="IModule"/> for garbage collection metrics.
    /// </summary>
    /// <param name="services">The service collection to which the GC module and services will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with the GC module and services registered.</returns>
    /// <remarks>
    /// This method registers the <see cref="SystemRuntimeCountersListener"/> as the <see cref="IRuntimeGcMetricsSource"/> 
    /// and the <see cref="GcModule"/> as an <see cref="IModule"/> with the dependency injection container.
    /// It also triggers the options pipeline for the <see cref="MetricOptions"/>.
    /// </remarks>
    public static IServiceCollection AddNetMetricGc(this IServiceCollection services)
    {
        // Registry and Factory are already added via AddNetMetricCore
        services.PostConfigure<MetricOptions>(_ => { }); // Triggers the options pipeline

        // Register the runtime GC metrics source
        services.AddSingleton<IRuntimeGcMetricsSource, SystemRuntimeCountersListener>();

        // Register the GC module
        services.AddSingleton<IModule>(sp =>
        {
            var factory = sp.GetRequiredService<IMetricFactory>();

            return new GcModule(factory, sp.GetRequiredService<IRuntimeGcMetricsSource>());
        });

        // If the project uses a central ModuleLoader, it can be preferred instead
        return services;
    }
}
