// <copyright file="InfluxLineProtocolFormatter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.InfluxDB.Internal;

/// <summary>
/// Provides functionality for generating Influx Line Protocol (ILP) strings
/// from <see cref="NetMetric.Abstractions.IMetric"/> values.
/// </summary>
/// <remarks>
/// <para>
/// This class is <see langword="static"/>, side-effect free, and suitable for unit testing.
/// It handles escaping measurement names, tags, and fields according to InfluxDB Line Protocol rules,
/// and supports multiple metric value shapes such as gauges, counters, distributions, summaries,
/// histograms, and multi-samples.
/// </para>
/// <para>
/// The emitted line format is:
/// <c>measurement[,tag=value...] field=value[,fieldN=valueN] timestamp</c>
/// </para>
/// <para>
/// Timestamp precision can be <c>"s"</c>, <c>"ms"</c>, <c>"us"</c>, or <c>"ns"</c>; see
/// <see cref="ToEpoch(System.DateTime,string)"/>.
/// </para>
/// </remarks>
internal static class InfluxLineProtocolFormatter
{
    private static readonly System.Globalization.CultureInfo Ci = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>
    /// Appends a single line of Influx Line Protocol text representing the specified metric.
    /// </summary>
    /// <param name="sb">The target <see cref="System.Text.StringBuilder"/> to append to.</param>
    /// <param name="metric">The metric to serialize.</param>
    /// <param name="tsUtc">The UTC timestamp to assign to the line.</param>
    /// <param name="precision">
    /// Timestamp precision specifier. Supported values are <c>"s"</c>, <c>"ms"</c>, <c>"us"</c>, and <c>"ns"</c>.
    /// </param>
    /// <remarks>
    /// The generated line follows the format
    /// <c>measurement[,tag=value...] field=value[,fieldN=valueN] timestamp</c>.
    /// Measurement and tag keys/values are escaped per ILP rules; fields are emitted based on the metric's value shape.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// // Assuming a metric:
    /// // Name: "http_requests", Tags: { method = "GET", code = "200" }, Value: CounterValue(123)
    /// var sb = new StringBuilder();
    /// InfluxLineProtocolFormatter.AppendMetricLine(
    ///     sb,
    ///     metric,
    ///     DateTime.UtcNow,
    ///     precision: "ms");
    ///
    /// // Example ILP (timestamp will differ):
    /// // http_requests,code=200,method=GET value=123i 1714651234567
    /// </code>
    /// </example>
    public static void AppendMetricLine(System.Text.StringBuilder sb, NetMetric.Abstractions.IMetric metric, System.DateTime tsUtc, string precision)
    {
        System.ArgumentNullException.ThrowIfNull(sb);
        System.ArgumentNullException.ThrowIfNull(metric);

        // Measurement
        var measurement = EscapeMeasurement(metric.Name);

        // Tags
        sb.Append(measurement);
        if (metric.Tags.Count > 0)
        {
            foreach (var kv in metric.Tags.OrderBy(k => k.Key, System.StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                sb.Append(',');
                sb.Append(EscapeTagKey(kv.Key));
                sb.Append('=');
                sb.Append(EscapeTagValue(kv.Value ?? string.Empty));
            }
        }

        // Fields
        sb.Append(' ');
        AppendFields(sb, metric.GetValue());

        // Timestamp
        sb.Append(' ');
        sb.Append(ToEpoch(tsUtc, precision));
        sb.AppendLine();
    }

    /// <summary>
    /// Appends field key–value pairs for the provided metric value.
    /// </summary>
    /// <param name="sb">The buffer to append to.</param>
    /// <param name="val">The metric value object.</param>
    /// <remarks>
    /// <para>
    /// Handles the following value types:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description><see cref="NetMetric.Abstractions.GaugeValue"/> → <c>value=&lt;double&gt;</c> (floating point).</description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="NetMetric.Abstractions.CounterValue"/> → <c>value=&lt;long&gt;i</c> (integer with <c>i</c> suffix).</description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="NetMetric.Abstractions.DistributionValue"/> → <c>count</c>, <c>min</c>, <c>max</c>, <c>p50</c>, <c>p90</c>, <c>p99</c>.</description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="NetMetric.Abstractions.SummaryValue"/> → <c>count</c>, <c>min</c>, <c>max</c>, plus <c>q{quantile}</c> fields.</description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="NetMetric.Abstractions.BucketHistogramValue"/> → <c>count</c>, <c>min</c>, <c>max</c>, <c>sum</c>, and bucket counters <c>b{index}_le</c>.</description>
    ///   </item>
    ///   <item>
    ///     <description><see cref="NetMetric.Abstractions.MultiSampleValue"/> → <c>items</c> (sample count).</description>
    ///   </item>
    /// </list>
    /// <para>
    /// Unknown values are serialized as a quoted string field named <c>unknown</c>.
    /// </para>
    /// </remarks>
    private static void AppendFields(System.Text.StringBuilder sb, object? val)
    {
        System.ArgumentNullException.ThrowIfNull(sb);

        switch (val)
        {
            case NetMetric.Abstractions.GaugeValue g:
                sb.Append("value=");
                sb.Append(g.Value.ToString("0.##########", Ci));
                break;

            case NetMetric.Abstractions.CounterValue c:
                sb.Append("value=");
                sb.Append(c.Value.ToString(Ci));
                sb.Append('i'); // integer field
                break;

            case NetMetric.Abstractions.DistributionValue d:
                AppendKV(sb, "count", d.Count.ToString(Ci) + "i");
                AppendKV(sb, "min", d.Min.ToString("0.##########", Ci));
                AppendKV(sb, "max", d.Max.ToString("0.##########", Ci));
                AppendKV(sb, "p50", d.P50.ToString("0.##########", Ci));
                AppendKV(sb, "p90", d.P90.ToString("0.##########", Ci));
                AppendKV(sb, "p99", d.P99.ToString("0.##########", Ci), first: false);
                break;

            case NetMetric.Abstractions.SummaryValue s:
                AppendKV(sb, "count", s.Count.ToString(Ci) + "i");
                AppendKV(sb, "min", s.Min.ToString("0.##########", Ci));
                AppendKV(sb, "max", s.Max.ToString("0.##########", Ci));
                foreach (var q in s.Quantiles.OrderBy(k => k.Key))
                    AppendKV(sb, $"q{q.Key.ToString("0.##", Ci)}", q.Value.ToString("0.##########", Ci));
                break;

            case NetMetric.Abstractions.BucketHistogramValue bh:
                AppendKV(sb, "count", bh.Count.ToString(Ci) + "i");
                AppendKV(sb, "min", bh.Min.ToString("0.##########", Ci));
                AppendKV(sb, "max", bh.Max.ToString("0.##########", Ci));
                AppendKV(sb, "sum", bh.Sum.ToString("0.##########", Ci));
                for (int i = 0; i < bh.Counts.Count; i++)
                    AppendKV(sb, $"b{i}_le", bh.Counts[i].ToString(Ci) + "i");
                break;

            case NetMetric.Abstractions.MultiSampleValue ms:
                AppendKV(sb, "items", ms.Items.Count.ToString(Ci) + "i");
                break;

            case null:
                sb.Append("value=0i");
                break;

            default:
                AppendKV(sb, "unknown", $"\"{val.ToString()?.Replace("\"", "\\\"", System.StringComparison.Ordinal) ?? "null"}\"");
                break;
        }
    }

