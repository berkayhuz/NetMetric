// <copyright file="DefaultAwsEnvironmentInfo.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AWS.Environment;

/// <summary>
/// Default <see cref="IAwsEnvironmentInfo"/> implementation that derives
/// service identity metadata exclusively from environment variables.
/// </summary>
/// <remarks>
/// <para>
/// This implementation favors AWS-specific variables first, then OpenTelemetry
/// conventions, and finally generic fallbacks. It performs no I/O, reflection,
/// or network calls; all data is read once from process environment variables.
/// </para>
/// <para>
/// The following attributes are resolved when available:
/// <list type="bullet">
///   <item><description><c>service.name</c> (e.g., <c>AWS_SERVICE_NAME</c>, <c>OTEL_SERVICE_NAME</c>)</description></item>
///  <item><description><c>service.version</c> (e.g., <c>AWS_SERVICE_VERSION</c>, <c>OTEL_SERVICE_VERSION</c>)</description></item>
///   <item><description><c>deployment.environment</c> (e.g., <c>ASPNETCORE_ENVIRONMENT</c>)</description></item>
///   <item><description><c>host.name</c> (from <c>HOSTNAME</c> or <see cref="global::System.Environment.MachineName"/>)</description></item>
///   <item><description><c>aws.region</c> (optional; from <c>AWS_REGION</c> or <c>AWS_DEFAULT_REGION</c>)</description></item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// The type can be instantiated directly and used to enrich metric dimensions:
/// <code language="csharp"><![CDATA[
/// var env = new DefaultAwsEnvironmentInfo();
/// var dims = env.GetDefaultDimensions();
/// // Example keys (when available): service.name, service.version, deployment.environment, host.name, aws.region
/// ]]></code>
/// </example>
/// <example>
/// Typical registration in a dependency injection container:
/// <code language="csharp"><![CDATA[
/// services.AddSingleton<IAwsEnvironmentInfo, DefaultAwsEnvironmentInfo>();
/// // Later injected into exporters/services that need default dimensions
/// ]]></code>
/// </example>
public sealed class DefaultAwsEnvironmentInfo : IAwsEnvironmentInfo
{
    /// <summary>
    /// Gets the logical service name if one is available.
    /// </summary>
    /// <value>
    /// Resolved in order from <c>AWS_SERVICE_NAME</c>, <c>OTEL_SERVICE_NAME</c>,
    /// <c>SERVICE_NAME</c>, then <c>APP_NAME</c>; otherwise <see langword="null"/>.
    /// </value>
    public string? ServiceName { get; }

    /// <summary>
    /// Gets the service version if one is available.
    /// </summary>
    /// <value>
    /// Resolved in order from <c>AWS_SERVICE_VERSION</c>, <c>OTEL_SERVICE_VERSION</c>,
    /// <c>SERVICE_VERSION</c>, <c>APP_VERSION</c>, then <c>BUILD_VERSION</c>;
    /// otherwise <see langword="null"/>.
    /// </value>
    public string? ServiceVersion { get; }

    /// <summary>
    /// Gets the deployment environment (for example, <c>Development</c>, <c>Staging</c>, <c>Production</c>).
    /// </summary>
    /// <value>
    /// Resolved in order from <c>ASPNETCORE_ENVIRONMENT</c>, <c>DOTNET_ENVIRONMENT</c>,
    /// then <c>ENVIRONMENT</c>; otherwise <see langword="null"/>.
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
    /// Initializes a new instance of the <see cref="DefaultAwsEnvironmentInfo"/> class
    /// and performs a single read of known environment variables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All values are captured at construction time and are immutable for the lifetime of
    /// the instance. If your application updates environment variables after startup,
    /// create a new instance to observe the changes.
    /// </para>
    /// <para>
    /// The host name is resolved from <c>HOSTNAME</c> when running in containers;
    /// otherwise the OS machine name is used.
    /// </para>
    /// </remarks>
    public DefaultAwsEnvironmentInfo()
    {
        // Service name: prefer AWS-specific, then OTEL, then generic
        ServiceName = GetFirstNonEmpty(
            "AWS_SERVICE_NAME",
            "OTEL_SERVICE_NAME",
            "SERVICE_NAME",
            "APP_NAME");

        // Service version: prefer AWS-specific, then OTEL, then generic
        ServiceVersion = GetFirstNonEmpty(
            "AWS_SERVICE_VERSION",
            "OTEL_SERVICE_VERSION",
            "SERVICE_VERSION",
            "APP_VERSION",
            "BUILD_VERSION");

        // Deployment environment: ASP.NET → DOTNET → generic
        DeploymentEnvironment = GetFirstNonEmpty(
            "ASPNETCORE_ENVIRONMENT",
            "DOTNET_ENVIRONMENT",
            "ENVIRONMENT");

        // Host name: containers often set HOSTNAME
        HostName = System.Environment.GetEnvironmentVariable("HOSTNAME")
                   ?? System.Environment.MachineName;
    }

    /// <summary>
    /// Builds a standardized set of default dimensions suitable for metric backends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned dictionary uses <see cref="StringComparer.Ordinal"/> to preserve
    /// case sensitivity and stable key semantics. Only non-empty values are included.
    /// </para>
    /// <para>
    /// Included keys (when available): <c>service.name</c>, <c>service.version</c>,
    /// <c>deployment.environment</c>, <c>host.name</c>, and <c>aws.region</c>.
    /// Region is optional and is added only if <c>AWS_REGION</c> or
    /// <c>AWS_DEFAULT_REGION</c> is present.
    /// </para>
    /// </remarks>
    /// <returns>
    /// An <see cref="IReadOnlyDictionary{TKey,TValue}"/> that maps dimension keys to values.
    /// </returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var info = new DefaultAwsEnvironmentInfo();
    /// var dims = info.GetDefaultDimensions();
    /// // E.g.: dims["service.name"], dims["deployment.environment"], dims["aws.region"]
    /// ]]></code>
    /// </example>
    public IReadOnlyDictionary<string, string> GetDefaultDimensions()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(ServiceName))
            d["service.name"] = ServiceName!;
        if (!string.IsNullOrWhiteSpace(ServiceVersion))
            d["service.version"] = ServiceVersion!;
        if (!string.IsNullOrWhiteSpace(DeploymentEnvironment))
            d["deployment.environment"] = DeploymentEnvironment!;
        if (!string.IsNullOrWhiteSpace(HostName))
            d["host.name"] = HostName!;

        // Optionally add AWS region if present
        var region = GetFirstNonEmpty("AWS_REGION", "AWS_DEFAULT_REGION");
        if (!string.IsNullOrWhiteSpace(region))
            d["aws.region"] = region!;

        return d;
    }

    /// <summary>
    /// Returns the value of the first non-empty environment variable
    /// from the provided list of candidate names.
    /// </summary>
    /// <param name="names">Candidate environment variable names (case sensitive as provided).</param>
    /// <returns>
    /// The first non-empty environment variable value found, or <see langword="null"/> if none exist.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="names"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method delegates to <see cref="global::System.Environment.GetEnvironmentVariable(string)"/>.
    /// Variable name lookups are platform dependent and typically case-sensitive on Linux/macOS and
    /// case-insensitive on Windows. Provide names accordingly for your target environment.
    /// </para>
    /// <para>
    /// Whitespace-only values are treated as empty.
    /// </para>
    /// </remarks>
    private static string? GetFirstNonEmpty(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        foreach (var n in names)
        {
            var v = System.Environment.GetEnvironmentVariable(n);
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        return null;
    }
}
