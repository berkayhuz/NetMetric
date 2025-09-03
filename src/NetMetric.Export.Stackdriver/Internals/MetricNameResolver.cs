// <copyright file="MetricNameResolver.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Stackdriver.Internals;

/// <summary>
/// Provides helpers for constructing <em>valid</em> custom metric type names
/// for Google Cloud Monitoring (formerly Stackdriver).
/// </summary>
/// <remarks>
/// <para>
/// Custom metric types in Google Cloud Monitoring must follow the pattern
/// <c>custom.googleapis.com/{prefix}/{id}</c>. This helper enforces a conservative
/// sanitization strategy so produced names are stable and API-compatible.
/// </para>
/// <para>
/// Sanitization rules applied by this class:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>All letters are converted to lowercase using invariant culture.</description>
///   </item>
///   <item>
///     <description>Allowed characters are: ASCII letters, digits, slash (<c>/</c>), underscore (<c>_</c>), and dash (<c>-</c>).</description>
///   </item>
///   <item>
///     <description>Any other character is replaced with underscore (<c>_</c>).</description>
///   </item>
///   <item>
///     <description>Leading and trailing underscores are trimmed.</description>
///   </item>
/// </list>
/// <para>
/// For background on custom metrics, see
/// <see href="https://cloud.google.com/monitoring/custom-metrics">Google Cloud Monitoring: Using custom metrics</see>.
/// </para>
/// </remarks>
internal static class MetricNameResolver
{
    /// <summary>
    /// Builds a fully qualified custom metric type string of the form
    /// <c>custom.googleapis.com/{prefix}/{id}</c>, applying sanitization to both
    /// <paramref name="prefix"/> and <paramref name="id"/>.
    /// </summary>
    /// <param name="prefix">
    /// A logical group name for your metrics (for example, <c>"netmetric"</c>).
    /// This is sanitized to lowercase and restricted to letters, digits, slash, underscore, and dash.
    /// </param>
    /// <param name="id">
    /// The metric identifier (for example, <c>"http_requests_total"</c>).
    /// This is sanitized to lowercase with invalid characters replaced by underscores.
    /// </param>
    /// <returns>
    /// A valid custom metric type string suitable for descriptor creation and time-series writes,
    /// e.g., <c>custom.googleapis.com/netmetric/http_requests_total</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="prefix"/> or <paramref name="id"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// using NetMetric.Export.Stackdriver.Internals;
    ///
    /// // Produces: "custom.googleapis.com/netmetric/http_requests_total"
    /// var metricType = MetricNameResolver.BuildCustomMetricType(
    ///     prefix: "NetMetric",
    ///     id: "Http-Requests.Total");
    /// ]]></code>
    /// </example>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Non-alphanumeric characters are replaced with underscores and casing is normalized:
    /// // Result: "custom.googleapis.com/app/session_start_v2/tenant_123_eu_west_1"
    /// var metricType = MetricNameResolver.BuildCustomMetricType(
    ///     prefix: "App/Session-Start_v2",
    ///     id: "Tenant#123@EU-West-1");
    /// ]]></code>
    /// </example>
    public static string BuildCustomMetricType(string prefix, string id)
    {
        // Example: custom.googleapis.com/netmetric/<id>
        static string Sanitize(string s)
        {
            ArgumentNullException.ThrowIfNull(s);
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) :
                         ch is '/' or '_' or '-' ? ch : '_');
            }
            return sb.ToString().Trim('_');
        }
        return $"custom.googleapis.com/{Sanitize(prefix)}/{Sanitize(id)}";
    }
}
