// <copyright file="Ucum.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Stackdriver.Internals;

/// <summary>
/// Provides normalization of unit strings into UCUM (Unified Code for Units of Measure)
/// codes, as required by Google Cloud Monitoring (Stackdriver).
/// </summary>
/// <remarks>
/// <para>
/// Google Cloud Monitoring expects metric units to be expressed in UCUM codes.  
/// This helper class provides a simple mapping from commonly used human-readable
/// unit strings (e.g., <c>"bytes"</c>, <c>"ms"</c>, <c>"percent"</c>) to their
/// standardized UCUM equivalents (e.g., <c>"By"</c>, <c>"ms"</c>, <c>"1"</c>).
/// </para>
/// <para>
/// The mapping is case-insensitive and covers common storage, time, and ratio units.  
/// If a given unit string cannot be recognized, it is returned unchanged to avoid
/// silently discarding user intent.
/// </para>
/// <para>
/// For ratio-like units (such as <c>"percent"</c>, <c>"count"</c>, or <c>"item"</c>),
/// the UCUM code <c>"1"</c> is used, since these values represent dimensionless quantities.
/// </para>
/// </remarks>
/// <example>
/// Example usage:
/// <code>
/// string normalized1 = Ucum.Normalize("bytes");   // returns "By"
/// string normalized2 = Ucum.Normalize("ms");      // returns "ms"
/// string normalized3 = Ucum.Normalize("percent"); // returns "1"
/// string normalized4 = Ucum.Normalize("unknown"); // returns "unknown"
/// string normalized5 = Ucum.Normalize(null);      // returns ""
/// </code>
/// </example>
internal static class Ucum
{
    /// <summary>
    /// A mapping of common human-readable unit strings to UCUM codes.
    /// </summary>
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Storage units
        ["byte"] = "By",
        ["bytes"] = "By",
        ["by"] = "By",
        ["kilobytes"] = "kBy",
        ["kb"] = "kBy",
        ["megabytes"] = "MBy",
        ["mb"] = "MBy",
        ["gigabytes"] = "GBy",
        ["gb"] = "GBy",

        // Time units
        ["second"] = "s",
        ["seconds"] = "s",
        ["ms"] = "ms",
        ["microsecond"] = "us",
        ["nanosecond"] = "ns",

        // Dimensionless ratios
        ["percent"] = "1",
        ["ratio"] = "1",
        ["count"] = "1",
        ["item"] = "1"
    };

    /// <summary>
    /// Normalizes a unit string into a UCUM-compatible code.
    /// </summary>
    /// <param name="unit">
    /// The input unit string to normalize.  
    /// Examples: <c>"bytes"</c>, <c>"ms"</c>, <c>"percent"</c>.
    /// </param>
    /// <returns>
    /// <list type="bullet">
    /// <item><description>The UCUM code if the unit is recognized (e.g., <c>"By"</c>, <c>"ms"</c>, <c>"1"</c>).</description></item>
    /// <item><description>An empty string if <paramref name="unit"/> is <see langword="null"/>, empty, or whitespace.</description></item>
    /// <item><description>The original unit string unchanged if no mapping exists.</description></item>
    /// </list>
    /// </returns>
    public static string Normalize(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
            return "";

        return Map.TryGetValue(unit!, out var mapped) ? mapped : unit!;
    }
}
