// <copyright file="ServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMetric.IIS.Extensions;

/// <summary>
/// Provides extension methods for registering IIS-related metrics
/// into the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// These extensions wire up the core IIS metric set and, optionally, a background
/// <see cref="IHostedService"/> that listens to IIS/ASP.NET Core Module events and
/// updates the metrics in near real time.
/// </para>
/// <para>
/// The registration is lightweight: instruments in <see cref="IisMetricSet"/> are created
/// lazily on first use via the injected <see cref="IMetricFactory"/>.
/// </para>
/// <para><strong>Thread safety:</strong> The registration is safe to call during application startup
/// and the resulting services are safe for concurrent use by the host.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IIS in-process metrics collection components into the service collection.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add the IIS metric services to.
    /// </param>
    /// <param name="enabledByDefault">
    /// If <see langword="true"/>, both the <see cref="IisMetricSet"/> and
    /// the <see cref="IisMetricsHostedService"/> are registered, enabling
    /// event-driven metrics collection immediately.  
    /// If <see langword="false"/>, only the metric set is registered and
    /// no background listener is started automatically.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that additional calls can be chained.
    /// </returns>
    /// <remarks>
    /// <para>
    /// When <paramref name="enabledByDefault"/> is <see langword="true"/>, the hosted service will:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Activate only on Windows runtimes.</description></item>
    ///   <item><description>Respect the <c>NETMETRIC_IIS_ENABLED</c> environment variable (values
    ///   <c>"0"</c>, <c>"false"</c>, or <c>"False"</c> disable the listener).</description></item>
    ///   <item><description>Translate known IIS/ANCM events into calls on <see cref="IisMetricSet"/>.</description></item>
    /// </list>
    /// <para>
    /// The extension requires a configured <see cref="IMetricFactory"/> in the container; if one is not
    /// already present, ensure the NetMetric core services are registered (e.g., via your project's
    /// main NetMetric DI extension) before calling this method.
    /// </para>
    /// </remarks>
    /// <example>
    /// The typical registration in a hosted application:
    /// <code language="csharp"><![CDATA[
    /// using Microsoft.Extensions.Hosting;
    /// using NetMetric.IIS.Extensions;
    ///
    /// var builder = Host.CreateApplicationBuilder(args);
    ///
    /// // Ensure NetMetric core is registered somewhere in your composition root:
    /// // builder.Services.AddNetMetricCore(...);
    ///
    /// // Register IIS metrics and start the background listener:
    /// builder.Services.AddNetMetricIisInProcess(enabledByDefault: true);
    ///
    /// var app = builder.Build();
    /// await app.RunAsync();
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricIisInProcess(this IServiceCollection services, bool enabledByDefault = true)
    {
        services.AddSingleton(sp => new IisMetricSet(sp.GetRequiredService<IMetricFactory>()));

        if (enabledByDefault)
        {
            services.AddSingleton<IHostedService, IisMetricsHostedService>();
        }

        return services;
    }
}
