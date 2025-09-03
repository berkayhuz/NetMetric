// <copyright file="CpuServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMetric.Abstractions;
using NetMetric.CPU.Configuration;
using NetMetric.CPU.Modules;

namespace NetMetric.CPU.DependencyInjection;

/// <summary>
/// Provides extension methods for adding CPU modules to the <see cref="IServiceCollection"/> in the NetMetric framework.
/// These methods allow users to register various configurations of the CPU module (e.g., default, light, verbose) to the dependency injection container.
/// </summary>
/// <remarks>
/// This class contains several overloads for adding CPU modules with different configurations, such as:
/// - Default configuration
/// - Light configuration (simplified profiling)
/// - Verbose configuration (detailed profiling)
/// Additionally, it supports advanced usage scenarios through a factory function to customize CPU module options.
/// </remarks>
public static class CpuServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default (preset: <see cref="CpuModulePreset.Default"/>) CPU module to the <see cref="IServiceCollection"/>.
    /// This method is idempotent, meaning the same module will not be added more than once.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CPU module will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with the CPU module added.</returns>
    public static IServiceCollection AddNetMetricCpuModule(this IServiceCollection services)
    {
        return services.AddNetMetricCpuModule(CpuModulePreset.Default);
    }

    /// <summary>
    /// Adds a CPU module with the specified preset to the <see cref="IServiceCollection"/>.
    /// This allows for different module configurations, such as Light, Default, or Verbose presets.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CPU module will be added.</param>
    /// <param name="preset">The <see cref="CpuModulePreset"/> that defines the configuration for the CPU module.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with the specified CPU module preset added.</returns>
    public static IServiceCollection AddNetMetricCpuModule(this IServiceCollection services, CpuModulePreset preset)
    {
        return services.AddNetMetricCpuModule(_ => CpuModuleOptions.FromPreset(preset));
    }

    /// <summary>
    /// Adds a CPU module using a specific <see cref="CpuModuleOptions"/> instance to the <see cref="IServiceCollection"/>.
    /// This method allows full control over the configuration of the CPU module.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CPU module will be added.</param>
    /// <param name="options">An instance of <see cref="CpuModuleOptions"/> that defines the configuration for the CPU module.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with the CPU module and specified options added.</returns>
    public static IServiceCollection AddNetMetricCpuModule(this IServiceCollection services, CpuModuleOptions options)
    {
        return services.AddNetMetricCpuModule(_ => options ?? new CpuModuleOptions());
    }

    /// <summary>
    /// Adds a CPU module with advanced configuration by providing an options factory. 
    /// This method allows the <see cref="CpuModuleOptions"/> to be generated dynamically based on the <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CPU module will be added.</param>
    /// <param name="optionsFactory">A factory function that generates a <see cref="CpuModuleOptions"/> instance.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with the CPU module and generated options added.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="optionsFactory"/> is <c>null</c>.</exception>
    public static IServiceCollection AddNetMetricCpuModule(this IServiceCollection services, Func<IServiceProvider, CpuModuleOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(optionsFactory);

        // Registering the options factory as a singleton service to be used later by the CpuModule.
        services.TryAddSingleton(optionsFactory);

        // Add the CpuModule as a singleton service, ensuring it is only added once.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModule, CpuModule>());

        return services;
    }

    /// <summary>
    /// Adds a light profile CPU module to the <see cref="IServiceCollection"/>. 
    /// This is a convenience method that uses the <see cref="CpuModulePreset.Light"/> preset.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CPU module will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with the light profile CPU module added.</returns>
    public static IServiceCollection AddNetMetricCpuLight(this IServiceCollection services)
    {
        return services.AddNetMetricCpuModule(CpuModulePreset.Light);
    }

    /// <summary>
    /// Adds a verbose profile CPU module to the <see cref="IServiceCollection"/>. 
    /// This is a convenience method that uses the <see cref="CpuModulePreset.Verbose"/> preset.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CPU module will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with the verbose profile CPU module added.</returns>
    public static IServiceCollection AddNetMetricCpuVerbose(this IServiceCollection services)
    {
        return services.AddNetMetricCpuModule(CpuModulePreset.Verbose);
    }
}
