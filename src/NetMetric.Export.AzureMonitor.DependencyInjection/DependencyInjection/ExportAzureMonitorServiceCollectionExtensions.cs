// <copyright file="ExportAzureMonitorServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Threading.Channels;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetMetric.Abstractions;
using NetMetric.Export.AzureMonitor.Abstractions;
using NetMetric.Export.AzureMonitor.Exporters;
using NetMetric.Export.AzureMonitor.Hosted;
using NetMetric.Export.AzureMonitor.Internal;
using NetMetric.Export.AzureMonitor.Options;
using NetMetric.Export.AzureMonitor.Validation;

namespace NetMetric.Export.AzureMonitor.DependencyInjection;

/// <summary>
/// Extension methods for registering the NetMetric Azure Monitor exporter into an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// This extension wires up the full Azure Monitor export pipeline:
/// </para>
/// <list type="bullet">
///   <item><description>Registers <see cref="AzureMonitorExporterOptions"/> and its validator <see cref="AzureMonitorExporterOptionsValidation"/>.</description></item>
///   <item><description>Registers optional <see cref="MetricOptions"/> (e.g., resource/global tags) used by exporters.</description></item>
///   <item><description>Registers diagnostics via <see cref="AzureMonitorDiagnostics"/> for self-metrics.</description></item>
///   <item><description>Creates a bounded <see cref="AzureMonitorChannel"/> queue according to <see cref="AzureMonitorExporterOptions.QueueFullPolicy"/>.</description></item>
///   <item><description>Configures an <see cref="TelemetryClient"/> using the provided connection string.</description></item>
///   <item><description>Registers the Azure Monitor client (<see cref="IAzureMonitorClient"/>) and the enqueue-only exporter (<see cref="AzureMonitorExporter"/>).</description></item>
///   <item><description>Adds the background sender <see cref="AzureMonitorSender"/> that drains the queue and sends to Azure Monitor.</description></item>
/// </list>
/// <para>
/// All services are registered with sensible lifetimes (singleton for diagnostics, channel, client, and exporter;
/// a hosted service for the sender) to minimize allocations and ensure consistent batching behavior.
/// </para>
/// </remarks>
public static class AzureMonitorServiceCollectionExtensions
{
    /// <summary>
    /// Adds and configures the NetMetric Azure Monitor exporter and its dependencies to the specified service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="configure">An action used to configure <see cref="AzureMonitorExporterOptions"/>.</param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance to support fluent registration.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method expects a valid Azure Monitor/Application Insights connection string to be provided via
    /// <see cref="AzureMonitorExporterOptions.ConnectionString"/>. The bounded channel capacity and full-mode
    /// behavior are derived from <see cref="AzureMonitorExporterOptions.MaxQueueLength"/> and
    /// <see cref="AzureMonitorExporterOptions.QueueFullPolicy"/>, respectively.
    /// </para>
    /// <para>
    /// The registered <see cref="IMetricExporter"/> (<see cref="AzureMonitorExporter"/>) is enqueue-only; actual
    /// transmission occurs in <see cref="AzureMonitorSender"/> which runs as a hosted background service.
    /// </para>
    /// </remarks>
    /// <example>
    /// The following example shows how to register the exporter in a generic host:
    /// <code language="csharp"><![CDATA[
    /// using Microsoft.Extensions.Hosting;
    /// using NetMetric.Export.AzureMonitor.DependencyInjection;
    /// using NetMetric.Export.AzureMonitor.Options;
    ///
    /// Host.CreateDefaultBuilder(args)
    ///     .ConfigureServices(services =>
    ///     {
    ///         services.AddNetMetricAzureMonitorExporter(o =>
    ///         {
    ///             o.ConnectionString = "<Your-ApplicationInsights-ConnectionString>";
    ///             o.MaxQueueLength = 10_000;
    ///             o.QueueFullPolicy = AzureMonitorExporterOptions.DropPolicy.DropOldest; // or DropWrite
    ///             // Additional batching/retry options, if any, can be configured here.
    ///         });
    ///     })
    ///     .Build()
    ///     .Run();
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricAzureMonitorExporter(
        this IServiceCollection services,
        Action<AzureMonitorExporterOptions> configure)
    {
        // Options and validation
        services.AddOptions<AzureMonitorExporterOptions>().Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AzureMonitorExporterOptions>, AzureMonitorExporterOptionsValidation>());

        // Optional: global metric options (e.g., resource tags)
        services.AddOptions<MetricOptions>();

        // Diagnostics singleton
        services.TryAddSingleton<AzureMonitorDiagnostics>();

        // Bounded channel (queue)
        services.TryAddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AzureMonitorExporterOptions>>().Value;
            var diag = sp.GetRequiredService<AzureMonitorDiagnostics>();

            var mode = o.QueueFullPolicy == AzureMonitorExporterOptions.DropPolicy.DropOldest
                ? BoundedChannelFullMode.DropOldest
                : BoundedChannelFullMode.DropWrite;

            return new AzureMonitorChannel(o.MaxQueueLength, mode, diag);
        });

        // TelemetryClient from connection string
        services.TryAddSingleton(sp =>
        {
            var cs = sp.GetRequiredService<IOptions<AzureMonitorExporterOptions>>().Value.ConnectionString!;
            var cfg = new TelemetryConfiguration { ConnectionString = cs };
            return new TelemetryClient(cfg);
        });

        // Client (direct) & exporter (enqueue-only)
        services.TryAddSingleton<IAzureMonitorClient, AzureMonitorTelemetryClient>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetricExporter, AzureMonitorExporter>());

        // Hosted background sender
        services.AddHostedService<AzureMonitorSender>();

        return services;
    }
}
