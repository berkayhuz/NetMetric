// <copyright file="PrometheusServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetMetric.Abstractions;
using NetMetric.Export.Prometheus.AspNetCore.Services;
using NetMetric.Export.Prometheus.AspNetCore.Util;
using NetMetric.Export.Prometheus.Exporters;
using NetMetric.Export.Prometheus.Options;

namespace NetMetric.Export.Prometheus.DependencyInjection;

/// <summary>
/// Provides extension methods for registering Prometheus scraping and exporting
/// components into an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// These helpers encapsulate the service registrations required to enable
/// NetMetric Prometheus scraping endpoints and/or exporters.
/// They also register shared utilities such as <see cref="IpRateLimiter"/>.
/// </para>
/// <para>
/// The registrations follow common dependency-injection patterns used in ASP.NET Core:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <strong>Options pattern:</strong> <see cref="PrometheusExporterOptions"/> can be configured via the optional
///   <c>configure</c> delegate or through standard configuration bindings (e.g., <c>IConfiguration</c>).
///   </description></item>
///   <item><description>
///   <strong>Idempotency:</strong> <see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.DependencyInjection.ServiceDescriptor)"/>
///   and <see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.DependencyInjection.ServiceDescriptor)"/>
///   are used where appropriate to avoid duplicate core registrations.
///   </description></item>
/// </list>
/// <para>
/// All registered services are safe to consume in concurrent request paths.
/// </para>
/// </remarks>
public static class PrometheusServiceCollectionExtensions
{
    /// <summary>
    /// Adds the services required to support Prometheus scraping endpoints.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    /// An optional delegate to configure <see cref="PrometheusExporterOptions"/>.
    /// If omitted, options may still be configured via configuration binding.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance to enable fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers the following services:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see cref="PrometheusExporterOptions"/> as a singleton value (via <see cref="IOptions{TOptions}"/>).</description></item>
    ///   <item><description><see cref="PrometheusScrapeService"/> as a singleton coordinator for module-driven scrapes.</description></item>
    ///   <item><description><see cref="IpRateLimiter"/> as a singleton reusable utility.</description></item>
    /// </list>
    /// <para>
    /// Use this method when exposing a <c>/metrics</c> HTTP endpoint that will be scraped by Prometheus.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// builder.Services.AddNetMetricPrometheusScraping(options =>
    /// {
    ///     options.EndpointPath = "/metrics";
    ///     options.MaxConcurrentScrapes = 4;
    ///     options.RateLimitWindow = TimeSpan.FromSeconds(1);
    /// });
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricPrometheusScraping(
        this IServiceCollection services,
        Action<PrometheusExporterOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);

        // Expose the concrete options value for consumers that prefer direct injection.
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<PrometheusExporterOptions>>().Value);

        // Core scraping coordination and shared utilities.
        services.TryAddSingleton<PrometheusScrapeService>();
        services.TryAddSingleton<IpRateLimiter>();

        return services;
    }

    /// <summary>
    /// Adds a Prometheus text exporter to the service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="writerFactory">
    /// A factory delegate that creates the <see cref="TextWriter"/> used to emit
    /// metrics in Prometheus text exposition format.
    /// </param>
    /// <param name="configure">
    /// An optional delegate to configure <see cref="PrometheusExporterOptions"/>.
    /// If omitted, options may still be configured via configuration binding.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance to enable fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers the following services:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see cref="PrometheusExporterOptions"/> as a singleton value (via <see cref="IOptions{TOptions}"/>).</description></item>
    ///   <item><description>
    ///   <see cref="IMetricExporter"/> implemented by <see cref="PrometheusTextExporter"/>
    ///   (added with <see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.DependencyInjection.ServiceDescriptor)"/> to allow multiple exporters).
    ///   </description></item>
    ///   <item><description><see cref="IpRateLimiter"/> as a singleton reusable utility.</description></item>
    /// </list>
    /// <para>
    /// The <paramref name="writerFactory"/> is invoked lazily by the exporter to create a fresh
    /// <see cref="TextWriter"/> for each export operation (e.g., once per scrape).
    /// </para>
    /// <para>
    /// Multiple <see cref="IMetricExporter"/> implementations can coexist; this method uses
    /// <see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.DependencyInjection.ServiceDescriptor)"/>
    /// to avoid clobbering other exporters that may already be registered.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="writerFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Register a Prometheus exporter that writes to the HTTP response body.
    /// builder.Services.AddNetMetricPrometheusExporter(
    ///     writerFactory: sp => new StreamWriter(new MemoryStream(), leaveOpen: false),
    ///     configure: options =>
    ///     {
    ///         options.HelpHeader = true;
    ///         options.IncludeTimestamps = false;
    ///     });
    ///
    /// // Multiple exporters can be registered, for example a JSON console exporter elsewhere.
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricPrometheusExporter(
        this IServiceCollection services,
        Func<IServiceProvider, TextWriter> writerFactory,
        Action<PrometheusExporterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(writerFactory);

        if (configure is not null)
            services.Configure(configure);

        // Expose the concrete options value for consumers that prefer direct injection.
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<PrometheusExporterOptions>>().Value);

        // Register the text exporter as an IMetricExporter without preventing other exporters.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetricExporter>(sp =>
            new PrometheusTextExporter(
                () => writerFactory(sp),
                sp.GetRequiredService<PrometheusExporterOptions>())));

        // Shared utility used by scraping/export paths.
        services.TryAddSingleton<IpRateLimiter>();

        return services;
    }
}
