// <copyright file="TagSanitizer.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Common;

/// <summary>
/// Provides utilities for sanitizing metric tag dictionaries by applying
/// constraints on key length, value length, and total tag count.
/// </summary>
/// <remarks>
/// <para>
/// Tags (also called dimensions or labels) are commonly attached to metrics
/// to provide additional context (e.g., <c>region=us-east</c>, <c>env=prod</c>).
/// This utility ensures tag sets remain bounded to prevent exporter or
/// downstream system issues caused by unbounded keys/values or cardinality explosion.
/// </para>
/// <para>
/// The method preserves insertion order of the input sequence when trimming,
/// and uses <see cref="StringComparer.Ordinal"/> for key comparison.
/// </para>
/// </remarks>
/// <example>
/// Basic usage:
/// <code language="csharp"><![CDATA[
/// var original = new Dictionary<string,string>
/// {
///     ["environment"] = "production",
///     ["region"]      = "us-east-1",
///     ["host"]        = new string('a', 200) // too long
/// }.ToFrozenDictionary();
///
/// // Enforce: max key len=10, max value len=20, max tags=2
/// var sanitized = TagSanitizer.Sanitize(original, 10, 20, 2);
///
/// foreach (var kvp in sanitized)
///     Console.WriteLine($"{kvp.Key}={kvp.Value}");
///
/// // Output might look like:
/// // environment=production
/// // region=us-east-1
/// ]]></code>
/// </example>
public static class TagSanitizer
{
    /// <summary>
    /// Sanitizes a set of tags by enforcing length and count constraints.
    /// </summary>
    /// <param name="tags">The original tag dictionary to sanitize (required).</param>
    /// <param name="maxKeyLength">
    /// Maximum allowed key length. 
    /// A value of 0 or less disables key length limiting.
    /// </param>
    /// <param name="maxValueLength">
    /// Maximum allowed value length. 
    /// A value of 0 or less disables value length limiting.
    /// </param>
    /// <param name="maxTags">
    /// Maximum number of tags to keep. 
    /// A value of <see langword="null"/>, 0, or negative disables tag count limiting.
    /// </param>
    /// <returns>
    /// A sanitized, immutable <see cref="FrozenDictionary{TKey,TValue}"/> with applied constraints.
    /// Keys and values exceeding the specified limits are truncated.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="tags"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// - Empty/null keys are skipped entirely.<br/>
    /// - Empty/null values are converted to <see cref="string.Empty"/>.<br/>
    /// - If <paramref name="maxTags"/> is specified and fewer tags are allowed than provided,
    /// extra tags are dropped after the limit is reached.<br/>
    /// - The return value is a new immutable dictionary; the input is never mutated.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var tags = new Dictionary<string,string>
    /// {
    ///     ["host"] = "app-server-01",
    ///     ["env"] = "production",
    ///     ["region"] = "eu-west-1"
    /// }.ToFrozenDictionary();
    ///
    /// var sanitized = TagSanitizer.Sanitize(tags, maxKeyLength: 5, maxValueLength: 10, maxTags: 2);
    /// // Result contains 2 tags with truncated keys/values if needed.
    /// ]]></code>
    /// </example>
    public static FrozenDictionary<string, string> Sanitize(
        FrozenDictionary<string, string> tags,
        int maxKeyLength,
        int maxValueLength,
        int? maxTags = null)
    {
        ArgumentNullException.ThrowIfNull(tags);

        if (tags.Count == 0)
        {
            return tags is FrozenDictionary<string, string> f ? f : tags.ToFrozenDictionary(StringComparer.Ordinal);
        }

        // Negative/zero limits disable enforcement
        bool limitKeys = maxKeyLength > 0;
        bool limitVals = maxValueLength > 0;
        bool limitCount = maxTags is > 0;

        var dict = limitCount
            ? new Dictionary<string, string>(Math.Min(tags.Count, maxTags!.Value), StringComparer.Ordinal)
            : new Dictionary<string, string>(tags.Count, StringComparer.Ordinal);

        foreach (var (k0, v0) in tags)
        {
            if (limitCount && dict.Count >= maxTags!)
                break;

            if (string.IsNullOrEmpty(k0))
                continue;

            string k = limitKeys && k0.Length > maxKeyLength ? k0[..maxKeyLength] : k0;
            if (string.IsNullOrEmpty(v0))
            {
                dict[k] = string.Empty;
                continue;
            }

            string v = limitVals && v0.Length > maxValueLength ? v0[..maxValueLength] : v0;
            dict[k] = v;
        }

        return dict.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
