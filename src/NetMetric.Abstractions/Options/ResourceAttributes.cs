// <copyright file="ResourceAttributes.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a standard set of attributes that describe the resource
/// producing metrics, such as the service, environment, and host.
/// <para>
/// Resource attributes provide context for metrics, enabling better
/// filtering, grouping, and correlation across distributed systems.
/// </para>
/// </summary>
public sealed record ResourceAttributes
{
    /// <summary>
    /// Gets the logical name of the service emitting metrics.
    /// <para>
    /// This value is required and uniquely identifies the service within a system.
    /// </para>
    /// </summary>
    public string ServiceName { get; init; }

    /// <summary>
    /// Gets the optional version string of the service.
    /// <para>
    /// Useful for distinguishing metrics across different deployments,
    /// such as <c>"1.2.0"</c> or a Git commit SHA.
    /// </para>
    /// </summary>
    public string? ServiceVersion { get; init; }

    /// <summary>
    /// Gets the optional deployment environment of the service.
    /// <para>
    /// Typical values include <c>"production"</c>, <c>"staging"</c>, or <c>"development"</c>.
    /// </para>
    /// </summary>
    public string? DeploymentEnvironment { get; init; }

    /// <summary>
    /// Gets the optional host name on which the service is running.
    /// <para>
    /// This may represent a machine name, container ID, or node identifier.
    /// </para>
    /// </summary>
    public string? HostName { get; init; }

    /// <summary>
    /// Gets the optional dictionary of additional custom resource attributes.
    /// <para>
    /// These can be used to attach arbitrary key-value pairs such as region, cluster,
    /// or runtime information.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, string>? Additional { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceAttributes"/> record.
    /// </summary>
    /// <param name="serviceName">
    /// The logical name of the service. Must not be null, empty, or whitespace.
    /// </param>
    /// <param name="serviceVersion">
    /// The optional version string of the service.
    /// </param>
    /// <param name="deploymentEnvironment">
    /// The optional deployment environment (e.g., production, staging).
    /// </param>
    /// <param name="hostName">
    /// The optional host name of the machine, container, or node.
    /// </param>
    /// <param name="additional">
    /// An optional dictionary of additional resource attributes.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="serviceName"/> is null, empty, or consists only of whitespace.
    /// </exception>
    public ResourceAttributes(
        string serviceName,
        string? serviceVersion = null,
        string? deploymentEnvironment = null,
        string? hostName = null,
        IReadOnlyDictionary<string, string>? additional = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ServiceName = serviceName;
        ServiceVersion = serviceVersion;
        DeploymentEnvironment = deploymentEnvironment;
        HostName = hostName;
        Additional = additional;
    }
}
