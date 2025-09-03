// <copyright file="JsonConsoleExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Buffers;
using System.Text;
using NetMetric.Abstractions;

namespace NetMetric.Export.JsonConsole.Exporters;

/// <summary>
/// Exports NetMetric metrics to the console as one compact JSON object per line.
/// </summary>
/// <remarks>
/// <para>
/// Each metric is serialized into a single-line JSON object suitable for log aggregation systems
/// (e.g., <c>stdout</c> scraping, file tailing, or shipping with tools like Fluent Bit).
/// The serializer uses <see cref="System.Text.Json.Utf8JsonWriter"/> (no reflection),
/// making the implementation trimming/AOT safe.
/// </para>
/// <para>
/// <b>Output shape.</b> Common fields are always present:
/// <list type="bullet">
///   <item><description><c>ts</c>: ISO 8601 timestamp (UTC; round-trip "O" format).</description></item>
///   <item><description><c>id</c>, <c>name</c>: Metric identity and friendly name.</description></item>
///   <item><description><c>kind</c>: Introspected metric kind (e.g., <c>"gauge"</c>, <c>"counter"</c>).</description></item>
///   <item><description><c>unit</c> and <c>desc</c> if available.</description></item>
///   <item><description><c>tags</c> object if any tags are set.</description></item>
/// </list>
/// Value-specific fields depend on the underlying metric value type
/// (e.g., <c>value</c>, <c>count</c>/<c>min</c>/<c>max</c>, <c>quantiles</c>, <c>buckets</c>/<c>counts</c>, etc.).
/// Unknown or <see langword="null"/> values are emitted as an <c>"unknown"</c> string field.
/// </para>
/// <para>
/// <b>Threading &amp; performance.</b> The exporter is stateless per call and writes each JSON line asynchronously
/// using the injected <see cref="IAsyncTextWriter"/>. It allocates a modest on-stack/pooled buffer per metric
/// via <see cref="System.Buffers.ArrayBufferWriter{T}"/> and avoids boxing except where unavoidable for dynamic values.
/// </para>
/// <para>
/// <b>Error behavior.</b> If <see cref="System.Threading.CancellationToken"/> is signaled during export,
/// the operation throws <see cref="System.OperationCanceledException"/>. A <see cref="System.ArgumentNullException"/>
/// is thrown if required arguments are <see langword="null"/>.
/// </para>
/// <para>
/// <b>Example usage.</b>
/// </para>
/// <example>
/// <code language="csharp"><![CDATA[
/// Option A: default console writer
/// using var exporter = new JsonConsoleExporter();
/// await exporter.ExportAsync(new[] { metric }, ct);
/// 
/// // Option B: custom writer (tests, redirection)
/// IAsyncTextWriter writer = new ConsoleTextWriter();
/// using var exporter2 = new JsonConsoleExporter(writer: writer, nowUtc: () => DateTime.UtcNow);
/// await exporter2.ExportAsync(metricsBatch, ct);
/// ]]></code>
/// </example>
/// <para>
/// <b>Log ingestion tip.</b> Configure your collector to treat each line as a complete JSON document.
/// </para>
/// </remarks>
public sealed class JsonConsoleExporter : IMetricExporter, IDisposable
{
    private readonly IAsyncTextWriter _writer;
    private readonly Func<DateTime> _nowUtc;
    private readonly bool _leaveOpen;
    private readonly System.Text.Json.JsonWriterOptions _writerOptions = new() { Indented = false };

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConsoleExporter"/> class.
    /// </summary>
    /// <param name="writer">
    /// Optional asynchronous text writer; when <see langword="null"/>, defaults to <see cref="ConsoleTextWriter"/>
    /// that writes to standard output.
    /// </param>
    /// <param name="nowUtc">
    /// Optional clock provider that returns the current UTC time; defaults to <see cref="DateTime.UtcNow"/>.
    /// Used for the emitted <c>ts</c> field.
    /// </param>
    /// <param name="leaveOpen">
    /// When <see langword="true"/>, the underlying <paramref name="writer"/> will not be disposed by
    /// <see cref="Dispose"/>. Defaults to <see langword="false"/>.
    /// </param>
    public JsonConsoleExporter(
        IAsyncTextWriter? writer = null,
        Func<DateTime>? nowUtc = null,
        bool leaveOpen = false)
    {
        _writer = writer ?? new ConsoleTextWriter();
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Exports the provided metrics to the configured text sink, one JSON document per line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method enumerates <paramref name="metrics"/> exactly once and writes each item asynchronously.
    /// It does not wait for the asynchronous writes to complete before returning; the method itself completes
    /// synchronously after scheduling the writes. Consumers that require strict flush semantics should
    /// ensure their <see cref="IAsyncTextWriter"/> implementation provides the desired guarantees.
    /// </para>
    /// <para>
    /// The implementation is AOT/trim safe and does not use reflection. See the <see cref="RequiresUnreferencedCodeAttribute"/>
    /// note for contract compatibility.
    /// </para>
    /// </remarks>
    /// <param name="metrics">A non-<see langword="null"/> sequence of metrics to export.</param>
    /// <param name="ct">A token that can be used to cancel the export operation.</param>
    /// <returns>A completed <see cref="System.Threading.Tasks.Task"/> once writes are scheduled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled via <paramref name="ct"/>.</exception>
    /// <inheritdoc />
    [RequiresUnreferencedCode("Required by the IMetricExporter contract. This implementation does not use reflection and is safe for trimming.")]
    public Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        foreach (var m in metrics)
        {
            ct.ThrowIfCancellationRequested();

            var (unit, desc, kind) = MetricIntrospection.ReadMeta(m);
            var ts = _nowUtc().ToString("O");

            object? v = m.GetValue();
            ArgumentNullException.ThrowIfNull(kind);
            var line = BuildJsonLine(ts, m, kind, unit, desc, v);
            _ = _writer.WriteLineAsync(line, ct);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the single-line JSON document for a given metric and its current value.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="System.Text.Json.Utf8JsonWriter"/> with <see cref="System.Text.Json.JsonWriterOptions.Indented"/> set to <see langword="false"/>
    /// to minimize output size and ensure one-document-per-line formatting.
    /// </remarks>
    /// <param name="ts">An ISO 8601 UTC timestamp string (<c>"O"</c> format) used for the <c>ts</c> field.</param>
    /// <param name="m">The metric instance to serialize. Must not be <see langword="null"/>.</param>
    /// <param name="kind">The introspected metric kind (e.g., <c>"gauge"</c>, <c>"counter"</c>).</param>
    /// <param name="unit">Optional unit string for the metric (may be <see langword="null"/> or empty).</param>
    /// <param name="desc">Optional description string for the metric (may be <see langword="null"/> or empty).</param>
    /// <param name="v">The value payload retrieved from the metric (may be <see langword="null"/>).</param>
    /// <returns>A UTF-8 decoded JSON string representing the metric.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="m"/> is <see langword="null"/>.</exception>
    private string BuildJsonLine(
        string ts,
        IMetric m,
        string kind,
        string? unit,
        string? desc,
        object? v)
    {
        ArgumentNullException.ThrowIfNull(m);

        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var json = new System.Text.Json.Utf8JsonWriter(buffer, _writerOptions))
        {
            json.WriteStartObject();

            // Common fields
            json.WriteString("ts", ts);
            json.WriteString("id", m.Id);
            json.WriteString("name", m.Name);
            json.WriteString("kind", kind);
            if (!string.IsNullOrEmpty(unit)) json.WriteString("unit", unit);
            if (!string.IsNullOrEmpty(desc)) json.WriteString("desc", desc);

            // Tags
            if (m.Tags is { Count: > 0 })
            {
                json.WritePropertyName("tags");
                json.WriteStartObject();
                foreach (var kvp in m.Tags)
                    json.WriteString(kvp.Key, kvp.Value);
                json.WriteEndObject();
            }

            // Value-specific payload
            switch (v)
            {
                case GaugeValue g:
                    json.WritePropertyName("value");
                    json.WriteNumberValue(g.Value);
                    break;

                case CounterValue c:
                    json.WritePropertyName("value");
                    json.WriteNumberValue(c.Value);
                    break;

                case DistributionValue d:
                    json.WriteNumber("count", d.Count);
                    json.WriteNumber("min", d.Min);
                    json.WriteNumber("p50", d.P50);
                    json.WriteNumber("p90", d.P90);
                    json.WriteNumber("p99", d.P99);
                    json.WriteNumber("max", d.Max);
                    break;

                case SummaryValue s:
                    json.WriteNumber("count", s.Count);
                    json.WriteNumber("min", s.Min);
                    json.WriteNumber("max", s.Max);
                    if (s.Quantiles is { Count: > 0 })
                    {
                        json.WritePropertyName("quantiles");
                        json.WriteStartArray();
                        foreach (var q in s.Quantiles)
                        {
                            json.WriteStartObject();
                            json.WriteNumber("q", q.Key);
                            json.WriteNumber("v", q.Value);
                            json.WriteEndObject();
                        }
                        json.WriteEndArray();
                    }
                    break;

                case BucketHistogramValue bh:
                    json.WriteNumber("count", bh.Count);
                    json.WriteNumber("min", bh.Min);
                    json.WriteNumber("max", bh.Max);

                    json.WritePropertyName("buckets");
                    json.WriteStartArray();
                    foreach (var b in bh.Buckets) json.WriteNumberValue(b);
                    json.WriteEndArray();

                    json.WritePropertyName("counts");
                    json.WriteStartArray();
                    foreach (var cval in bh.Counts) json.WriteNumberValue(cval);
                    json.WriteEndArray();

                    json.WriteNumber("sum", bh.Sum);
                    break;

                case MultiSampleValue ms:
                    json.WritePropertyName("items");
                    json.WriteStartArray();
                    foreach (var item in ms.Items)
                        WriteSample(json, item);
                    json.WriteEndArray();
                    break;

                case null:
                    json.WriteString("unknown", "null");
                    break;

                default:
                    json.WriteString("unknown", v.GetType().Name);
                    break;
            }

            json.WriteEndObject();
            json.Flush();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Writes a single sample value into the current JSON array, mapping common CLR types to JSON scalars.
    /// </summary>
    /// <param name="json">The active <see cref="System.Text.Json.Utf8JsonWriter"/> to write to. Must not be <see langword="null"/>.</param>
    /// <param name="item">The item to serialize; may be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    private static void WriteSample(System.Text.Json.Utf8JsonWriter json, object? item)
    {
        ArgumentNullException.ThrowIfNull(json);

        switch (item)
        {
            case null:
                json.WriteNullValue();
                return;

            case byte or sbyte or short or ushort or int or uint or long or ulong:
                json.WriteNumberValue(Convert.ToInt64(item));
                return;

            case float f:
                json.WriteNumberValue(f);
                return;

            case double d:
                json.WriteNumberValue(d);
                return;

            case decimal dm:
                json.WriteNumberValue(dm);
                return;

            case bool b:
                json.WriteBooleanValue(b);
                return;

            case string s:
                json.WriteStringValue(s);
                return;

            default:
                json.WriteStringValue(item.ToString());
                return;
        }
    }

    /// <summary>
    /// Releases resources used by the exporter.
    /// </summary>
    /// <remarks>
    /// If the provided writer implements <see cref="IDisposable"/> and <see cref="_leaveOpen"/> is <see langword="false"/>,
    /// this method will dispose it. Otherwise, it is a no-op.
    /// </remarks>
    public void Dispose()
    {
        if (!_leaveOpen && _writer is IDisposable d)
            d.Dispose();
    }
}
