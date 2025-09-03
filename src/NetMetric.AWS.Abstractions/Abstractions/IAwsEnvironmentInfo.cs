// <copyright file="IAwsEnvironmentInfo.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AWS.Abstractions;

/// <summary>
/// Defines a contract for obtaining AWS runtime and service metadata
/// (for example, ECS, EKS, Lambda, EC2, or local) together with a stable set
/// of default metric dimensions suitable for CloudWatch publications.
/// </summary>
/// <remarks>
/// <para>
/// Implementations typically read environment variables and/or lightweight runtime
/// indicators to provide consistent metric dimensions such as
/// <c>service.name</c>, <c>service.version</c>, <c>deployment.environment</c>, and <c>host.name</c>.
/// No network calls are required by this interface.
/// </para>
/// <para><b>Related types (no hard dependency from Abstractions):</b><br/>
/// <c>DefaultAwsEnvironmentInfo</c>, <c>AwsResourceDetector</c>, <c>CloudWatchMetricExporter</c>.
/// </para>
/// <para><b>Thread safety</b></para>
/// Implementations should be safe for concurrent access from multiple threads.
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Register a default implementation and enable MergeDefaultDimensions on the exporter.
/// using Microsoft.Extensions.DependencyInjection;
/// using NetMetric.AWS.Abstractions;
/// using NetMetric.AWS.Environment;
/// using NetMetric.AWS.Exporters;
/// using NetMetric.AWS.Options;
/// using Amazon.CloudWatch;
///
/// var services = new ServiceCollection();
/// services.AddSingleton<IAwsEnvironmentInfo, DefaultAwsEnvironmentInfo>();
/// services.AddSingleton<IAmazonCloudWatch, AmazonCloudWatchClient>();
/// services.AddSingleton(new CloudWatchExporterOptions
/// {
///     Namespace = "MyCompany.MyService",
///     MergeDefaultDimensions = true
/// });
///
/// var sp = services.BuildServiceProvider();
/// var env = sp.GetRequiredService<IAwsEnvironmentInfo>();
/// var cw = sp.GetRequiredService<IAmazonCloudWatch>();
/// var opts = sp.GetRequiredService<CloudWatchExporterOptions>();
///
/// // Example: constructing the exporter with environment defaults merged into metric dimensions
/// var exporter = new CloudWatchMetricExporter(cw, opts, env);
/// ]]></code>
/// </example>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Reading default dimensions for diagnostic purposes
/// IAwsEnvironmentInfo env = new DefaultAwsEnvironmentInfo();
/// var dims = env.GetDefaultDimensions();
/// foreach (var kv in dims)
/// {
///     Console.WriteLine($"{kv.Key} = {kv.Value}");
/// }
/// // Possible output:
/// // service.name = OrdersApi
/// // service.version = 1.2.3
/// // deployment.environment = Production
/// // host.name = ip-10-0-0-12
/// // aws.region = eu-central-1 (if present)
/// ]]></code>
/// </example>
public interface IAwsEnvironmentInfo
{
    /// <summary>
    /// Gets the logical service name running in the current environment.
    /// </summary>
    string? ServiceName { get; }

    /// <summary>
    /// Gets the version of the running service.
    /// </summary>
    string? ServiceVersion { get; }

    /// <summary>
    /// Gets the deployment environment.
    /// </summary>
    string? DeploymentEnvironment { get; }

    /// <summary>
    /// Gets the host name of the instance or container.
    /// </summary>
    string? HostName { get; }

    /// <summary>
    /// Builds a standardized, read-only set of default CloudWatch metric dimensions.
    /// </summary>
    /// <remarks>
    /// Common keys include: <c>service.name</c>, <c>service.version</c>,
    /// <c>deployment.environment</c>, <c>host.name</c>, and optionally <c>aws.region</c>.
    /// Implementations should omit keys whose values are null or whitespace.
    /// </remarks>
    IReadOnlyDictionary<string, string> GetDefaultDimensions();
}
