// <copyright file="EventCountersServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetMetric.Abstractions;
using NetMetric.Export.DependencyInjection;
using NetMetric.Export.EventCounters.Exporters;
using NetMetric.Export.EventCounters.Options;
using NetMetric.Export.Exporters;

namespace NetMetric.Export.EventCounters.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register the
/// NetMetric <c>EventCounters</c> exporter.
/// </summary>
/// <remarks>
/// <para>
/// These helpers wire up the <see cref="EventCountersExporter"/> so that
/// metrics produced by NetMetric are surfaced via .NET
/// <see cref="System.Diagnostics.Tracing.EventCounter"/> instruments and can be observed
/// with tools such as <c>dotnet-counters</c>, <c>dotnet-monitor</c>, PerfView, or Visual Studio.
/// </para>
/// <para>
/// Calling <see cref="AddNetMetricEventCountersExporter(IServiceCollection, System.Action{EventCountersExporterOptions}?)"/>
/// is idempotent with respect to exporter wiring: it registers the exporter as a
/// singleton <see cref="IMetricExporter"/> and ensures exporters are injected into
/// <see cref="MetricOptions"/> via an <see cref="IPostConfigureOptions{TOptions}"/>
/// that sets <see cref="MetricOptions.Exporter"/> to either the single exporter
/// instance or a <see cref="CompositeExporter"/> of all registered exporters
/// when none has been selected yet.
/// </para>
/// <para>
/// This method follows the Options pattern and uses
/// <see cref="OptionsServiceCollectionExtensions.AddOptions(IServiceCollection)"/> and
/// <see cref="OptionsServiceCollectionExtensions.PostConfigure{TOptions}(IServiceCollection, System.Action{TOptions})"/>
/// to apply configuration provided by the caller.
/// </para>
/// </remarks>
/// <example>
/// The following example shows how to enable the EventCounters exporter in a typical host:
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.Hosting;
/// using NetMetric.Export.EventCounters.DependencyInjection;
///
/// var builder = Host.CreateApplicationBuilder(args);
/// builder.Services
///     .AddNetMetric() // hypothetical root NetMetric registration
///     .AddNetMetricEventCountersExporter(options =>
///     {
///         options.IncludeTagsInCounterNames = false;
///         options.MetricNamePrefix = "netmetric";
///     });
///
/// var app = builder.Build();
/// app.Run();
/// ]]></code>
/// </example>
/// <seealso cref="EventCountersExporter"/>
/// <seealso cref="EventCountersExporterOptions"/>
/// <seealso cref="CompositeExporter"/>
public static class EventCountersServiceCollectionExtensions
{
    /// <summary>
    /// Registers the NetMetric exporter that publishes metrics as .NET EventCounters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>Registers <see cref="EventCountersExporter"/> as a concrete exporter and as <see cref="IMetricExporter"/> (singleton).</description>
    ///   </item>
    ///   <item>
    ///     <description>Adds <see cref="EventCountersExporterOptions"/> to the options system and applies the optional <paramref name="configure"/> delegate.</description>
    ///   </item>
    ///   <item>
    ///     <description>Wires exporters into <see cref="MetricOptions"/> so that <see cref="MetricOptions.Exporter"/> is set to the single registered exporter or to a <see cref="CompositeExporter"/> if multiple exporters are present and no exporter has been selected yet.</description>
    ///   </item>
    /// </list>
    /// <para>
    /// Safe to call multiple times; duplicate registrations are avoided via
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>.
    /// </para>
    /// </remarks>
    /// <param name="services">The DI service collection to add the exporter to.</param>
    /// <param name="configure">An optional delegate to configure <see cref="EventCountersExporterOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance to enable chaining.</returns>
    public static IServiceCollection AddNetMetricEventCountersExporter(
        this IServiceCollection services,
        Action<EventCountersExporterOptions>? configure = null)
    {
        services.AddNetMetricExporter<EventCountersExporter>();
        services.AddOptions<EventCountersExporterOptions>();
        if (configure is not null)
            services.PostConfigure(configure);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetricExporter, EventCountersExporter>());

        // Keep identical naming/behavior with existing export packages (idempotent wiring).
        WireExportersIntoOptions(services);

        return services;
    }

    /// <summary>
    /// Ensures that registered <see cref="IMetricExporter"/> instances are injected
    /// into <see cref="MetricOptions"/> after configuration, selecting either the
    /// single exporter or a <see cref="CompositeExporter"/> when multiple exist.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    private static void WireExportersIntoOptions(IServiceCollection services)
    {
        services.AddOptions<MetricOptions>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<MetricOptions>>(sp =>
            {
                // IEnumerable<IMetricExporter> resolves to an empty sequence when none are registered.
                var exporters = sp.GetServices<IMetricExporter>();
                return new ExporterPostConfigure(exporters);
            }));
    }

    /// <summary>
    /// Post-configures <see cref="MetricOptions"/> to assign
    /// <see cref="MetricOptions.Exporter"/> when it has not yet been set.
    /// </summary>
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by the DI container via IOptions post-configure.")]
    private sealed class ExporterPostConfigure : IPostConfigureOptions<MetricOptions>
    {
        private readonly IEnumerable<IMetricExporter> _exporters;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExporterPostConfigure"/> class.
        /// </summary>
        /// <param name="exporters">The set of registered <see cref="IMetricExporter"/> instances.</param>
        public ExporterPostConfigure(IEnumerable<IMetricExporter> exporters)
            => _exporters = exporters ?? Array.Empty<IMetricExporter>();

        /// <summary>
        /// Assigns <see cref="MetricOptions.Exporter"/> if it is currently <see langword="null"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Selection logic:
        /// </para>
        /// <list type="number">
        ///   <item>
        ///     <description>If <paramref name="options"/> already has an <see cref="MetricOptions.Exporter"/>, this method returns without modifying it.</description>
        ///   </item>
        ///   <item>
        ///     <description>If no exporters are registered, this method returns without assigning one.</description>
        ///   </item>
        ///   <item>
        ///     <description>If exactly one exporter is registered, it is assigned directly.</description>
        ///   </item>
        ///   <item>
        ///     <description>If multiple exporters are registered, a <see cref="CompositeExporter"/> is created and assigned.</description>
        ///   </item>
        /// </list>
        /// </remarks>
        /// <param name="name">The named options instance (unused).</param>
        /// <param name="options">The <see cref="MetricOptions"/> instance to post-configure.</param>
        public void PostConfigure(string? name, MetricOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.Exporter is not null)
            {
                return;
            }

            var arr = _exporters as IMetricExporter[] ?? _exporters.ToArray();

            if (arr.Length == 0)
            {
                return;
            }

            options.Exporter = arr.Length == 1 ? arr[0] : new CompositeExporter(arr);
        }
    }
}
