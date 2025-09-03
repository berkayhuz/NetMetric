// <copyright file="ServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMetric.Abstractions;
using NetMetric.Process.Abstractions;
using NetMetric.Process.Configuration;
using NetMetric.Process.Modules;
using ProcessModule = NetMetric.Process.Modules.ProcessModule;

namespace NetMetric.Process.DependencyInjection;

/// <summary>
/// Provides extension methods for registering the NetMetric Process module and related services into the <see cref="IServiceCollection"/>.
/// </summary>
public static class ProcessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default (preset: Default) Process module into the service collection.
    /// This method is idempotent, meaning the module will only be added once.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddNetMetricProcessModule(this IServiceCollection services)
    {
        return services.AddNetMetricProcessModule(_ => new ProcessOptions());
    }

    /// <summary>
    /// Registers the Process module with the specified options into the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
    /// <param name="options">The options to configure the Process module.</param>
    /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddNetMetricProcessModule(this IServiceCollection services, ProcessOptions options)
    {
        return services.AddNetMetricProcessModule(_ => options ?? new ProcessOptions());
    }

    /// <summary>
    /// Registers the Process module with custom configuration provided through an action.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
    /// <param name="configure">An action to configure the <see cref="ProcessOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddNetMetricProcessModule(this IServiceCollection services, Action<ProcessOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return services.AddNetMetricProcessModule(sp =>
        {
            var o = new ProcessOptions();

            configure(o);

            return o;
        });
    }

    /// <summary>
    /// Registers the Process module using an advanced configuration approach where the options are created dynamically through the <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
    /// <param name="optionsFactory">A function to generate the <see cref="ProcessOptions"/> dynamically from the <see cref="IServiceProvider"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddNetMetricProcessModule(
        this IServiceCollection services,
        Func<IServiceProvider, ProcessOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(optionsFactory);

        // Register the options object
        services.TryAddSingleton(sp => optionsFactory(sp));

        // Register the process info provider (single process instance with disposal)
        services.TryAddSingleton<IProcessInfoProvider, DefaultProcessInfoProvider>();

        // Register the module as an IModule
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModule, ProcessModule>());

        return services;
    }
}
