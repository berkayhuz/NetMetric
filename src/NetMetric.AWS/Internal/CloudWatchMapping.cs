// <copyright file="CloudWatchMapping.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Amazon.CloudWatch;

namespace NetMetric.AWS.Internal;

/// <summary>
/// Provides helper methods to translate NetMetric tag values into Amazon CloudWatch data types.
/// </summary>
/// <remarks>
/// <para>
/// At present, this type focuses on mapping metric <em>unit</em> tags to
/// <see cref="StandardUnit"/> values. It enables a consistent, centralized translation of
/// user-specified unit tags (for example, <c>"ms"</c>, <c>"bytes"</c>, <c>"percent"</c>) into
/// CloudWatch units.
/// </para>
/// <para>
/// Tags are treated case-insensitively and may use either <c>"unit"</c> or
/// <c>"netmetric.unit"</c> as the tag key. If neither key is present or the value is not
/// recognized, <see cref="StandardUnit.None"/> is returned.
/// </para>
/// <para>
/// <b>Thread safety:</b> All members are pure and stateless, and therefore thread-safe.
/// </para>
/// <para>
/// <b>Performance:</b> Mapping performs a single key lookup (two at most) and a small
/// case-normalization step; it is suitable for hot paths in metric export code.
/// </para>
/// </remarks>
internal static class CloudWatchMapping
{
    /// <summary>
    /// Maps <paramref name="tags"/> to a CloudWatch <see cref="StandardUnit"/>.
    /// </summary>
    /// <param name="tags">
    /// The metric tags dictionary (may be <see langword="null"/>). Keys are evaluated in
    /// case-insensitive manner for <c>"unit"</c> or <c>"netmetric.unit"</c>.
    /// </param>
    /// <returns>
    /// The resolved <see cref="StandardUnit"/> value when a recognized unit is found;
    /// otherwise <see cref="StandardUnit.None"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Recognized values (case-insensitive):
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>"ms"</c>, <c>"millisecond"</c>, <c>"milliseconds"</c> → <see cref="StandardUnit.Milliseconds"/></description></item>
    ///   <item><description><c>"s"</c>, <c>"sec"</c>, <c>"second"</c>, <c>"seconds"</c> → <see cref="StandardUnit.Seconds"/></description></item>
    ///   <item><description><c>"us"</c>, <c>"microsecond"</c>, <c>"microseconds"</c> → <see cref="StandardUnit.Microseconds"/></description></item>
    ///   <item><description><c>"byte"</c>, <c>"bytes"</c> → <see cref="StandardUnit.Bytes"/></description></item>
    ///   <item><description><c>"kb"</c> → <see cref="StandardUnit.Kilobytes"/></description></item>
    ///   <item><description><c>"mb"</c> → <see cref="StandardUnit.Megabytes"/></description></item>
    ///   <item><description><c>"gb"</c> → <see cref="StandardUnit.Gigabytes"/></description></item>
    ///   <item><description><c>"percent"</c>, <c>"%"</c> → <see cref="StandardUnit.Percent"/></description></item>
    ///   <item><description><c>"count"</c> → <see cref="StandardUnit.Count"/></description></item>
    /// </list>
    /// <para>
    /// <b>Nanoseconds:</b> CloudWatch does not support a nanoseconds unit. Values such as
    /// <c>"ns"</c>, <c>"nanosecond"</c>, or <c>"nanoseconds"</c> resolve to <see cref="StandardUnit.None"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// The following example shows how a unit can be supplied via tags and mapped before sending
    /// a metric to CloudWatch:
    /// <code language="csharp">
    /// var tags = new Dictionary&lt;string, string&gt;(StringComparer.OrdinalIgnoreCase)
    /// {
    ///     ["service.name"] = "Checkout",
    ///     ["unit"] = "ms"
    /// };
    ///
    /// // Returns StandardUnit.Milliseconds
    /// var unit = CloudWatchMapping.MapUnitFromTags(tags);
    /// </code>
    /// </example>
    public static StandardUnit MapUnitFromTags(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is null || tags.Count == 0) return StandardUnit.None;

        if (TryGetUnitTag(tags, out var u))
        {
            var x = u!.Trim().ToUpperInvariant();
            return x switch
            {
                "MS" or "MILLISECOND" or "MILLISECONDS" => StandardUnit.Milliseconds,
                "S" or "SEC" or "SECOND" or "SECONDS" => StandardUnit.Seconds,
                "US" or "MICROSECOND" or "MICROSECONDS" => StandardUnit.Microseconds,
                "NS" or "NANOSECOND" or "NANOSECONDS" => StandardUnit.None, // Not supported by CloudWatch
                "BYTE" or "BYTES" => StandardUnit.Bytes,
                "KB" => StandardUnit.Kilobytes,
                "MB" => StandardUnit.Megabytes,
                "GB" => StandardUnit.Gigabytes,
                "PERCENT" or "%" => StandardUnit.Percent,
                "COUNT" => StandardUnit.Count,
                _ => StandardUnit.None
            };
        }

        return StandardUnit.None;
    }

    /// <summary>
    /// Attempts to extract a unit string from <paramref name="tags"/> using the keys
    /// <c>"unit"</c> or <c>"netmetric.unit"</c>.
    /// </summary>
    /// <param name="tags">The metric tag collection to inspect. Must not be <see langword="null"/>.</param>
    /// <param name="unit">
    /// When this method returns, contains the extracted (non-empty, non-whitespace) unit
    /// value if either key is present; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a unit value was found; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="tags"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The lookup is case-insensitive with respect to keys, and does not trim or normalize
    /// the resulting value; callers that need normalized forms should use
    /// <see cref="MapUnitFromTags(IReadOnlyDictionary{string, string})"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var tags = new Dictionary&lt;string, string&gt;(StringComparer.OrdinalIgnoreCase)
    /// {
    ///     ["netmetric.unit"] = "percent"
    /// };
    ///
    /// if (TryGetUnitTag(tags, out var raw))
    /// {
    ///     // raw == "percent"
    /// }
    /// </code>
    /// </example>
    private static bool TryGetUnitTag(IReadOnlyDictionary<string, string> tags, out string? unit)
    {
        ArgumentNullException.ThrowIfNull(tags);

        if (tags.TryGetValue("unit", out unit) && !string.IsNullOrWhiteSpace(unit))
        {
            return true;
        }
        if (tags.TryGetValue("netmetric.unit", out unit) && !string.IsNullOrWhiteSpace(unit))
        {
            return true;
        }

        unit = null;
        return false;
    }
}
