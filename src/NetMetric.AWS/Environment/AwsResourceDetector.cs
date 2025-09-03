// <copyright file="AwsResourceDetector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AWS.Environment;

/// <summary>
/// Detects the current AWS runtime environment and derives default CloudWatch metric dimensions
/// using environment variables only (no network or metadata service calls).
/// </summary>
/// <remarks>
/// <para>
/// This detector performs a best-effort, heuristic classification by inspecting well-known AWS
/// environment variables that are typically present in <c>AWS Lambda</c>, <c>Amazon ECS</c>,
/// <c>Amazon EKS</c> (Kubernetes), and <c>Amazon EC2</c> environments. If none of the indicators
/// are found, the environment is considered <see cref="AwsRuntime.Local"/>.
/// </para>
/// <para>
/// In addition, the detector attempts to read common service metadata such as service name,
/// version, deployment environment, and host name to populate default metric dimensions.
/// All reads are performed via <see cref="global::System.Environment.GetEnvironmentVariable(string)"/>; no
/// I/O, network calls, or reflection are used in detection logic.
/// </para>
/// <para>
/// The classification is heuristic and non-authoritative. Container images or hosts that override
/// environment variables may lead to misclassification. Prefer passing explicit service metadata
/// via configuration when strong guarantees are required.
/// </para>
/// <threadsafety>
/// This type is immutable after construction and is therefore thread-safe for concurrent reads.
/// Each instance performs environment variable lookups once in the constructor and caches results
/// in read-only properties.
/// </threadsafety>
/// <performance>
/// Environment variable access is O(1) and performed only once per instance construction.
/// <see cref="GetDefaultDimensions"/> allocates a small dictionary (≤ 5 entries).
/// </performance>
/// </remarks>
/// <example>
/// The following example constructs a detector and prints a normalized set of default dimensions:
/// <code language="csharp"><![CDATA[
/// var detector = new AwsResourceDetector();
/// Console.WriteLine($"Runtime: {detector.Runtime}");
/// var dims = detector.GetDefaultDimensions();
/// foreach (var kvp in dims)
/// {
///     Console.WriteLine($"{kvp.Key} = {kvp.Value}");
/// }
/// ]]></code>
/// </example>
/// <example>
/// The following example demonstrates overriding service metadata via environment variables,
/// which the detector will honor in priority order:
/// <code language="csharp"><![CDATA[
/// Environment.SetEnvironmentVariable("AWS_SERVICE_NAME", "CheckoutService");
/// Environment.SetEnvironmentVariable("AWS_SERVICE_VERSION", "1.4.2");
/// Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
///
/// var detector = new AwsResourceDetector();
/// // detector.ServiceName == "CheckoutService"
/// // detector.ServiceVersion == "1.4.2"
/// // detector.DeploymentEnvironment == "Production"
/// ]]></code>
/// </example>
/// <seealso cref="AwsRuntime"/>
public sealed class AwsResourceDetector
{
    /// <summary>
    /// Represents the detected AWS runtime environment.
    /// </summary>
    public enum AwsRuntime
    {
        /// <summary>
        /// The runtime could not be determined from known indicators.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The process is running inside AWS Lambda.
        /// </summary>
        Lambda,

        /// <summary>
        /// The process is running in Amazon Elastic Container Service (ECS).
        /// </summary>
        ECS,

        /// <summary>
        /// The process is running in Amazon Elastic Kubernetes Service (EKS) / Kubernetes.
        /// </summary>
        EKS,

        /// <summary>
        /// The process is running directly on an Amazon EC2 instance (best-effort).
        /// </summary>
        EC2,

        /// <summary>
        /// Not detected as an AWS-managed environment; assumed to be local or other.
        /// </summary>
        Local
    }

    /// <summary>
    /// Gets the detected runtime environment (e.g., <see cref="AwsRuntime.Lambda"/>,
    /// <see cref="AwsRuntime.ECS"/>, <see cref="AwsRuntime.EKS"/>, <see cref="AwsRuntime.EC2"/>,
    /// or <see cref="AwsRuntime.Local"/>).
    /// </summary>
    /// <value>
    /// A value of <see cref="AwsRuntime"/> indicating the environment classification.
    /// </value>
    public AwsRuntime Runtime { get; }

    /// <summary>
    /// Gets the logical service name, if available.
    /// </summary>
    /// <value>
    /// Resolved in order from <c>AWS_SERVICE_NAME</c>, <c>OTEL_SERVICE_NAME</c>, then <c>SERVICE_NAME</c>.
    /// May be <see langword="null"/> if none are defined.
    /// </value>
    public string? ServiceName { get; }

    /// <summary>
    /// Gets the service version, if available.
    /// </summary>
    /// <value>
    /// Resolved in order from <c>AWS_SERVICE_VERSION</c>, <c>OTEL_SERVICE_VERSION</c>, then <c>SERVICE_VERSION</c>.
    /// May be <see langword="null"/>.
    /// </value>
    public string? ServiceVersion { get; }

    /// <summary>
    /// Gets the deployment environment (for example, <c>Development</c>, <c>Staging</c>, <c>Production</c>).
    /// </summary>
    /// <value>
    /// Resolved in order from <c>ASPNETCORE_ENVIRONMENT</c>, <c>DOTNET_ENVIRONMENT</c>, then <c>ENVIRONMENT</c>.
    /// May be <see langword="null"/>.
    /// </value>
    public string? DeploymentEnvironment { get; }

