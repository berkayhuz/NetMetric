// <copyright file="ResourceValidator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Stackdriver.Internals;

/// <summary>
/// Validates that a <see cref="StackdriverExporterOptions"/> instance defines the required labels
/// for known Google Cloud Monitoring (Stackdriver) monitored resource types.
/// </summary>
/// <remarks>
/// <para>
/// Google Cloud Monitoring requires specific label keys to be present on each monitored resource type.
/// This validator checks whether the provided <see cref="StackdriverExporterOptions.ResourceType"/> is
/// recognized and, if so, verifies that all mandatory label keys exist in
/// <see cref="StackdriverExporterOptions.ResourceLabels"/> and that their values are non-empty.
/// </para>
/// <para>
/// Unknown or custom resource types are considered valid by this validator, as their label constraints
/// are not enforced here. This enables forward compatibility with new resource types introduced by GCM.
/// </para>
/// <para>
/// The validation is purely structural—no network calls are performed. It is therefore safe to invoke
/// this method during application startup or option post-configuration.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var options = new StackdriverExporterOptions
/// {
///     ResourceType = "k8s_container",
///     ResourceLabels =
///     {
///         ["project_id"]    = "my-gcp-project",
///         ["location"]      = "europe-west1",
///         ["cluster_name"]  = "prod-cluster",
///         ["namespace_name"]= "payments",
///         ["pod_name"]      = "payments-7b9c5c7d9f-abcde",
///         ["container_name"]= "payments-api"
///     }
/// };
///
/// bool ok = ResourceValidator.IsResourceShapeValid(options);
/// if (!ok)
/// {
///     throw new InvalidOperationException("Stackdriver resource configuration is incomplete.");
/// }
/// ]]></code>
/// </example>
/// <seealso cref="StackdriverExporterOptions"/>
internal static class ResourceValidator
{
    /// <summary>
    /// Minimal required labels for known monitored resource types.
    /// Keys represent <c>monitoredResource.type</c> values; each string array lists the
    /// mandatory label keys for that resource type.
    /// </summary>
    /// <remarks>
    /// This list is a summarized subset of Google Cloud Monitoring monitored resource definitions.
    /// It is not exhaustive; absence of a resource type here causes the validator to permit it.
    /// </remarks>
    private static readonly Dictionary<string, string[]> RequiredLabels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // "global": no mandatory labels (project is derived from context)
            ["gce_instance"] = new[] { "instance_id", "zone" },
            ["aws_ec2_instance"] = new[] { "instance_id", "region", "aws_account" },
            ["k8s_container"] = new[]
            {
                "project_id", "location", "cluster_name", "namespace_name", "pod_name", "container_name"
            },
            ["k8s_pod"] = new[]
            {
                "project_id", "location", "cluster_name", "namespace_name", "pod_name"
            },
            ["k8s_node"] = new[]
            {
                "project_id", "location", "cluster_name", "node_name"
            },
        };

    /// <summary>
    /// Determines whether the monitored resource shape described by <paramref name="opt"/> is valid.
    /// </summary>
    /// <param name="opt">The exporter options containing the monitored resource type and labels.</param>
    /// <returns>
    /// <see langword="true"/> if:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="StackdriverExporterOptions.ResourceType"/> is recognized and all required label keys
    /// exist in <see cref="StackdriverExporterOptions.ResourceLabels"/> with non-empty values.
    /// </description>
    /// </item>
    /// <item><description>The resource type is not recognized (no constraints enforced by this validator).</description></item>
    /// </list>
    /// <see langword="false"/> if the resource type is empty/whitespace or if any mandatory label is missing or empty.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="opt"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// This method does not modify <paramref name="opt"/> and does not perform I/O.
    /// It should be used to fail fast during configuration binding or startup checks.
    /// </remarks>
    public static bool IsResourceShapeValid(StackdriverExporterOptions opt)
    {
        ArgumentNullException.ThrowIfNull(opt);

        if (string.IsNullOrWhiteSpace(opt.ResourceType))
        {
            return false;
        }

        if (!RequiredLabels.TryGetValue(opt.ResourceType, out var required))
        {
            // Unknown resource types are accepted to avoid blocking forward-compat scenarios.
            return true;
        }

        // Ensure all required labels exist and are non-empty.
        foreach (var key in required)
        {
            if (!opt.ResourceLabels.TryGetValue(key, out var val) || string.IsNullOrWhiteSpace(val))
            {
                return false;
            }
        }

        return true;
    }
}
