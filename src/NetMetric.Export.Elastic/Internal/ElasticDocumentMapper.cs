// <copyright file="ElasticDocumentMapper.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Buffers;
using System.Collections.ObjectModel;
using NetMetric.Abstractions;

namespace NetMetric.Export.Elastic.Internal;

/// <summary>
/// Maps in-memory metrics into two-line NDJSON payloads (action + document) suitable for the
/// Elasticsearch Bulk API (<c>_bulk</c>). Implements <see cref="IElasticDocumentMapper"/> and
/// <see cref="IMetricValueVisitor"/> to emit compact, allocation-conscious JSON using
/// <see cref="Utf8JsonWriter"/> that is AOT/trim friendly.
/// </summary>
/// <remarks>
/// <para>
/// Each call to <see cref="Map(IMetric, DateTime, string)"/> produces exactly two lines:
/// the index action line and the metric document line. The mapper visits the metric's
/// concrete <see cref="MetricValue"/> via <see cref="IMetricValueVisitor"/> and writes a minimal,
/// schema-light document with common metadata (timestamp, id, name, kind, unit, description, tags)
/// and a type-specific <c>body</c> section.
/// </para>
/// <para>
/// The mapper is stateless across calls and not thread-safe for concurrent use of the same instance
/// because it maintains a reusable buffer for the current mapping operation. Create separate instances
/// per concurrent caller or synchronize access.
/// </para>
/// <para>
/// Numeric writing is tolerant: unknown numeric-like values are serialized as strings to avoid runtime
/// failures. Arrays are handled similarly via <see cref="WriteArrayFlexible{T}(Utf8JsonWriter, string, IEnumerable{T})"/>.
/// </para>
/// <para>
/// Performance considerations: the type is allocation-conscious and avoids large intermediate strings by
/// writing to an <see cref="ArrayBufferWriter{T}"/>. Validation in <see cref="JsonWriterOptions.SkipValidation"/>
/// remains enabled to catch misuse during development.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var mapper = new ElasticDocumentMapper(indent: false);
/// var now = DateTime.UtcNow;
/// var index = "metrics-netmetric-2025.09.02";
///
/// // Assume 'metric' implements IMetric and wraps a GaugeValue.
/// IReadOnlyList&lt;string&gt; lines = mapper.Map(metric, now, index);
///
/// // lines[0] == { "index": { "_index": "metrics-netmetric-2025.09.02" } }
/// // lines[1] == metric document (ts, id, name, kind, tags, body...)
/// </code>
/// </example>
/// <seealso cref="IElasticDocumentMapper"/>
/// <seealso cref="IMetricValueVisitor"/>
public sealed class ElasticDocumentMapper : IElasticDocumentMapper, IMetricValueVisitor
{
    private readonly JsonWriterOptions _writerOptions;
    private readonly Collection<string> _lines = new();