    /// <summary>
    /// Gets the host name associated with the current process.
    /// </summary>
    /// <value>
    /// Resolved from <c>HOSTNAME</c>; falls back to <see cref="global::System.Environment.MachineName"/> if not set.
    /// </value>
    public string? HostName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsResourceDetector"/> class and performs
    /// a one-time environment variable read to populate metadata and determine the runtime.
    /// </summary>
    /// <remarks>
    /// This constructor avoids any network calls and reads only process environment variables.
    /// All property values are captured at construction time; subsequent changes to environment
    /// variables will not be reflected by this instance.
    /// </remarks>
    public AwsResourceDetector()
    {
        // Service metadata
        ServiceName = System.Environment.GetEnvironmentVariable("AWS_SERVICE_NAME")
                      ?? System.Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
                      ?? System.Environment.GetEnvironmentVariable("SERVICE_NAME");

        ServiceVersion = System.Environment.GetEnvironmentVariable("AWS_SERVICE_VERSION")
                         ?? System.Environment.GetEnvironmentVariable("OTEL_SERVICE_VERSION")
                         ?? System.Environment.GetEnvironmentVariable("SERVICE_VERSION");

        DeploymentEnvironment = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                                ?? System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                                ?? System.Environment.GetEnvironmentVariable("ENVIRONMENT");

        HostName = System.Environment.GetEnvironmentVariable("HOSTNAME")
                   ?? System.Environment.MachineName;

        Runtime = DetectRuntime();
    }

    /// <summary>
    /// Builds a standardized set of default dimensions suitable for CloudWatch metrics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned dictionary includes the detected runtime and any non-empty service metadata
    /// (<c>service.name</c>, <c>service.version</c>, <c>deployment.environment</c>, and <c>host.name</c>).
    /// Keys are case-sensitive and use <see cref="StringComparer.Ordinal"/>.
    /// </para>
    /// <para>
    /// Dimension keys produced by this method:
    /// <list type="bullet">
    ///   <item><description><c>aws.runtime</c> — the string form of <see cref="Runtime"/>.</description></item>
    ///   <item><description><c>service.name</c> — when <see cref="ServiceName"/> is non-empty.</description></item>
    ///   <item><description><c>service.version</c> — when <see cref="ServiceVersion"/> is non-empty.</description></item>
    ///   <item><description><c>deployment.environment</c> — when <see cref="DeploymentEnvironment"/> is non-empty.</description></item>
    ///   <item><description><c>host.name</c> — when <see cref="HostName"/> is non-empty.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <returns>
    /// A <see cref="Dictionary{TKey, TValue}"/> of dimension keys and values.
    /// </returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var detector = new AwsResourceDetector();
    /// var dimensions = detector.GetDefaultDimensions();
    /// // Example keys: "aws.runtime", "service.name", "service.version", "deployment.environment", "host.name"
    /// ]]></code>
    /// </example>
    public Dictionary<string, string> GetDefaultDimensions()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["aws.runtime"] = Runtime.ToString()
        };

        if (!string.IsNullOrWhiteSpace(ServiceName)) d["service.name"] = ServiceName!;
        if (!string.IsNullOrWhiteSpace(ServiceVersion)) d["service.version"] = ServiceVersion!;
        if (!string.IsNullOrWhiteSpace(DeploymentEnvironment)) d["deployment.environment"] = DeploymentEnvironment!;
        if (!string.IsNullOrWhiteSpace(HostName)) d["host.name"] = HostName!;

        return d;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the specified environment variable exists and is non-empty.
    /// </summary>
    /// <param name="name">The environment variable name.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="name"/> is defined and not whitespace; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This helper is internal to detection logic and is not intended for public use.
    /// </remarks>
    private static bool Has(string name) =>
        !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable(name));

    /// <summary>
    /// Performs heuristic runtime detection based on well-known AWS environment variables.
    /// </summary>
    /// <returns>
    /// A value of <see cref="AwsRuntime"/> indicating the detected environment.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Indicators (non-exhaustive):
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>AWS_LAMBDA_FUNCTION_NAME</c> or <c>AWS_LAMBDA_RUNTIME_API</c> ⇒ <see cref="AwsRuntime.Lambda"/></description></item>
    ///   <item><description><c>ECS_CONTAINER_METADATA_URI</c> or <c>ECS_CONTAINER_METADATA_URI_V4</c> ⇒ <see cref="AwsRuntime.ECS"/></description></item>
    ///   <item><description><c>KUBERNETES_SERVICE_HOST</c> ⇒ <see cref="AwsRuntime.EKS"/></description></item>
    ///   <item><description><c>AWS_REGION</c> or <c>AWS_DEFAULT_REGION</c> (as a weak signal) ⇒ <see cref="AwsRuntime.EC2"/></description></item>
    /// </list>
    /// If none match, <see cref="AwsRuntime.Local"/> is returned.
    /// </remarks>
    private static AwsRuntime DetectRuntime()
    {
        // Lambda indicators
        if (Has("AWS_LAMBDA_FUNCTION_NAME") || Has("AWS_LAMBDA_RUNTIME_API"))
            return AwsRuntime.Lambda;

        // ECS indicators
        if (Has("ECS_CONTAINER_METADATA_URI") || Has("ECS_CONTAINER_METADATA_URI_V4"))
            return AwsRuntime.ECS;

        // EKS indicators (Kubernetes)
        if (Has("KUBERNETES_SERVICE_HOST"))
            return AwsRuntime.EKS;

        // EC2 assumption: region variables commonly present on instances/user data
        if (Has("AWS_REGION") || Has("AWS_DEFAULT_REGION"))
            return AwsRuntime.EC2;

        return AwsRuntime.Local;
    }
}
