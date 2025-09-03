// <copyright file="AwsServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Amazon;
using Amazon.CloudWatch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace NetMetric.AWS.DependencyInjection;

/// <summary>
/// Provides Dependency Injection (DI) extension methods to register NetMetric's
/// Amazon CloudWatch integration end-to-end.
/// </summary>
/// <remarks>
/// <para>
/// This type wires up the following building blocks:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="CloudWatchExporterOptions"/> registration via either a delegate or an <see cref="IConfiguration"/> section.</description></item>
///   <item><description>AWS SDK client (<see cref="IAmazonCloudWatch"/>) creation with region autodetection from <c>AWS_REGION</c> / <c>AWS_DEFAULT_REGION</c>.</description></item>
///   <item><description>Option validation via <see cref="IValidateOptions{TOptions}"/> (<see cref="CloudWatchExporterOptionsValidator"/>).</description></item>
///   <item><description>Exporter composition: <see cref="CloudWatchMetricExporter"/> (direct) and optional <see cref="CloudWatchBufferedExporter"/> (buffered).</description></item>
///   <item><description>Hosted background flush service <see cref="CloudWatchFlushService"/> that periodically ships metrics to CloudWatch.</description></item>
/// </list>
/// <para>
/// The configuration-section overload performs a minimal manual mapping instead of <c>ConfigurationBinder.Bind</c>
/// to remain trimming/AOT-friendly. Only known keys are mapped; unknown keys are ignored.
/// </para>
/// <para><b>Thread safety.</b> Registration is intended to be called during application startup and is thread-safe when used in standard ASP.NET Core hosting.</para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// 1) Delegate-based configuration
///    Program.cs / Startup.cs
/// services.AddNetMetricAwsCloudWatch(opts =>
/// {
///     opts.Namespace = "MyApp/Prod";
///     opts.EnableBuffering = true;          // use background channel + flush service
///     opts.MergeDefaultDimensions = true;   // merge service.name, host.name, etc.
/// });
/// 
/// 2) Configuration section-based configuration (AOT/trim-safe)
/// IConfigurationSection section = configuration.GetSection("CloudWatchExporter");
/// services.AddNetMetricAwsCloudWatch(section);
/// </code>
/// <code language="json">
/// appsettings.json
/// {
///     "CloudWatchExporter": {
///         "Namespace": "MyApp/Prod",
///     "EnableBuffering": true,
///     "MaxBatchSize": 20,
///     "FlushIntervalMs": 2000,
///     "DimensionTagKeys": ["service.name", "service.version", "deployment.environment", "host.name", "aws.region"],
///     "BlockedDimensionKeyPatterns": ["^user\\.", ".*id$"]
///     }
/// }
/// </code>
/// </example>
public static class AwsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the CloudWatch integration using a configuration delegate.
    /// </summary>
    /// <param name="services">The target <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">A delegate that configures <see cref="CloudWatchExporterOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Use this overload when you want to set options programmatically. For configuration files,
    /// prefer the <see cref="AddNetMetricAwsCloudWatch(IServiceCollection, IConfiguration)"/> overload.
    /// </remarks>
    public static IServiceCollection AddNetMetricAwsCloudWatch(
        this IServiceCollection services,
        Action<CloudWatchExporterOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<CloudWatchExporterOptions>().Configure(configure);
        RegisterCommon(services);
        return services;
    }

    /// <summary>
    /// Registers the CloudWatch integration from an <see cref="IConfiguration"/> section.
    /// </summary>
    /// <param name="services">The target <see cref="IServiceCollection"/>.</param>
    /// <param name="section">The configuration section that holds <see cref="CloudWatchExporterOptions"/> values.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="section"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This overload performs a minimal manual mapping rather than using <c>ConfigurationBinder.Bind</c>,
    /// which keeps it friendly to trimming and AOT compilation scenarios.
    /// </para>
    /// <para>
    /// Only known option keys are mapped. Unknown keys are ignored by design.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddNetMetricAwsCloudWatch(
        this IServiceCollection services,
        IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.AddOptions<CloudWatchExporterOptions>()
            .Configure(options => ApplyConfiguration(options, section));

        RegisterCommon(services);
        return services;
    }

    /// <summary>
    /// Applies a minimal, AOT/trim-friendly mapping from the provided configuration section
    /// to the supplied <see cref="CloudWatchExporterOptions"/> instance.
    /// </summary>
    /// <param name="opts">The destination <see cref="CloudWatchExporterOptions"/> instance.</param>
    /// <param name="c">The source <see cref="IConfiguration"/> section.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="opts"/> or <paramref name="c"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// This method intentionally avoids <c>ConfigurationBinder</c> to eliminate reflection usage and
    /// preserve linker-friendliness. Numeric/boolean parsing failures are ignored and leave defaults intact.
    /// </remarks>
    private static void ApplyConfiguration(CloudWatchExporterOptions opts, IConfiguration c)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(c);

        // strings
        opts.Namespace = c["Namespace"] ?? opts.Namespace;

        // booleans
        if (bool.TryParse(c["DropEmptyDimensions"], out var b)) opts.DropEmptyDimensions = b;
        if (bool.TryParse(c["UseDotsCase"], out b)) opts.UseDotsCase = b;
        if (bool.TryParse(c["UseStatisticSetForDistributions"], out b)) opts.UseStatisticSetForDistributions = b;
        if (bool.TryParse(c["MergeDefaultDimensions"], out b)) opts.MergeDefaultDimensions = b;
        if (bool.TryParse(c["EnableBuffering"], out b)) opts.EnableBuffering = b;
        if (bool.TryParse(c["DropOnlyOverflowingKey"], out b)) opts.DropOnlyOverflowingKey = b;

        // integers
        if (int.TryParse(c["MaxBatchSize"], out var i)) opts.MaxBatchSize = i;
        if (int.TryParse(c["TimeoutMs"], out i)) opts.TimeoutMs = i;
        if (int.TryParse(c["MaxRetries"], out i)) opts.MaxRetries = i;
        if (int.TryParse(c["BaseDelayMs"], out i)) opts.BaseDelayMs = i;
        if (int.TryParse(c["BufferCapacity"], out i)) opts.BufferCapacity = i;
        if (int.TryParse(c["FlushIntervalMs"], out i)) opts.FlushIntervalMs = i;
        if (int.TryParse(c["MaxFlushBatch"], out i)) opts.MaxFlushBatch = i;
        if (int.TryParse(c["MaxDimensionValueLength"], out i)) opts.MaxDimensionValueLength = i;
        if (int.TryParse(c["MaxUniqueValuesPerKey"], out i)) opts.MaxUniqueValuesPerKey = i;

        // lists
        var dimKeys = c.GetSection("DimensionTagKeys")
            .GetChildren()
            .Select(s => s.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))!;
        if (dimKeys.Any()) opts.SetDimensionTagKeys(dimKeys!);

        var blocked = c.GetSection("BlockedDimensionKeyPatterns")
            .GetChildren()
            .Select(s => s.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))!;
        if (blocked.Any()) opts.SetBlockedDimensionKeyPatterns(blocked!);
    }

    /// <summary>
    /// Registers common building blocks needed by the CloudWatch integration:
    /// <list type="bullet">
    /// <item><description>Default AWS environment info (<see cref="IAwsEnvironmentInfo"/> → <see cref="DefaultAwsEnvironmentInfo"/>).</description></item>
    /// <item><description><see cref="IAmazonCloudWatch"/> client with region autodetection.</description></item>
    /// <item><description><see cref="CloudWatchExporterOptionsValidator"/> for option validation.</description></item>
    /// <item><description>Concrete exporters: <see cref="CloudWatchMetricExporter"/> and optional <see cref="CloudWatchBufferedExporter"/>.</description></item>
    /// <item><description>Hosted background flush service <see cref="CloudWatchFlushService"/>.</description></item>
    /// </list>
    /// </summary>
    /// <param name="services">The target <see cref="IServiceCollection"/>.</param>
    /// <remarks>
    /// <para>
    /// The hosted service is always added. If buffering is disabled via options,
    /// the service will remain effectively idle.
    /// </para>
    /// <para>
    /// The <see cref="MetricOptions"/> are configured so that <see cref="MetricOptions.Exporter"/> points to the
    /// buffered exporter when <see cref="CloudWatchExporterOptions.EnableBuffering"/> is <see langword="true"/>,
    /// otherwise to the direct <see cref="CloudWatchMetricExporter"/>.
    /// </para>
    /// </remarks>
    private static void RegisterCommon(IServiceCollection services)
    {
        services.TryAddSingleton<IAwsEnvironmentInfo, DefaultAwsEnvironmentInfo>();

        services.TryAddSingleton<IAmazonCloudWatch>(_ =>
        {
            var regionName = System.Environment.GetEnvironmentVariable("AWS_REGION")
                          ?? System.Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");

            return string.IsNullOrWhiteSpace(regionName)
                ? new AmazonCloudWatchClient()
                : new AmazonCloudWatchClient(RegionEndpoint.GetBySystemName(regionName));
        });

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<CloudWatchExporterOptions>, CloudWatchExporterOptionsValidator>());

        services.TryAddSingleton(sp =>
        {
            var cw = sp.GetRequiredService<IAmazonCloudWatch>();
            var opts = sp.GetRequiredService<IOptions<CloudWatchExporterOptions>>().Value;
            var env = sp.GetRequiredService<IAwsEnvironmentInfo>();
            return new CloudWatchMetricExporter(cw, opts, env);
        });

        services.TryAddSingleton<CloudWatchBufferedExporter>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CloudWatchExporterOptions>>().Value;
            return new CloudWatchBufferedExporter(opts);
        });

        services.AddOptions<MetricOptions>().Configure<CloudWatchBufferedExporter, CloudWatchMetricExporter, IOptions<CloudWatchExporterOptions>>(
        (opts, buffered, inner, cwOpts) =>
        {
            opts.Exporter = cwOpts.Value.EnableBuffering ? buffered : inner;
        });

        // Always register the hosted service; behavior is governed by options.
        services.AddHostedService<CloudWatchFlushService>();
    }
}
