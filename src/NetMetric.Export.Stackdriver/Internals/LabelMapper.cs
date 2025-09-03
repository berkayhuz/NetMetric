// <copyright file="LabelMapper.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Stackdriver.Internals;

/// <summary>
/// Provides helper methods for converting metric tags into Google Cloud Monitoring (Stackdriver)
/// label dictionaries, enforcing length, count, and character constraints.
/// </summary>
/// <remarks>
/// <para>
/// Google Cloud Monitoring applies limits to label keys and values (e.g., maximum number of labels,
/// maximum key/value lengths, and allowed characters). This class centralizes the enforcement of
/// those limits so callers can safely transform arbitrary tag sets into valid Stackdriver labels.
/// </para>
/// <para>
/// Keys are sanitized to contain only letters, digits, underscore (<c>_</c>), or dash (<c>-</c>),
/// and to start with a letter (a <c>k_</c> prefix is added when necessary). Values exceeding the
/// configured maximum length are truncated. The total number of labels is capped according to the
/// provided options.
/// </para>
/// <para><strong>Thread safety:</strong> All members are stateless and thread-safe.</para>
/// </remarks>
/// <example>
/// The following example shows how to convert a set of tags into Stackdriver-compatible labels:
/// <code language="csharp"><![CDATA[
/// using System.Collections.Frozen;
/// using NetMetric.Export.Stackdriver.Exporters;
/// using NetMetric.Export.Stackdriver.Internals;
///
/// var tags = new Dictionary<string, string?>
/// {
///     ["env"] = "prod",
///     ["service.name"] = "checkout-api",
///     ["bad key!*"] = "value-with-üñíçødê"
/// };
///
/// var options = new StackdriverExporterOptions
/// {
///     MaxLabelsPerMetric = 10,
///     MaxLabelKeyLength = 64,
///     MaxLabelValueLength = 256
/// };
///
/// var labels = LabelMapper.ToLabels(tags!, options);
///
/// // labels now contains sanitized keys (e.g., "service_name", "k_bad_key__")
/// // and values truncated to MaxLabelValueLength when applicable.
/// ]]></code>
/// </example>
internal static class LabelMapper
{
    /// <summary>
    /// Converts a collection of NetMetric tags to a Stackdriver-compatible label dictionary,
    /// applying key sanitization, value truncation, and label count limits.
    /// </summary>
    /// <param name="tags">The source tag key/value pairs to convert. May be <see langword="null"/>.</param>
    /// <param name="opt">The exporter options that specify label limits and constraints.</param>
    /// <returns>
    /// A read-only dictionary containing labels that comply with Stackdriver requirements.
    /// Returns an empty dictionary when <paramref name="tags"/> is <see langword="null"/> or empty.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Behavior:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>Skips entries with <see cref="string.IsNullOrWhiteSpace(string)"/> keys.</description>
    ///   </item>
    ///   <item>
    ///     <description>Sanitizes each key via <see cref="SanitizeKey(string, int)"/>.</description>
    ///   </item>
    ///   <item>
    ///     <description>Truncates values longer than <paramref name="opt"/>.<c>MaxLabelValueLength</c> (when &gt; 0).</description>
    ///   </item>
    ///   <item>
    ///     <description>Stops when the number of labels reaches <paramref name="opt"/>.<c>MaxLabelsPerMetric</c> (when specified).</description>
    ///   </item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="opt"/> is <see langword="null"/>.</exception>
    public static FrozenDictionary<string, string> ToLabels(
        IReadOnlyDictionary<string, string> tags,
        StackdriverExporterOptions opt)
    {
        ArgumentNullException.ThrowIfNull(opt);

        if (tags is null || tags.Count == 0)
        {
            return FrozenDictionary<string, string>.Empty;
        }

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        int max = opt.MaxLabelsPerMetric ?? int.MaxValue;

        foreach (var kv in tags)
        {
            if (dict.Count >= max)
            {
                break;
            }
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            string key = SanitizeKey(kv.Key, opt.MaxLabelKeyLength);
            string val = kv.Value ?? string.Empty;

            if (opt.MaxLabelValueLength > 0 && val.Length > opt.MaxLabelValueLength)
            {
                val = val[..opt.MaxLabelValueLength];
            }

            dict[key] = val;
        }

        return dict.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Produces a Stackdriver-safe label key by normalizing characters and ensuring
    /// the first character is a letter; adds the prefix <c>k_</c> when needed.
    /// </summary>
    /// <param name="key">The original (unsanitized) label key.</param>
    /// <param name="maxLen">The maximum allowed key length (values &lt; 1 are treated as 1).</param>
    /// <returns>
    /// A key consisting only of letters, digits, underscore (<c>_</c>), or dash (<c>-</c>), and beginning with a letter.
    /// Characters outside this set are replaced with <c>_</c>. The result is truncated to <paramref name="maxLen"/>.
    /// </returns>
    /// <remarks>
    /// This method is deterministic and culture-insensitive (ordinal character checks).
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    private static string SanitizeKey(string key, int maxLen)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Allocate buffer with clamped length (at least 1 char, at most maxLen).
        Span<char> buf = stackalloc char[Math.Min(key.Length, Math.Max(maxLen, 1))];

        var idx = 0;
        foreach (var ch in key)
        {
            if (idx >= buf.Length)
            {
                break;
            }

            buf[idx++] = char.IsLetterOrDigit(ch) ? ch :
                         (ch is '_' or '-' ? ch : '_');
        }

        var s = new string(buf[..idx]);

        if (s.Length == 0 || !char.IsLetter(s[0]))
        {
            s = $"k_{s}";
        }

        return s;
    }
}
