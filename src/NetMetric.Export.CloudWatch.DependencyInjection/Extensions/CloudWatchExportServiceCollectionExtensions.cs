// <copyright file="CloudWatchExportServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Amazon.CloudWatch;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetMetric.Abstractions;
using NetMetric.Export.CloudWatch.Exporters;
using NetMetric.Export.CloudWatch.Options;
using NetMetric.Export.Exporters;

namespace NetMetric.Export.CloudWatch.DependencyInjection;

/// <summary>
/// Provides extension methods for registering the Amazon CloudWatch metric exporter with
/// the <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// This extension wires up the <see cref="CloudWatchExporter"/> and its dependencies, applies
/// option validation for <see cref="CloudWatchExporterOptions"/>, and post-configures
/// <see cref="MetricOptions"/> to attach either a single exporter or a
/// <see cref="CompositeExporter"/> when multiple <see cref="IMetricExporter"/> instances are
/// available.
/// </para>
/// <para><b>Validation</b></para>
/// <list type="bullet">
///   <item><description><see cref="CloudWatchExporterOptions.Namespace"/> must be a non-empty string.</description></item>
///   <item><description><see cref="CloudWatchExporterOptions.MaxBatchSize"/> must be in the range 1..20 (CloudWatch API constraint).</description></item>
///   <item><description><see cref="CloudWatchExporterOptions.MaxDimensions"/> must be in the range 1..10 (CloudWatch API constraint).</description></item>
///   <item><description><see cref="CloudWatchExporterOptions.StorageResolution"/> must be either 1 or 60 (high/standard resolution).</description></item>
/// </list>
/// <para><b>Thread safety.</b> Registration methods are typically called during application startup and
/// are not thread-safe. Use them during service configuration only.</para>
/// </remarks>
/// <example>
/// The following example demonstrates how to register the CloudWatch exporter with custom options:
/// <code language="csharp"><![CDATA[
/// using Amazon;
/// using Microsoft.Extensions.DependencyInjection;
/// using NetMetric.Export.CloudWatch.DependencyInjection;
/// using NetMetric.Export.CloudWatch.Options;
///
/// var services = new ServiceCollection();
///
/// services.AddNetMetricCloudWatchExporter(o =>
/// {
///     o.Namespace = "NetMetric";
///     o.MaxBatchSize = 20;           // CloudWatch PutMetricData max per request
///     o.MaxDimensions = 10;          // CloudWatch max dimensions per datum
///     o.StorageResolution = 60;      // 1 for high-resolution, 60 for standard
///     o.Region = RegionEndpoint.EUWest1; // Optional; when omitted, AWS SDK default resolution applies
/// });
///
/// var provider = services.BuildServiceProvider();
/// ]]></code>
/// </example>
public static class CloudWatchServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Amazon CloudWatch metric exporter to the <paramref name="services"/> collection,
    /// configures and validates <see cref="CloudWatchExporterOptions"/>, and registers the
    /// required <see cref="IAmazonCloudWatch"/> client.
    /// </summary>
    /// <param name="services">The service collection to add the exporter to.</param>
    /// <param name="configure">An action to configure <see cref="CloudWatchExporterOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method:
    /// </para>
    /// <list type="number">
    ///   <item><description>Registers <see cref="CloudWatchExporterOptions"/> and applies built-in validations.</description></item>
    ///   <item><description>Registers an <see cref="IAmazonCloudWatch"/> singleton. If <see cref="CloudWatchExporterOptions.Region"/> is set,
    ///   it creates the client with that region; otherwise, it uses the AWS SDK default resolution (environment, config files, etc.).</description></item>
    ///   <item><description>Registers <see cref="CloudWatchExporter"/> as an <see cref="IMetricExporter"/> (singleton).</description></item>
    ///   <item><description>Ensures <see cref="MetricOptions"/> is present and post-configured so that if multiple exporters exist,
    ///   they are combined via <see cref="CompositeExporter"/>. If an exporter is already assigned on
    ///   <see cref="MetricOptions.Exporter"/>, the post-configure step does nothing.</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddNetMetricCloudWatchExporter(
        this IServiceCollection services,
        Action<CloudWatchExporterOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<CloudWatchExporterOptions>()
                .Configure(configure)
                .Validate(o => !string.IsNullOrWhiteSpace(o.Namespace), "CloudWatch Namespace is required.")
                .Validate(o => o.MaxBatchSize > 0 && o.MaxBatchSize <= 20, "MaxBatchSize must be 1..20.")
                .Validate(o => o.MaxDimensions > 0 && o.MaxDimensions <= 10, "MaxDimensions must be 1..10.")
                .Validate(o => o.StorageResolution is 1 or 60, "StorageResolution must be 1 or 60.");

        services.TryAddSingleton<IAmazonCloudWatch>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CloudWatchExporterOptions>>().Value;
            return opts.Region is null ? new AmazonCloudWatchClient()
                                       : new AmazonCloudWatchClient(opts.Region);
        });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetricExporter, CloudWatchExporter>());

        // If MetricOptions.Exporter is null, combine exporters with CompositeExporter (idempotent).
        services.AddOptions<MetricOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<MetricOptions>, ExporterPostConfigure>());

        return services;
    }

    /// <summary>
    /// Internal helper that post-configures <see cref="MetricOptions"/> by attaching the available
    /// exporters. If multiple exporters are registered, they are wrapped in a <see cref="CompositeExporter"/>.
    /// </summary>
    /// <remarks>
    /// If <see cref="MetricOptions.Exporter"/> is already set by the application, no changes are made.
    /// </remarks>
    [SuppressMessage("Performance", "CA1812", Justification = "Instantiated by DI container via ServiceDescriptor.")]
    internal sealed class ExporterPostConfigure : IPostConfigureOptions<MetricOptions>
    {
        private readonly IEnumerable<IMetricExporter> _exporters;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExporterPostConfigure"/> class.
        /// </summary>
        /// <param name="exporters">The collection of registered <see cref="IMetricExporter"/> instances.</param>
        /// <remarks>
        /// When <paramref name="exporters"/> is <see langword="null"/>, an empty collection is used to avoid null checks later.
        /// </remarks>
        public ExporterPostConfigure(IEnumerable<IMetricExporter> exporters)
        {
            _exporters = exporters ?? Array.Empty<IMetricExporter>();
        }

        /// <summary>
        /// Sets <see cref="MetricOptions.Exporter"/> if it is not already configured. If multiple
        /// exporters are available, a <see cref="CompositeExporter"/> is created to fan-out exports.
        /// </summary>
        /// <param name="name">The named options instance. Not used.</param>
        /// <param name="options">The <see cref="MetricOptions"/> instance to post-configure.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
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
