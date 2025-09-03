// <copyright file="InfluxExporterServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetMetric.Abstractions;
using NetMetric.Export.Exporters;
using NetMetric.Export.InfluxDB.Configurations;
using NetMetric.Export.InfluxDB.Exporters;
using NetMetric.Export.InfluxDB.Validations;

namespace NetMetric.Export.InfluxDB.DependencyInjection;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register the
/// NetMetric InfluxDB exporter.
/// </summary>
/// <remarks>
/// <para>
/// This registration is <em>idempotent</em> with respect to exporter wiring and follows
/// the <see href="https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options">Options pattern</see>.
/// It ensures that multiple exporters can coexist and that <see cref="MetricOptions.Exporter"/>
/// is set to either a single exporter or a <see cref="CompositeExporter"/> if multiple exporters
/// are present and no explicit exporter has been selected.
/// </para>
/// <para>
/// The extension configures a named <see cref="HttpClient"/> (<c>"NetMetric.InfluxDB"</c>) using
/// the base address from <see cref="InfluxExporterOptions"/>. The <c>Authorization</c> header is applied
/// by <see cref="InfluxLineProtocolExporter"/> per request, allowing for token rotation or per-request customization.
/// </para>
/// <para>
/// Validation for <see cref="InfluxExporterOptions"/> is added via
/// <see cref="InfluxExporterOptionsValidation"/> to fail fast at startup when mandatory values
/// (e.g., <see cref="InfluxExporterOptions.BaseAddress"/>, <see cref="InfluxExporterOptions.Org"/>,
/// <see cref="InfluxExporterOptions.Bucket"/>, <see cref="InfluxExporterOptions.Token"/>) are missing.
/// </para>
/// </remarks>
/// <example>
/// Example usage in a host builder:
/// <code language="csharp"><![CDATA[
/// var builder = Host.CreateApplicationBuilder(args);
/// builder.Services.AddNetMetricInfluxExporter(options =>
/// {
///     options.BaseAddress = new Uri("https://influx.example.com");
///     options.Org = "my-org";
///     options.Bucket = "metrics";
///     options.Token = "my-secret-token";
///     // options.WritePrecision = WritePrecision.Ns; // if applicable
/// });
/// ]]></code>
/// </example>
/// <seealso cref="InfluxExporterOptions"/>
/// <seealso cref="InfluxLineProtocolExporter"/>
/// <seealso cref="CompositeExporter"/>
public static class InfluxExporterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the NetMetric exporter that publishes metrics to InfluxDB using the line protocol and the v2 write API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method performs the following steps:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       Adds <see cref="InfluxExporterOptions"/> to the options system and applies the provided
    ///       <paramref name="configure"/> delegate.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Configures a named <see cref="HttpClient"/> (<c>"NetMetric.InfluxDB"</c>) whose
    ///       <see cref="System.Net.Http.HttpClient.BaseAddress"/> is taken from
    ///       <see cref="InfluxExporterOptions.BaseAddress"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Registers <see cref="InfluxLineProtocolExporter"/> as a singleton implementation of
    ///       <see cref="IMetricExporter"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Validates <see cref="InfluxExporterOptions"/> via <see cref="InfluxExporterOptionsValidation"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Wires exporters into <see cref="MetricOptions"/> in an idempotent manner, assigning
    ///       <see cref="MetricOptions.Exporter"/> to the single registered exporter, or to a newly created
    ///       <see cref="CompositeExporter"/> when multiple exporters are present and no explicit exporter has been selected.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// The registration avoids common duplication issues by using
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>.
    /// </para>
    /// </remarks>
    /// <param name="services">The dependency injection service collection to augment.</param>
    /// <param name="configure">A delegate used to configure <see cref="InfluxExporterOptions"/>.</param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that additional calls can be chained.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// services.AddNetMetricInfluxExporter(o =>
    /// {
    ///     o.BaseAddress = new Uri("https://influxdb.local:8086");
    ///     o.Org = "acme";
    ///     o.Bucket = "prod-metrics";
    ///     o.Token = Environment.GetEnvironmentVariable("INFLUX_TOKEN");
    /// });
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricInfluxExporter(
        this IServiceCollection services,
        Action<InfluxExporterOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // 1) Influx options + user configuration
        services.AddOptions<InfluxExporterOptions>()
                .Configure(configure);

        // 2) Named HttpClient for the exporter (Authorization handled by the exporter per request)
        services.AddHttpClient("NetMetric.InfluxDB", static (sp, client) =>
        {
            var o = sp.GetRequiredService<IOptions<InfluxExporterOptions>>().Value;
            client.BaseAddress = o.BaseAddress;
        });

        // 3) Register exporter as a singleton IMetricExporter
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IMetricExporter, InfluxLineProtocolExporter>());

        // 4) Validate Influx options at startup
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<InfluxExporterOptions>,
            InfluxExporterOptionsValidation>());

        // 5) Wire exporters into MetricOptions (idempotent) without an extra internal class
        //    This avoids CA1812 (no uninstantiated internal class) while preserving behavior.
        services.AddOptions<MetricOptions>()
            .PostConfigure<IEnumerable<IMetricExporter>>(static (options, exporters) =>
            {
                ArgumentNullException.ThrowIfNull(options);

                // Respect an explicitly selected exporter.
                if (options.Exporter is not null)
                    return;

                var arr = exporters as IMetricExporter[] ?? exporters?.ToArray() ?? Array.Empty<IMetricExporter>();
                if (arr.Length == 0)
                    return;

                options.Exporter = arr.Length == 1 ? arr[0] : new CompositeExporter(arr);
            });

        return services;
    }
}
