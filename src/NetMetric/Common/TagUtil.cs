// <copyright file="TagUtil.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Common;

/// <summary>
/// Provides helper methods for working with metric tags, including merging
/// global, resource, and local tags with well-defined precedence rules.
/// </summary>
/// <remarks>
/// <para>
/// Metric tags (also called dimensions or labels) are often attached to metrics
/// to provide richer context. This utility ensures consistent tag merging across
/// multiple sources:
/// </para>
/// <list type="number">
///   <item><description><b>Global tags</b> (lowest precedence).</description></item>
///   <item><description><b>Resource attributes</b> (middle precedence).</description></item>
///   <item><description><b>Local tags</b> (highest precedence).</description></item>
/// </list>
/// <para>
/// Keys from higher-precedence sources overwrite values from lower-precedence sources.
/// </para>
/// </remarks>
/// <example>
/// Typical usage:
/// <code language="csharp"><![CDATA[
/// var local = new Dictionary<string,string>
/// {
///     ["region"] = "us-east-1",
///     ["env"] = "staging"
/// }.ToFrozenDictionary();
///
/// var opts = new MetricOptions
/// {
///     GlobalTags = new Dictionary<string,string>
///     {
///         ["env"] = "production",
///         ["team"] = "metrics"
///     }.ToFrozenDictionary(),
///     NmResource = new ResourceAttributes
///     {
///         ServiceName = "checkout",
///         ServiceVersion = "1.2.3"
///     }
/// };
///
/// var merged = TagUtil.MergeGlobalTags(local, opts);
/// foreach (var kv in merged)
///     Console.WriteLine($"{kv.Key}={kv.Value}");
///
/// // Output:
/// // team=metrics              (from global)
/// // service.name=checkout     (from resource)
/// // service.version=1.2.3     (from resource)
/// // region=us-east-1          (from local, overrides if exists)
/// // env=staging               (local overrides global)
/// ]]></code>
/// </example>
public static class TagUtil
{
    /// <summary>
    /// Merges global tags, resource attributes, and local tags into a single immutable dictionary.
    /// </summary>
    /// <param name="local">The local tags to merge (highest precedence). Must not be <c>null</c>.</param>
    /// <param name="opts">
    /// Optional metric options that may contain global tags (lowest precedence) and resource attributes (middle precedence).
    /// </param>
    /// <returns>
    /// A <see cref="FrozenDictionary{TKey,TValue}"/> containing merged tags with precedence:
    /// local &gt; resource &gt; global. Returns <see cref="FrozenDictionary{TKey,TValue}.Empty"/>
    /// if no tags are available after merging.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="local"/> is <c>null</c>.</exception>
    /// <remarks>
    /// <para>
    /// - Keys that are <see langword="null"/> or empty are skipped.<br/>
    /// - Values that are <see langword="null"/> are normalized to <see cref="string.Empty"/>.<br/>
    /// - Precedence is enforced by overwriting dictionary entries as each source is applied.<br/>
    /// - The resulting dictionary uses <see cref="StringComparer.Ordinal"/> for key comparison.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var merged = TagUtil.MergeGlobalTags(localTags, metricOptions);
    /// if (merged.TryGetValue("service.name", out var svc))
    ///     Console.WriteLine($"service={svc}");
    /// ]]></code>
    /// </example>
    public static FrozenDictionary<string, string> MergeGlobalTags(
        FrozenDictionary<string, string> local,
        MetricOptions? opts)
    {
        ArgumentNullException.ThrowIfNull(local);

        if ((opts?.GlobalTags is null || opts.GlobalTags.Count == 0) &&
            opts?.NmResource is null)
        {
            return local.Count == 0
                ? FrozenDictionary<string, string>.Empty
                : local;
        }

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        // 1) Global tags (lowest precedence)
        if (opts?.GlobalTags is { Count: > 0 })
        {
            foreach (var kv in opts.GlobalTags)
            {
                if (!string.IsNullOrEmpty(kv.Key))
                {
                    dict[kv.Key] = kv.Value ?? string.Empty;
                }
            }
        }

        // 2) Resource attributes → tags (middle precedence)
        if (opts?.NmResource is ResourceAttributes r)
        {
            if (!string.IsNullOrWhiteSpace(r.ServiceName))
            {
                dict["service.name"] = r.ServiceName!;
            }
            if (!string.IsNullOrWhiteSpace(r.ServiceVersion))
            {
                dict["service.version"] = r.ServiceVersion!;
            }
            if (!string.IsNullOrWhiteSpace(r.DeploymentEnvironment))
            {
                dict["deployment.environment"] = r.DeploymentEnvironment!;
            }
            if (!string.IsNullOrWhiteSpace(r.HostName))
            {
                dict["host.name"] = r.HostName!;
            }

            if (r.Additional is { Count: > 0 })
            {
                foreach (var kv in r.Additional)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                    {
                        dict[kv.Key] = kv.Value ?? string.Empty;
                    }
                }
            }
        }

        // 3) Local tags (highest precedence)
        if (local.Count > 0)
        {
            foreach (var kv in local)
            {
                if (!string.IsNullOrEmpty(kv.Key))
                {
                    dict[kv.Key] = kv.Value ?? string.Empty;
                }
            }
        }

        return dict.Count == 0
            ? FrozenDictionary<string, string>.Empty
            : dict.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
