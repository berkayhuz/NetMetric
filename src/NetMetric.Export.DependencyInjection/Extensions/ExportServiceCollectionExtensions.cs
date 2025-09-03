// <copyright file="ExportServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMetric.Abstractions;
using NetMetric.Export.Exporters;

namespace NetMetric.Export.DependencyInjection;

/// <summary>
/// Extension methods for registering metric exporters into the dependency injection container.
/// </summary>
/// <remarks>
/// These helpers allow adding built-in exporters (console, JSON Lines) as well as custom exporters.
/// All registered exporters are wired into <see cref="NetMetric.Abstractions.MetricOptions.Exporter"/> automatically,
/// using <see cref="NetMetric.Export.Exporters.CompositeExporter"/> if multiple exporters are configured.
/// <para>
/// Registration is additive: each call contributes one exporter. On first resolution of
/// <see cref="NetMetric.Abstractions.MetricOptions"/>, the set of registered exporters is captured and assigned
/// to <see cref="NetMetric.Abstractions.MetricOptions.Exporter"/>. If more than one exporter is present,
/// a <see cref="NetMetric.Export.Exporters.CompositeExporter"/> is created to fan out writes to all exporters.
/// </para>
/// </remarks>
/// <threadsafety>
/// The registration methods are typically called during application startup and are thread-safe when used
/// within the standard ASP.NET Core host setup sequence.
/// </threadsafety>
public static class ExportServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="NetMetric.Export.Exporters.JsonLinesExporter"/> which writes metrics
    /// in NDJSON (JSON Lines) format to a file at the specified path.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="filePath">Absolute or relative file path for the NDJSON output file.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="filePath"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// builder.Services
    ///     .AddNetMetricJsonLinesExporter("metrics.ndjson")
    ///     .AddOptions<MetricOptions>()
    ///     .Configure(o => o.Enabled = true);
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricJsonLinesExporter(this IServiceCollection services, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetricExporter>(sp =>
            JsonLinesExporter.ToFile(filePath)));
        WireExportersIntoOptions(services);
        return services;
    }

    /// <summary>
    /// Registers a custom exporter by its implementation type.
    /// </summary>
    /// <typeparam name="TExporter">
    /// The exporter type to register. Must be a class that implements <see cref="NetMetric.Abstractions.IMetricExporter"/>
    /// and expose a public constructor compatible with the DI container.
    /// </typeparam>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <remarks>
    /// The <see cref="DynamicallyAccessedMembersAttribute"/> ensures public constructors of
    /// <typeparamref name="TExporter"/> are preserved under trimming / AOT.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Custom exporter registered by type
    /// builder.Services.AddNetMetricExporter<MyExporter>();
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricExporter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExporter>(
        this IServiceCollection services)
        where TExporter : class, IMetricExporter
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetricExporter, TExporter>());
        WireExportersIntoOptions(services);
        return services;
    }

    /// <summary>
    /// Registers a custom exporter using a factory delegate.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="factory">
    /// A delegate that produces an <see cref="NetMetric.Abstractions.IMetricExporter"/> instance.
    /// The factory may resolve dependencies from the provided <c>IServiceProvider</c>.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// builder.Services.AddNetMetricExporter(sp =>
    /// {
    ///     var httpClient = sp.GetRequiredService<HttpClient>();
    ///     return new MyHttpExporter(httpClient);
    /// });
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricExporter(
        this IServiceCollection services,
        Func<IServiceProvider, IMetricExporter> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetricExporter>(factory));
        WireExportersIntoOptions(services);
        return services;
    }

    /// <summary>
    /// Registers the <see cref="NetMetric.Export.Exporters.ConsoleExporter"/> which writes metrics to the console.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <remarks>
    /// This exporter is suitable for development or diagnostics. For persistent storage or collection by
    /// an external system, consider also registering <see cref="NetMetric.Export.Exporters.JsonLinesExporter"/>
    /// or a custom exporter.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// builder.Services
    ///     .AddNetMetricConsoleExporter()
    ///     .AddOptions<MetricOptions>()
    ///     .Configure(o => o.Enabled = true);
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricConsoleExporter(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetricExporter, ConsoleExporter>());
        WireExportersIntoOptions(services);
        return services;
    }

    /// <summary>
    /// Wires all registered <see cref="NetMetric.Abstractions.IMetricExporter"/> implementations into
    /// <see cref="NetMetric.Abstractions.MetricOptions.Exporter"/>. If multiple exporters are registered,
    /// wraps them in a <see cref="NetMetric.Export.Exporters.CompositeExporter"/>.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <remarks>
    /// <para>
    /// If <see cref="NetMetric.Abstractions.MetricOptions.Exporter"/> has already been set by user code,
    /// this method does not overwrite it. Otherwise:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>No registered exporters → leaves <c>Exporter</c> as <see langword="null"/>.</description>
    /// </item>
    /// <item>
    /// <description>Exactly one exporter → assigns that instance directly to <c>Exporter</c>.</description>
    /// </item>
    /// <item>
    /// <description>
    /// Two or more exporters → assigns a <see cref="NetMetric.Export.Exporters.CompositeExporter"/> that
    /// forwards export calls to each registered exporter in registration order.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    private static void WireExportersIntoOptions(IServiceCollection services)
    {
        services.AddOptions<MetricOptions>()
            .PostConfigure<IEnumerable<IMetricExporter>>((options, exporters) =>
            {
                ArgumentNullException.ThrowIfNull(options);
                if (options.Exporter is not null) return;

                var arr = exporters as IMetricExporter[] ?? exporters?.ToArray() ?? Array.Empty<IMetricExporter>();
                if (arr.Length == 0) return;

                options.Exporter = arr.Length == 1 ? arr[0] : new CompositeExporter(arr);
            });
    }
}
