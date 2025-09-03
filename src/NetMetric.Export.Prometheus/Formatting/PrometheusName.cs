// <copyright file="PrometheusName.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Prometheus.Formatting;

/// <summary>
/// Provides utility methods for normalizing metric names and escaping strings
/// according to the Prometheus text exposition format rules.
/// </summary>
/// <remarks>
/// <para>
/// Prometheus metric names must match the pattern <c>[a-zA-Z_:][a-zA-Z0-9_:]*</c>.
/// This type offers a robust sanitizer that coerces arbitrary input into a
/// Prometheus-compliant name, as well as helpers for escaping label values and
/// HELP text for safe inclusion in the text format.
/// </para>
/// <para>
/// For the official rules and additional background, see:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       The Prometheus text format specification:
///       <see href="https://prometheus.io/docs/instrumenting/exposition_formats/"/>
///     </description>
///   </item>
///   <item>
///     <description>
///       Metric and label naming best practices:
///       <see href="https://prometheus.io/docs/practices/naming/"/>
///     </description>
///   </item>
/// </list>
/// <para>
/// This class is <see langword="static"/> and thread-safe.
/// </para>
/// </remarks>
internal static class PrometheusName
{
    /// <summary>
    /// Converts an arbitrary metric name into a Prometheus-compliant identifier.
    /// </summary>
    /// <param name="raw">The raw metric name to sanitize. If <see langword="null"/> or whitespace, a default is used.</param>
    /// <param name="asciiOnly">
    /// When <see langword="true"/>, only ASCII letters and digits are permitted; any
    /// non-ASCII character is replaced with <c>'_'</c>. When <see langword="false"/>,
    /// Unicode letters and digits are allowed and letters are lowercased using
    /// <see cref="CultureInfo.InvariantCulture"/>.
    /// </param>
    /// <returns>
    /// A sanitized metric name that conforms to Prometheus naming rules. If
    /// <paramref name="raw"/> is <see langword="null"/> or whitespace, the fallback
    /// <c>"netmetric_unnamed"</c> is returned.
    /// </returns>
    /// <remarks>
    /// <para>The sanitizer enforces the following:</para>
    /// <list type="bullet">
    ///   <item><description>Spaces (<c>' '</c>), dots (<c>'.'</c>), and dashes (<c>'-'</c>) are replaced with underscores (<c>'_'</c>).</description></item>
    ///   <item><description>The first character must be a letter, underscore (<c>'_'</c>), or colon (<c>':'</c>).</description></item>
    ///   <item><description>Subsequent characters may include digits.</description></item>
    ///   <item><description>Any disallowed character is replaced with an underscore.</description></item>
    /// </list>
    /// <para>
    /// This method does not validate semantic conventions (e.g., unit suffixes); it
    /// only enforces lexical compliance with the Prometheus grammar.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // ASCII-only sanitization:
    /// var name1 = PrometheusName.SanitizeMetricName("My App-Requests.Total", asciiOnly: true);
    /// // Result: "My_App_Requests_Total"
    ///
    /// // Unicode-friendly sanitization (letters are lowercased):
    /// var name2 = PrometheusName.SanitizeMetricName("İstek.Sayısı", asciiOnly: false);
    /// // Result (example): "istek_sayısı"
    ///
    /// // Null/whitespace input falls back to a safe default:
    /// var name3 = PrometheusName.SanitizeMetricName(null, asciiOnly: true);
    /// // Result: "netmetric_unnamed"
    /// ]]></code>
    /// </example>
    internal static string SanitizeMetricName(string? raw, bool asciiOnly)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "netmetric_unnamed";
        }

        Span<char> buffer = stackalloc char[raw.Length];
        int j = 0;

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];

            // Replace common separators with underscore.
            if (c == ' ' || c == '.' || c == '-')
            {
                buffer[j++] = '_';
                continue;
            }

            if (i == 0)
            {
                buffer[j++] = IsValidFirst(c, asciiOnly) ? Normalize(c, asciiOnly) : '_';
            }
            else
            {
                buffer[j++] = IsValidRest(c, asciiOnly) ? Normalize(c, asciiOnly) : '_';
            }
        }

        return new string(buffer[..j]);
    }

    /// <summary>
    /// Escapes a label value for safe inclusion in the Prometheus text exposition format.
    /// </summary>
    /// <param name="value">The raw label value. If <see langword="null"/>, an empty string is returned.</param>
    /// <param name="maxLen">
    /// The maximum allowed length for the returned string. Values longer than this
    /// are truncated to <paramref name="maxLen"/> characters. Specify <c>&lt;= 0</c> to disable truncation.
    /// </param>
    /// <returns>
    /// An escaped label value where backslashes (<c>\</c>), newlines, and double quotes are encoded
    /// as <c>"\\\\"</c>, <c>"\\n"</c>, and <c>"\\\""</c> respectively.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Prometheus requires label values to be quoted, with special characters escaped.
    /// This helper performs the minimal escaping necessary for the text format.
    /// </para>
    /// <para>
    /// Truncation (when enabled) occurs before escaping. If you need to guarantee a
    /// post-escape maximum length, consider truncating the result of this method.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var raw   = "path=\"/api/v1\"\nstatus=200";
    /// var safe  = PrometheusName.EscapeLabelValue(raw, maxLen: 0);
    /// // safe == "path=\\\"/api/v1\\\"\\nstatus=200"
    ///
    /// var short = PrometheusName.EscapeLabelValue("0123456789", maxLen: 5);
    /// // short == "01234"
    /// ]]></code>
    /// </example>
    internal static string EscapeLabelValue(string? value, int maxLen)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (maxLen > 0 && value.Length > maxLen)
        {
            value = value.AsSpan(0, maxLen).ToString();
        }

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// Escapes metric HELP text for the Prometheus text exposition format.
    /// </summary>
    /// <param name="s">The raw HELP text. May be <see langword="null"/>.</param>
    /// <returns>
    /// The escaped HELP text string. If <paramref name="s"/> is <see langword="null"/>,
    /// an empty string is returned.
    /// </returns>
    /// <remarks>
    /// <para>
    /// In the text format, HELP lines must escape backslashes and newlines.
    /// Double quotes are not required to be escaped in HELP (unlike in label values),
    /// but escaping backslashes and newlines is mandatory.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var help = "Total requests processed\nacross all endpoints.";
    /// var safe = PrometheusName.EscapeHelp(help);
    /// // safe == "Total requests processed\\nacross all endpoints."
    /// ]]></code>
    /// </example>
    internal static string EscapeHelp(string? s)
    {
        return s?
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            ?? string.Empty;
    }

    // ---- Helpers (private) --------------------------------------------------

    private static bool IsAsciiLetterOrDigit(char c) =>
        (c >= 'a' && c <= 'z') ||
        (c >= 'A' && c <= 'Z') ||
        (c >= '0' && c <= '9');

    private static bool IsValidFirst(char c, bool asciiOnly) =>
        c == '_' || c == ':' || (asciiOnly ? IsAsciiLetter(c) : char.IsLetter(c));

    private static bool IsValidRest(char c, bool asciiOnly) =>
        c == '_' || c == ':' || (asciiOnly ? IsAsciiLetterOrDigit(c) : char.IsLetterOrDigit(c));

    private static bool IsAsciiLetter(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static char Normalize(char c, bool asciiOnly)
        => asciiOnly ? c : char.ToLower(c, CultureInfo.InvariantCulture);
}