    private DateTime _now;
    private IMetric? _metric;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticDocumentMapper"/> class.
    /// </summary>
    /// <param name="indent">
    /// When <see langword="true"/>, emitted JSON is pretty-printed; otherwise it is compact.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <remarks>
    /// Pretty-printing increases payload size and is generally unnecessary for the Bulk API.
    /// Keep it <see langword="false"/> for production use.
    /// </remarks>
    public ElasticDocumentMapper(bool indent = false)
    {
        _writerOptions = new JsonWriterOptions
        {
            Indented = indent,
            SkipValidation = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    /// <summary>
    /// Maps the specified <paramref name="metric"/> into two NDJSON lines: the index action
    /// and the metric document destined for <paramref name="indexName"/>.
    /// </summary>
    /// <param name="metric">The metric to map.</param>
    /// <param name="utcNow">The UTC timestamp to persist in the document (field <c>ts</c>).</param>
    /// <param name="indexName">The target Elasticsearch index name.</param>
    /// <returns>
    /// A read-only list of strings with exactly two entries: the action line and the document line.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="indexName"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    /// <remarks>
    /// This method is not thread-safe. Do not invoke concurrently on the same instance.
    /// </remarks>
    public IReadOnlyList<string> Map(IMetric metric, DateTime utcNow, string indexName)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);

        _lines.Clear();
        _now = utcNow;
        _metric = metric;

        _lines.Add(WriteJsonToString(w =>
        {
            w.WriteStartObject();
            w.WritePropertyName("index");
            w.WriteStartObject();
            w.WriteString("_index", indexName);
            w.WriteEndObject();
            w.WriteEndObject();
        }));

        metric.Accept(this);

        return _lines;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Writes a <c>kind</c> of <c>"gauge"</c> with a single numeric <c>value</c> field.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(GaugeValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        WriteDoc(kind: "gauge", bodyWriter: w =>
        {
            w.WriteNumber("value", value.Value);
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Writes a <c>kind</c> of <c>"counter"</c> with a single numeric <c>value</c> field.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(CounterValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        WriteDoc(kind: "counter", bodyWriter: w =>
        {
            w.WriteNumber("value", value.Value);
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Writes a <c>kind</c> of <c>"distribution"</c> including <c>Count</c>, <c>Min</c>, <c>P50</c>, <c>P90</c>, <c>P99</c>, and <c>Max</c>.
    /// Numeric fields are written via <see cref="WriteNumberFlexible(Utf8JsonWriter, string, object?)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(DistributionValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        WriteDoc(kind: "distribution", bodyWriter: w =>
        {
            WriteNumberFlexible(w, "Count", value.Count);
            WriteNumberFlexible(w, "Min", value.Min);
            WriteNumberFlexible(w, "P50", value.P50);
            WriteNumberFlexible(w, "P90", value.P90);
            WriteNumberFlexible(w, "P99", value.P99);
            WriteNumberFlexible(w, "Max", value.Max);
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Writes a <c>kind</c> of <c>"summary"</c> including <c>Count</c>, <c>Min</c>, <c>Max</c>, and a <c>quantiles</c> array
    /// of <c>{ quantile, value }</c> pairs.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(SummaryValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        WriteDoc(kind: "summary", bodyWriter: w =>
        {
            WriteNumberFlexible(w, "Count", value.Count);
            WriteNumberFlexible(w, "Min", value.Min);
            WriteNumberFlexible(w, "Max", value.Max);

            w.WritePropertyName("quantiles");
            w.WriteStartArray();

            foreach (var kv in value.Quantiles)
            {
                w.WriteStartObject();
                w.WriteNumber("quantile", kv.Key);
                w.WriteNumber("value", kv.Value);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Writes a <c>kind</c> of <c>"bucket_histogram"</c> including <c>Count</c>, <c>Min</c>, <c>Max</c>, <c>Sum</c>,
    /// and two arrays: <c>buckets</c> (bucket boundaries) and <c>counts</c> (observations per bucket).
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(BucketHistogramValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        WriteDoc(kind: "bucket_histogram", bodyWriter: w =>
        {
            WriteNumberFlexible(w, "Count", value.Count);
            WriteNumberFlexible(w, "Min", value.Min);
            WriteNumberFlexible(w, "Max", value.Max);
            WriteNumberFlexible(w, "Sum", value.Sum);

            WriteArrayFlexible(w, "buckets", value.Buckets);
            WriteArrayFlexible(w, "counts", value.Counts);
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Writes a <c>kind</c> of <c>"multi_sample"</c> with an <c>items</c> array of <c>{ k, v }</c> pairs.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(MultiSampleValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        WriteDoc(kind: "multi_sample", bodyWriter: w =>
        {
            w.WritePropertyName("items");
            w.WriteStartArray();

            foreach (var msi in value.Items)
            {
                w.WriteStartObject();

                w.WriteString("k", msi.Name);
                WriteNumberFlexible(w, "v", msi.Value);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Unknown/unsupported metric value types are emitted with <c>kind</c> = <c>"unknown"</c> and
    /// an <c>unknown</c> field containing the runtime type name. This helps detect mapping gaps
    /// without failing the entire bulk operation.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(MetricValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        // Unknown type: record the runtime type name.
        WriteDoc(kind: "unknown", bodyWriter: w =>
        {
            w.WriteString("unknown", value.GetType().Name);
        });
    }

    /// <summary>
    /// Writes a single metric document for the current metric, including common metadata
    /// and the provided <paramref name="bodyWriter"/> content.
    /// </summary>
    /// <param name="kind">Logical metric kind (e.g., <c>gauge</c>, <c>counter</c>).</param>
    /// <param name="bodyWriter">A delegate that writes the type-specific body fields.</param>
    /// <remarks>
    /// The method consumes the current <see cref="_metric"/> and <see cref="_now"/> captured by
    /// <see cref="Map(IMetric, DateTime, string)"/>. It appends the rendered JSON line to the
    /// internal buffer returned by that method.
    /// </remarks>
    private void WriteDoc(string kind, Action<Utf8JsonWriter> bodyWriter)
    {
        var mm = _metric!;

        string? unit = (mm as MetricBase)?.Unit;
        string? desc = (mm as MetricBase)?.Description;

        var line = WriteJsonToString(w =>
        {
            w.WriteStartObject();

            // timestamp
            w.WriteString("ts", _now.ToString("O"));

            // metadata
            w.WriteString("id", mm.Id);
            w.WriteString("name", mm.Name);
            w.WriteString("kind", kind);

            if (unit is not null) w.WriteString("unit", unit);
            if (desc is not null) w.WriteString("desc", desc);

            // tags
            w.WritePropertyName("tags");
            w.WriteStartObject();
            foreach (var tag in mm.Tags)
            {
                w.WriteString(tag.Key, tag.Value);
            }
            w.WriteEndObject();

            // body
            w.WritePropertyName("body");
            w.WriteStartObject();
            bodyWriter(w);
            w.WriteEndObject();

            w.WriteEndObject();
        });

        _lines.Add(line);
    }

    /// <summary>
    /// Renders JSON by invoking <paramref name="write"/> against a temporary
    /// <see cref="Utf8JsonWriter"/> and returns the resulting UTF-8 string.
    /// </summary>
    /// <param name="write">The writer callback that emits the JSON payload.</param>
    /// <returns>The generated JSON text.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="write"/> is <see langword="null"/>.
    /// </exception>
    private string WriteJsonToString(Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
        {
            write(writer);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Writes a JSON number named <paramref name="name"/> when <paramref name="value"/> is a known numeric type;
    /// otherwise writes it as a string. Null values are ignored.
    /// </summary>
    /// <param name="w">The JSON writer.</param>
    /// <param name="name">The property name to write.</param>
    /// <param name="value">The value to serialize.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="w"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    private static void WriteNumberFlexible(Utf8JsonWriter w, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (value is null) return;

        switch (value)
        {
            case byte v: w.WriteNumber(name, v); break;
            case sbyte v: w.WriteNumber(name, v); break;
            case short v: w.WriteNumber(name, v); break;
            case ushort v: w.WriteNumber(name, v); break;
            case int v: w.WriteNumber(name, v); break;
            case uint v: w.WriteNumber(name, v); break;
            case long v: w.WriteNumber(name, v); break;
            case ulong v: w.WriteNumber(name, v); break;
            case float v: w.WriteNumber(name, v); break;
            case double v: w.WriteNumber(name, v); break;
            case decimal v: w.WriteNumber(name, v); break;
            default:
                w.WriteString(name, value.ToString());
                break;
        }
    }

    /// <summary>
    /// Writes a JSON array named <paramref name="name"/> from <paramref name="items"/>, emitting numbers for known numeric types
    /// and strings otherwise. Null items are serialized as <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="w">The JSON writer.</param>
    /// <param name="name">The property name for the array.</param>
    /// <param name="items">The sequence of items to serialize.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="w"/> or <paramref name="items"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    private static void WriteArrayFlexible<T>(Utf8JsonWriter w, string name, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(items);

        w.WritePropertyName(name);
        w.WriteStartArray();
        foreach (var it in items)
        {
            switch (it)
            {
                case byte v: w.WriteNumberValue(v); break;
                case sbyte v: w.WriteNumberValue(v); break;
                case short v: w.WriteNumberValue(v); break;
                case ushort v: w.WriteNumberValue(v); break;
                case int v: w.WriteNumberValue(v); break;
                case uint v: w.WriteNumberValue(v); break;
                case long v: w.WriteNumberValue(v); break;
                case ulong v: w.WriteNumberValue(v); break;
                case float v: w.WriteNumberValue(v); break;
                case double v: w.WriteNumberValue(v); break;
                case decimal v: w.WriteNumberValue(v); break;
                default:
                    w.WriteStringValue(it?.ToString());
                    break;
            }
        }
        w.WriteEndArray();
    }
}
