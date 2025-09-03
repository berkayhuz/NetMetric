// <copyright file="ServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMetric.Abstractions;
using NetMetric.OpenTelemetryBridge.Configurations;
using NetMetric.OpenTelemetryBridge.Internal;

namespace NetMetric.OpenTelemetryBridge.DependencyInjection;

/// <summary>
/// Extension methods for registering the NetMetric → OpenTelemetry bridge.
/// </summary>
/// <remarks>
/// <para>
/// These extensions wire an <see cref="IMetricExporter"/> implementation that forwards
/// NetMetric metrics into OpenTelemetry instruments created on a
/// <see cref="System.Diagnostics.Metrics.Meter"/>. This enables applications that already
/// emit NetMetric values to expose the same telemetry to OpenTelemetry-compatible backends
/// (for example, via OpenTelemetry SDK exporters configured elsewhere).
/// </para>
/// <para>
/// The registration uses <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
/// semantics to allow multiple <see cref="IMetricExporter"/> implementations to coexist.
/// Each call adds another exporter instance without replacing existing ones.
/// </para>
/// </remarks>
/// <threadsafety>
/// This static class is thread-safe. The underlying exporter registered by these methods
/// (<see cref="OpenTelemetryMetricExporter"/>) is registered as a singleton and is designed
/// to be used concurrently by the DI container.
/// </threadsafety>
/// <seealso cref="OpenTelemetryBridgeOptions"/>
/// <seealso cref="OpenTelemetryMetricExporter"/>
/// <seealso cref="IMetricExporter"/>
public static class OpenTelemetryBridgeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="OpenTelemetryMetricExporter"/> into the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">
    /// An optional callback to configure <see cref="OpenTelemetryBridgeOptions"/> before registration.
    /// If <see langword="null"/>, default options are used.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance to enable fluent chaining.
    /// </returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     Registers a singleton <see cref="IMetricExporter"/> that bridges NetMetric values
    ///     to OpenTelemetry instruments. The exporter does not configure OpenTelemetry SDK
    ///     pipelines itself; ensure your OpenTelemetry SDK is configured separately.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Multiple invocations will register multiple exporters (via
    ///     <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>),
    ///     allowing composition with other exporters if desired.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     If no configuration is provided, <see cref="OpenTelemetryBridgeOptions"/> defaults are applied.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// using Microsoft.Extensions.DependencyInjection;
    /// using NetMetric.OpenTelemetryBridge.Configurations;
    /// using NetMetric.OpenTelemetryBridge.DependencyInjection;
    ///
    /// var services = new ServiceCollection();
    ///
    /// // Basic registration with default options:
    /// services.AddNetMetricOpenTelemetryBridge();
    ///
    /// // Or customize the bridge options:
    /// services.AddNetMetricOpenTelemetryBridge(opts =>
    /// {
    ///     // Set the OpenTelemetry meter name and version to appear in exported metrics
    ///     opts.MeterName = "NetMetric.Bridge";
    ///     opts.MeterVersion = "1.0.0";
    ///
    ///     // Optional: configure mapping, name sanitization, or gauge series limits, etc.
    ///     // opts.AttributeMapper = new CustomAttributeMapper();
    ///     // opts.GaugeSeriesLimit = 1_000;
    /// });
    ///
    /// // Build the provider as usual:
    /// var provider = services.BuildServiceProvider();
    /// ]]></code>
    /// </example>
    /// <example>
    /// <para>Using the bridge alongside other exporters:</para>
    /// <code language="csharp"><![CDATA[
    /// using Microsoft.Extensions.DependencyInjection;
    /// using NetMetric.Export.Exporters; // Suppose this contains another IMetricExporter
    /// using NetMetric.OpenTelemetryBridge.DependencyInjection;
    ///
    /// var services = new ServiceCollection();
    ///
    /// // Add the OpenTelemetry bridge exporter
    /// services.AddNetMetricOpenTelemetryBridge();
    ///
    /// // Add an additional exporter (e.g., JSON Lines) registered elsewhere as IMetricExporter
    /// services.AddSingleton<IMetricExporter>(new JsonLinesExporter(/* ... */));
    ///
    /// // Both exporters will receive the same NetMetric values
    /// var provider = services.BuildServiceProvider();
    /// ]]></code>
    /// </example>
    /// <returns>
    /// The updated <see cref="IServiceCollection"/>.
    /// </returns>
    public static IServiceCollection AddNetMetricOpenTelemetryBridge(
        this IServiceCollection services,
        Action<OpenTelemetryBridgeOptions>? configure = null)
    {
        var opts = new OpenTelemetryBridgeOptions();
        configure?.Invoke(opts);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetricExporter>(
            _ => new OpenTelemetryMetricExporter(opts)));

        return services;
    }
}
