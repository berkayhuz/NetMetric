// <copyright file="DefaultAttributeMapper.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.OpenTelemetryBridge.Mapper;

/// <summary>
/// Provides the default implementation of the <see cref="IAttributeMapper"/> interface,
/// converting NetMetric tags directly into OpenTelemetry-compatible attributes
/// without applying any transformation or normalization logic.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="DefaultAttributeMapper"/> is designed for straightforward mappings:
/// </para>
/// <list type="bullet">
///   <item><description>Keys are preserved as-is, without renaming or sanitization.</description></item>
///   <item><description>Values are copied as strings; <see langword="null"/> values are replaced with <see cref="string.Empty"/>.</description></item>
/// </list>
/// <para>
/// This mapper is suitable for most basic integration scenarios where tags already
/// conform to OpenTelemetry attribute conventions.
/// </para>
/// <para>
/// Example usage:
/// <code language="csharp">
/// var mapper = new DefaultAttributeMapper();
/// var tags = new Dictionary&lt;string, string?&gt;
/// {
///     ["host"] = "web-01",
///     ["region"] = null
/// };
///
/// IEnumerable&lt;KeyValuePair&lt;string, object&gt;&gt; attributes = mapper.MapTags(tags);
///
/// // Result:
/// // [ "host" → "web-01", "region" → "" ]
/// </code>
/// </para>
/// </remarks>
public sealed class DefaultAttributeMapper : IAttributeMapper
{
    /// <summary>
    /// Maps a dictionary of NetMetric tags into a sequence of OpenTelemetry attributes.
    /// </summary>
    /// <param name="tags">
    /// The input tag dictionary to map.  
    /// If <see langword="null"/>, an empty sequence is returned.
    /// </param>
    /// <returns>
    /// An <see cref="IEnumerable{T}"/> of <see cref="KeyValuePair{TKey, TValue}"/> instances
    /// where:
    /// <list type="bullet">
    ///   <item><description>The key matches the original tag key.</description></item>
    ///   <item><description>The value is the tag value, or <see cref="string.Empty"/> if the tag value was <see langword="null"/>.</description></item>
    /// </list>
    /// </returns>
    /// <example>
    /// Example mapping:
    /// <code language="csharp">
    /// var mapper = new DefaultAttributeMapper();
    /// var tags = new Dictionary&lt;string, string?&gt;
    /// {
    ///     ["service"] = "checkout",
    ///     ["env"] = "production",
    ///     ["zone"] = null
    /// };
    ///
    /// var attributes = mapper.MapTags(tags);
    ///
    /// // Produces:
    /// // [ "service" → "checkout", "env" → "production", "zone" → "" ]
    /// </code>
    /// </example>
    public IEnumerable<KeyValuePair<string, object>> MapTags(
        IReadOnlyDictionary<string, string>? tags)
        => tags is null
            ? Array.Empty<KeyValuePair<string, object>>()
            : tags.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value ?? string.Empty));
}
