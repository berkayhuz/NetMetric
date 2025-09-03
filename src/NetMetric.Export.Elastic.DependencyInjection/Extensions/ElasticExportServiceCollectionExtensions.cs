// <copyright file="ElasticExportServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetMetric.Abstractions;
using NetMetric.Export.Elastic.Abstractions;
using NetMetric.Export.Elastic.Configurations;
using NetMetric.Export.Elastic.Exporters;
using NetMetric.Export.Elastic.Internal;
using NetMetric.Export.Exporters;

namespace NetMetric.Export.Elastic.DependencyInjection;

/// <summary>
/// Provides extension methods for registering the Elasticsearch metric exporter
/// with the dependency injection (DI) system.
/// </summary>
/// <remarks>
/// <para>
/// This integration wires up a complete, opinionated pipeline for exporting metrics to Elasticsearch
/// using NDJSON bulk ingestion. It is safe to call multiple times; registrations use
/// <see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions"/> “TryAdd*” patterns where appropriate.
/// </para>
/// <list type="bullet">
///   <item><description>Registers and validates <see cref="ElasticExportOptions"/>.</description></item>
///  <item><description>Configures an<see cref="HttpClient"/> with response decompression, request timeout, and certificate revocation checks enabled.</description></item>
///   <item><description>Registers the document mapper (<see cref="IElasticDocumentMapper"/>), the low-level bulk client (<see cref="ElasticBulkClient"/>), and the high-level <see cref="ElasticExporter"/>.</description></item>
///   <item><description>If no exporter is explicitly chosen in <see cref="MetricOptions.Exporter"/>, the Elasticsearch exporter is selected automatically (or combined via <see cref="CompositeExporter"/> when multiple instances exist).</description></item>
/// </list>
/// </remarks>
/// <threadsafety>
/// The registrations produced by these extensions are thread-safe and suitable for concurrent resolution
/// by the Microsoft dependency injection container.
/// </threadsafety>
/// <seealso cref="ElasticExportOptions"/>
/// <seealso cref="ElasticExporter"/>
/// <seealso cref="ElasticBulkClient"/>
public static class ElasticExportServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Elasticsearch exporter and its dependencies into the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add registrations to.</param>
    /// <param name="configure">A delegate to configure <see cref="ElasticExportOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, to allow for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method performs the following:
    /// </para>
    /// <list type="number">
    ///   <item><description>Adds and validates <see cref="ElasticExportOptions"/> using <see cref="OptionsBuilder{TOptions}.Validate(Func{TOptions,bool}, string)"/>.</description></item>
    ///   <item><description>Registers a preconfigured <see cref="HttpClient"/> with <see cref="HttpClientHandler.AutomaticDecompression"/> and <see cref="HttpClientHandler.CheckCertificateRevocationList"/> enabled, and applies the timeout from <see cref="ElasticExportOptions.HttpTimeoutSeconds"/>.</description></item>
    ///   <item><description>Registers <see cref="IElasticDocumentMapper"/> (default <see cref="ElasticDocumentMapper"/>), <see cref="ElasticBulkClient"/>, and <see cref="ElasticExporter"/>.</description></item>
    ///   <item><description>Post-configures <see cref="MetricOptions"/> so that, if no exporter is set, an <see cref="ElasticExporter"/> is selected automatically; multiple matching exporters are combined via <see cref="CompositeExporter"/>.</description></item>
    /// </list>
    /// <para>
    /// <b>Base address note:</b> The <see cref="HttpClient.BaseAddress"/> is intentionally not set. The
    /// <see cref="ElasticBulkClient"/> composes absolute URIs for each bulk request.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// using Microsoft.Extensions.DependencyInjection;
    /// using NetMetric.Export.Elastic.Configurations;
    /// using NetMetric.Export.Elastic.DependencyInjection;
    ///
    /// var services = new ServiceCollection();
    ///
    /// services.AddNetMetricElasticExporter(opt =>
    /// {
    ///     opt.Endpoint = new Uri("https://my-elastic:9200");
    ///     opt.Authorization = "ApiKey <base64-encoded-key>"; // or "Basic ..." etc.
    ///     opt.IndexNamePattern = "metrics-{yyyy.MM.dd}";
    ///     opt.BatchSize = 1000;
    ///     opt.MaxBulkBytes = 5 * 1024 * 1024; // 5 MB
    ///     opt.HttpTimeoutSeconds = 30;
    ///     opt.MaxRetries = 3;
    ///     opt.RetryBaseDelayMs = 200;
    /// });
    ///
    /// var provider = services.BuildServiceProvider();
    /// // Resolve and use IMetricExporter (ElasticExporter will be selected if none was set explicitly)
    /// var exporter = provider.GetRequiredService<IEnumerable<IMetricExporter>>();
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricElasticExporter(
        this IServiceCollection services,
        Action<ElasticExportOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Options & validation
        services.AddOptions<ElasticExportOptions>()
            .PostConfigure(configure)
            .Validate(
                opt =>
                    opt.Endpoint is not null &&
                    opt.BatchSize > 0 &&
                    opt.HttpTimeoutSeconds > 0 &&
                    opt.MaxRetries >= 0 &&
                    opt.RetryBaseDelayMs >= 0 &&
                    opt.MaxBulkBytes >= 0,
                "Invalid ElasticExportOptions");

        // HttpClient with decompression, timeout and CRL check (CA5399)
        services.TryAddSingleton<HttpClient>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<ElasticExportOptions>>().Value;

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                // Ensure certificate revocation checking is enabled
                CheckCertificateRevocationList = true
            };

            var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(opt.HttpTimeoutSeconds)
            };

            // BaseAddress is intentionally not set;
            // ElasticBulkClient constructs absolute URIs itself.
            return http;
        });

        // Mapper, client, exporter
        services.TryAddSingleton<IElasticDocumentMapper, ElasticDocumentMapper>();
        services.TryAddSingleton<ElasticBulkClient>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetricExporter, ElasticExporter>());

        // Wire Elastic exporters into MetricOptions.Exporter (idempotent)
        services.AddOptions<MetricOptions>()
            .PostConfigure<IEnumerable<IMetricExporter>>((options, exporters) =>
            {
                ArgumentNullException.ThrowIfNull(options);
                if (options.Exporter is not null) return;

                // Only wire Elastic exporters
                var arr = (exporters ?? Array.Empty<IMetricExporter>())
                          .Where(e => e is ElasticExporter)
                          .ToArray();

                if (arr.Length == 0) return;

                options.Exporter = arr.Length == 1 ? arr[0] : new CompositeExporter(arr);
            });

        return services;
    }
}
