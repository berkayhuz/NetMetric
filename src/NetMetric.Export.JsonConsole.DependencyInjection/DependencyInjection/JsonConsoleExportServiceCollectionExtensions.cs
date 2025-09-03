// <copyright file="JsonConsoleExportServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetMetric.Abstractions;
using NetMetric.Export.Exporters;
using NetMetric.Export.JsonConsole.Exporters;

namespace NetMetric.Export.JsonConsole.DependencyInjection;

/// <summary>
/// Provides dependency-injection (DI) extension methods to register the
/// NetMetric JSON console exporter.
/// </summary>
/// <remarks>
/// <para>
/// This registration follows the
/// <see href="https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options">Options pattern</see>
/// and is <em>idempotent</em> with respect to exporter wiring. It ensures that
/// <see cref="MetricOptions.Exporter"/> is populated automatically if the application
/// has not explicitly selected an exporter.
/// </para>
/// <para>
/// If multiple implementations of <see cref="IMetricExporter"/> are registered,
/// a <see cref="CompositeExporter"/> is assigned to <see cref="MetricOptions.Exporter"/>
/// so that all exporters execute in sequence. If none is registered, the value is left
/// unchanged (<see langword="null"/>).
/// </para>
/// </remarks>
/// <example>
/// Minimal hosting example:
/// <code language="csharp"><![CDATA[
/// var builder = WebApplication.CreateBuilder(args);
/// builder.Services
///     .AddNetMetric() // hypothetical root NetMetric registration
///     .AddNetMetricJsonConsoleExporter();
///
/// var app = builder.Build();
/// app.Run();
/// ]]></code>
/// </example>
/// <seealso cref="JsonConsoleExporter"/>
/// <seealso cref="CompositeExporter"/>
/// <seealso cref="MetricOptions"/>
public static class JsonConsoleExportServiceCollectionExtensions
{
    /// <summary>
    /// Registers the JSON console metric exporter and configures <see cref="MetricOptions"/>
    /// to use the registered exporter(s) by default.
    /// </summary>
    /// <param name="services">The DI service collection to add registrations to.</param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that additional calls can be chained.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method performs the following steps:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       Adds options support for <see cref="MetricOptions"/> and installs
    ///       <see cref="OptionsBuilder{TOptions}.PostConfigure{TDep}(Action{TOptions,TDep})"/>
    ///       on the <see cref="OptionsBuilder{TOptions}"/> to resolve the
    ///       <see cref="IEnumerable{T}"/> of <see cref="IMetricExporter"/> and determine
    ///       the effective exporter.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Registers <see cref="JsonConsoleExporter"/> as an <see cref="IMetricExporter"/>
    ///       with <see cref="ServiceLifetime.Singleton"/>.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// If <see cref="MetricOptions.Exporter"/> is already set (e.g., by the application),
    /// this registration respects that selection and does not overwrite it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// Registering the exporter alongside other exporters:
    /// <code language="csharp"><![CDATA[
    /// services
    ///     .AddNetMetric()
    ///     .AddNetMetricJsonConsoleExporter()
    ///     .AddSomeOtherExporter();
    ///
    /// // When multiple IMetricExporter instances are registered,
    /// // the effective MetricOptions.Exporter becomes a CompositeExporter
    /// // that invokes each exporter in sequence.
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricJsonConsoleExporter(
            this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Wire exporters into MetricOptions (idempotent) without an internal post-configure class.
        services.AddOptions<MetricOptions>()
            .PostConfigure<IEnumerable<IMetricExporter>>((options, exporters) =>
            {
                ArgumentNullException.ThrowIfNull(options);

                // Respect an explicit selection.
                if (options.Exporter is not null)
                    return;

                var arr = exporters as IMetricExporter[] ?? exporters?.ToArray() ?? Array.Empty<IMetricExporter>();
                if (arr.Length == 0)
                    return;

                options.Exporter = arr.Length == 1 ? arr[0] : new CompositeExporter(arr);
            });

        // Register the concrete exporter as an IMetricExporter (singleton).
        // TryAddEnumerable prevents duplicate registrations across multiple calls/packages.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IMetricExporter>(_ => new JsonConsoleExporter()));

        return services;
    }
}
