// <copyright file="MemoryServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMetric.Abstractions;
using NetMetric.Memory.Configuration;
using NetMetric.Memory.Modules;

namespace NetMetric.Memory.DependencyInjection;

/// <summary>
/// Provides extension methods for adding the NetMetric memory module to the <see cref="IServiceCollection"/>.
/// </summary>
public static class MemoryServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default (preset: Default) memory module to the service collection.
    /// Idempotent: the same module will not be added multiple times.
    /// </summary>
    /// <param name="services">The service collection to which the memory module is added.</param>
    /// <returns>The service collection with the memory module added.</returns>
    public static IServiceCollection AddNetMetricMemoryModule(this IServiceCollection services)
    {
        return services.AddNetMetricMemoryModule(MemoryModuleOptions.FromPreset(MemoryModuleOptions.MemoryModulePreset.Default));
    }

    /// <summary>
    /// Adds the memory module with the specified preset (Light/Default/Verbose) to the service collection.
    /// </summary>
    /// <param name="services">The service collection to which the memory module is added.</param>
    /// <param name="preset">The preset to use for configuring the memory module.</param>
    /// <returns>The service collection with the memory module added.</returns>
    public static IServiceCollection AddNetMetricMemoryModule(this IServiceCollection services, MemoryModuleOptions.MemoryModulePreset preset)
    {
        return services.AddNetMetricMemoryModule(_ => MemoryModuleOptions.FromPreset(preset));
    }

    /// <summary>
    /// Adds the memory module using a specific <see cref="MemoryModuleOptions"/> instance to the service collection.
    /// </summary>
    /// <param name="services">The service collection to which the memory module is added.</param>
    /// <param name="options">The configuration options for the memory module.</param>
    /// <returns>The service collection with the memory module added.</returns>
    public static IServiceCollection AddNetMetricMemoryModule(this IServiceCollection services, MemoryModuleOptions options)
    {
        return services.AddNetMetricMemoryModule(_ => options ?? new MemoryModuleOptions());
    }

    /// <summary>
    /// Advanced: Adds the memory module using a factory that generates <see cref="MemoryModuleOptions"/> based on the service provider.
    /// </summary>
    /// <param name="services">The service collection to which the memory module is added.</param>
    /// <param name="optionsFactory">A factory that produces the <see cref="MemoryModuleOptions"/>.</param>
    /// <returns>The service collection with the memory module added.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the optionsFactory is null.</exception>
    public static IServiceCollection AddNetMetricMemoryModule(this IServiceCollection services, Func<IServiceProvider, MemoryModuleOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(optionsFactory);

        // IMPORTANT: We register the produced OPTIONS object as a singleton, not the factory itself.
        services.TryAddSingleton(sp => optionsFactory(sp));

        // MemoryModule is added as a singleton of type IModule (idempotent).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModule, MemoryModule>());

        return services;
    }

    /// <summary>
    /// Shortcut method to add the memory module with the "Light" preset configuration.
    /// </summary>
    /// <param name="services">The service collection to which the memory module is added.</param>
    /// <returns>The service collection with the memory module added.</returns>
    public static IServiceCollection AddNetMetricMemoryLight(this IServiceCollection services)
    {
        return services.AddNetMetricMemoryModule(MemoryModuleOptions.MemoryModulePreset.Light);
    }

    /// <summary>
    /// Shortcut method to add the memory module with the "Verbose" preset configuration.
    /// </summary>
    /// <param name="services">The service collection to which the memory module is added.</param>
    /// <returns>The service collection with the memory module added.</returns>
    public static IServiceCollection AddNetMetricMemoryVerbose(this IServiceCollection services)
    {
        return services.AddNetMetricMemoryModule(MemoryModuleOptions.MemoryModulePreset.Verbose);
    }
}