    /// <summary>
    /// Appends a single field key–value pair to the ILP fields segment.
    /// </summary>
    /// <param name="sb">The target buffer.</param>
    /// <param name="key">The field key (escaped as needed).</param>
    /// <param name="value">The field value string (already formatted, including integer suffixes if applicable).</param>
    /// <param name="first">
    /// Indicates whether this is the first field in the list; when <see langword="false"/>,
    /// a comma is prepended before writing the pair.
    /// </param>
    private static void AppendKV(System.Text.StringBuilder sb, string key, string value, bool first = false)
    {
        System.ArgumentNullException.ThrowIfNull(sb);

        if (!first) sb.Append(',');

        sb.Append(EscapeFieldKey(key));
        sb.Append('=');
        sb.Append(value);
    }

    // --- Escaping methods (ILP rules) ---

    /// <summary>
    /// Escapes a measurement name for Influx Line Protocol (escapes <c>,</c> and space).
    /// </summary>
    /// <param name="s">The raw measurement name.</param>
    /// <returns>The escaped measurement name.</returns>
    private static string EscapeMeasurement(string s)
    {
        System.ArgumentNullException.ThrowIfNull(s);

        return s.Replace(",", "\\,", System.StringComparison.Ordinal)
                .Replace(" ", "\\ ", System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Escapes a tag key according to Influx Line Protocol rules (escapes <c>,</c>, space, and <c>=</c>).
    /// </summary>
    /// <param name="s">The raw tag key.</param>
    /// <returns>The escaped tag key.</returns>
    private static string EscapeTagKey(string s)
    {
        System.ArgumentNullException.ThrowIfNull(s);

        return s.Replace(",", "\\,", System.StringComparison.Ordinal)
                .Replace(" ", "\\ ", System.StringComparison.Ordinal)
                .Replace("=", "\\=", System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Escapes a tag value according to Influx Line Protocol rules (same rules as tag keys).
    /// </summary>
    /// <param name="s">The raw tag value.</param>
    /// <returns>The escaped tag value.</returns>
    private static string EscapeTagValue(string s)
    {
        return EscapeTagKey(s);
    }

    /// <summary>
    /// Escapes a field key according to Influx Line Protocol rules (same rules as tag keys).
    /// </summary>
    /// <param name="s">The raw field key.</param>
    /// <returns>The escaped field key.</returns>
    private static string EscapeFieldKey(string s)
    {
        return EscapeTagKey(s);
    }

    /// <summary>
    /// Converts a UTC timestamp to a Unix epoch value in the specified precision.
    /// </summary>
    /// <param name="utc">The UTC timestamp.</param>
    /// <param name="precision">
    /// The precision specifier: <c>"s"</c> (seconds), <c>"ms"</c> (milliseconds),
    /// <c>"us"</c> (microseconds), or <c>"ns"</c> (nanoseconds). Any other value defaults to nanoseconds.
    /// </param>
    /// <returns>
    /// A 64-bit integer representing the Unix epoch time at the requested precision.
    /// </returns>
    /// <example>
    /// <code language="csharp">
    /// var now = DateTime.UtcNow;
    /// var epochMs = InfluxLineProtocolFormatter.ToEpoch(now, "ms"); // e.g., 1714651234567
    /// </code>
    /// </example>
    private static long ToEpoch(System.DateTime utc, string precision)
    {
        var ticks = utc.Ticks - System.DateTime.UnixEpoch.Ticks; // 100ns
        return precision switch
        {
            "s" => ticks / System.TimeSpan.TicksPerSecond,
            "ms" => ticks / System.TimeSpan.TicksPerMillisecond,
            "us" => ticks / 10, // 1 microsecond = 10 * 100ns
            _ => ticks * 100,   // default nanoseconds
        };
    }
}
