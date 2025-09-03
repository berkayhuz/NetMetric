// <copyright file="IAttributeMapper.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.OpenTelemetryBridge.Abstractions;

/// <summary>
/// Defines a contract for converting NetMetric tag dictionaries into
/// a sequence of attributes consumable by telemetry pipelines.
/// </summary>
/// <remarks>
/// <para>
/// Implementations can tailor how tag keys and values are projected into
/// attribute pairs. Typical scenarios include:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>Passing tags through unchanged.</description>
///   </item>
///   <item>
///     <description>Filtering, renaming, or prefixing keys to meet organizational conventions.</description>
///   </item>
///   <item>
///     <description>Normalizing values (for example, parsing integers, trimming whitespace, or mapping enums).</description>
///   </item>
/// </list>
/// <para>
/// Keys <em>must</em> remain strings. Values should be simple, immutable .NET types
/// that are cheap to allocate and serialize (e.g., <see cref="string"/>, numeric primitives,
/// or <see cref="bool"/>). Avoid complex objects, mutable collections, or large payloads,
/// as downstream telemetry systems may reject them or incur overhead.
/// </para>
/// <para>
/// Thread-safety: Implementations are expected to be stateless and thread-safe. If mutable
/// state is required (for example, caches), it should be properly synchronized.
/// </para>
/// </remarks>
/// <example>
/// <para>
/// The following example shows a pass-through implementation that returns tags as attributes
/// without modification. Null values are converted to empty strings to ensure downstream compatibility.
/// </para>
/// <code language="csharp"><![CDATA[
/// using System.Collections.Generic;
/// using NetMetric.OpenTelemetryBridge.Abstractions;
/// 
/// public sealed class PassthroughAttributeMapper : IAttributeMapper
/// {
///     public IEnumerable<KeyValuePair<string, object>> MapTags(
///         IReadOnlyDictionary<string, string>? tags)
///     {
///         if (tags is null)
///             yield break;
/// 
///         foreach (var kvp in tags)
///         {
///             var key = kvp.Key ?? string.Empty;
///             var value = kvp.Value is null ? string.Empty : kvp.Value;
///             yield return new KeyValuePair<string, object>(key, value);
///         }
///     }
/// }
/// ]]></code>
/// <para>
/// The next example demonstrates a mapper that filters out internal keys and
/// normalizes a known numeric value:
/// </para>
/// <code language="csharp"><![CDATA[
/// using System;
/// using System.Collections.Generic;
/// using NetMetric.OpenTelemetryBridge.Abstractions;
/// 
/// public sealed class FilteringAttributeMapper : IAttributeMapper
/// {
///     public IEnumerable<KeyValuePair<string, object>> MapTags(
///         IReadOnlyDictionary<string, string>? tags)
///     {
///         if (tags is null)
///             yield break;
/// 
///         foreach (var (key, raw) in tags)
///         {
///             if (string.IsNullOrWhiteSpace(key) || key.StartsWith("_internal.", StringComparison.Ordinal))
///                 continue;
/// 
///             if (string.Equals(key, "http.status_code", StringComparison.OrdinalIgnoreCase)
///                 && int.TryParse(raw, out var status))
///             {
///                 yield return new KeyValuePair<string, object>(key, status);
///                 continue;
///             }
/// 
///             yield return new KeyValuePair<string, object>(key, raw ?? string.Empty);
///         }
///     }
/// }
/// ]]></code>
/// </example>
public interface IAttributeMapper
{
    /// <summary>
    /// Maps a dictionary of NetMetric tags into a sequence of attribute pairs.
    /// </summary>
    /// <param name="tags">
    /// The source tags to map, or <see langword="null"/> if no tags are present.
    /// Keys should be unique and non-empty; values may be <see langword="null"/>,
    /// in which case implementations are encouraged to substitute <see cref="string.Empty"/>.
    /// </param>
    /// <returns>
    /// An ordered sequence of <see cref="KeyValuePair{TKey,TValue}"/> where
    /// <c>TKey</c> is <see cref="string"/> and
    /// <c>TValue</c> is <see cref="object"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Implementations should not mutate the input dictionary. Returning a lazily
    /// evaluated sequence (using <c>yield return</c>) is permitted and often preferred
    /// to minimize allocations.
    /// </para>
    /// <para>
    /// Consumers should treat the returned sequence as immutable and enumerate it at most once.
    /// </para>
    /// </remarks>
    IEnumerable<KeyValuePair<string, object>> MapTags(
        IReadOnlyDictionary<string, string>? tags);
}
