// <copyright file="ServerTimingParser.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Network.Http;

/// <summary>
/// Provides functionality to parse the <c>Server-Timing</c> header from HTTP responses.
/// The <c>Server-Timing</c> header allows servers to send timing information about various stages of the request processing.
/// </summary>
internal static class ServerTimingParser
{
    /// <summary>
    /// Represents a parsed server timing item, consisting of a name and an optional duration in milliseconds.
    /// </summary>
    internal sealed record Item(string Name, double? DurationMs);

    /// <summary>
    /// Parses the <c>Server-Timing</c> header into a sequence of <see cref="Item"/> instances.
    /// Each <see cref="Item"/> represents a specific timing metric, such as request processing stages or other custom timings.
    /// </summary>
    /// <param name="header">The <c>Server-Timing</c> header value to parse.</param>
    /// <returns>An enumeration of parsed <see cref="Item"/> objects representing the server timing data.</returns>
    public static IEnumerable<Item> Parse(string? header)
    {
        // If the header is null or whitespace, return no items.
        if (string.IsNullOrWhiteSpace(header))
        {
            yield break;
        }

        // Split the header value into individual parts (separated by commas).
        var parts = header.Split(',');

        // Iterate through each part and extract the timing information.
        foreach (var p in parts)
        {
            // Split each part by semicolons into key-value pairs.
            var segs = p.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            // Skip empty parts.
            if (segs.Length == 0)
            {
                continue;
            }

            // The first segment is the name of the timing item.
            var name = segs[0];
            double? dur = null;

            // Look for a "dur" key and parse its value as a duration in milliseconds.
            for (int i = 1; i < segs.Length; i++)
            {
                var kv = segs[i].Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2 && kv[0].Equals("dur", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to parse the duration value as a double.
                    if (double.TryParse(kv[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var ms))
                    {
                        dur = ms;
                    }
                }
            }

            // If a valid name is found, return a new <see cref="Item"/> representing this timing.
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return new Item(name, dur);
            }
        }
    }
}
