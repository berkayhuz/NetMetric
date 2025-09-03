// <copyright file="JsonLinesExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.2.1
// </copyright>

using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NetMetric.Export.Exporters;

/// <summary>
/// Exports metrics in the <c>JSON Lines</c> format (<em>one JSON object per line</em>).
/// </summary>
/// <remarks>
/// <para>
/// Each metric value visited through <see cref="IMetricValueVisitor"/> is projected into a single
/// JSON object and written as a line to the configured <see cref="TextWriter"/>. The exporter is
/// designed to be trimming- and AOT-friendly by relying on
/// <see cref="JsonSerializerContext"/> source generation via <see cref="NetMetricJsonContext"/>.
/// </para>
/// <para>
/// The payload schema is represented by <see cref="MetricPayload"/> and includes common metric
/// metadata (timestamp, id, name, unit, and tags) plus metric-specific fields (e.g. <c>value</c>,
/// <c>buckets</c>, <c>counts</c>, or <c>quantiles</c>).
/// </para>
/// <para>
/// <strong>Output characteristics</strong>:
/// <list type="bullet">
///   <item><description>One JSON object per line (no indentation).</description></item>
///   <item><description>UTC timestamps in ISO 8601 "O" (round-trip) format.</description></item>
///   <item><description>Stable field names suitable for log shippers and tail-based ingestion.</description></item>
/// </list>
/// </para>
/// </remarks>
/// <threadsafety>
/// <para>
/// <strong>Thread safety:</strong> This type is <em>not</em> thread-safe. Use a single instance per
/// writer/stream and do not invoke <see cref="ExportAsync(System.Collections.Generic.IEnumerable{IMetric}, System.Threading.CancellationToken)"/>
/// concurrently. If multiple concurrent export pipelines are needed, create separate instances,
/// each with its own <see cref="TextWriter"/>.
/// </para>
/// </threadsafety>
/// <example>
/// The following example writes metrics to a rolling file in append mode:
/// <code language="csharp"><![CDATA[
/// using var exporter = JsonLinesExporter.ToFile("/var/log/metrics.ndjson");
/// await exporter.ExportAsync(new IMetric[] { myGauge, myCounter }, ct);
/// ]]></code>
/// Creating with a custom writer and JSON options:
/// <code language="csharp"><![CDATA[
/// var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false };
/// using var writer = new StreamWriter(Console.OpenStandardOutput());
/// using var exporter = new JsonLinesExporter(writer, options, () => DateTime.UtcNow, leaveOpen: true);
/// await exporter.ExportAsync(metrics, ct);
/// ]]></code>
/// </example>
public sealed class JsonLinesExporter : IMetricExporter, IMetricValueVisitor, IDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _leaveOpen;
    private readonly JsonSerializerOptions _json;
    private readonly Func<DateTime> _nowUtc;
    private readonly NetMetricJsonContext _ctx;
    private readonly JsonTypeInfo<MetricPayload> _payloadTypeInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonLinesExporter"/> class.
    /// </summary>
    /// <param name="writer">The <see cref="TextWriter"/> used to emit JSON lines.</param>
    /// <param name="jsonOptions">
    /// Optional JSON options. Defaults to <see cref="JsonSerializerDefaults.Web"/> with unindented output.
    /// </param>
    /// <param name="nowUtc">
    /// Optional time provider used to stamp payloads. Defaults to <see cref="DateTime.UtcNow"/>.
    /// </param>
    /// <param name="leaveOpen">
    /// When <see langword="true"/>, this instance does not dispose the underlying <paramref name="writer"/> on
    /// <see cref="Dispose"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="writer"/> is <see langword="null"/>.</exception>
    public JsonLinesExporter(
        TextWriter writer,
        JsonSerializerOptions? jsonOptions = null,
        Func<DateTime>? nowUtc = null,
        bool leaveOpen = false)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _json = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false };
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        _leaveOpen = leaveOpen;

        // Instantiate the source-generated context (without relying on the Default property).
        _ctx = new NetMetricJsonContext(_json);
        _payloadTypeInfo = _ctx.MetricPayload;
    }

    /// <summary>
    /// Creates a <see cref="JsonLinesExporter"/> that writes to a file in <em>append</em> mode.
    /// </summary>
    /// <param name="path">The target file path. The file is created if it does not exist.</param>
    /// <param name="jsonOptions">Optional JSON options.</param>
    /// <param name="nowUtc">Optional time provider.</param>
    /// <returns>A new <see cref="JsonLinesExporter"/> instance bound to the file.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when the file cannot be opened for append.</exception>
    /// <remarks>
    /// The file is opened with <see cref="FileShare.ReadWrite"/> so that external tailing processes
    /// (e.g., <c>tail -f</c> or log shippers) can read while the exporter writes.
    /// </remarks>
    public static JsonLinesExporter ToFile(
        string path,
        JsonSerializerOptions? jsonOptions = null,
        Func<DateTime>? nowUtc = null)
        => new JsonLinesExporter(
            new StreamWriter(File.Open(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)),
            jsonOptions, nowUtc);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The method iterates the supplied metrics, dispatches each metric value to the corresponding
    /// <see cref="IMetricValueVisitor"/> overload, and finally flushes the underlying writer.
    /// </para>
    /// <para>
    /// ⚠️ <strong>Trimming note:</strong> JSON serialization can require preserved metadata. This
    /// implementation is designed to be trim-safe by using source generation
    /// (<see cref="NetMetricJsonContext"/>). See the <see cref="RequiresUnreferencedCodeAttribute"/>
    /// annotation for details.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("JSON serialization may require preserved types. This implementation uses source generation to be trim-safe.")]
    public async Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        foreach (var metric in metrics)
        {
            ct.ThrowIfCancellationRequested();
            metric?.Accept(this);
        }

        await _writer.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes a <see cref="GaugeValue"/> as a single JSON line.
    /// </summary>
    /// <param name="value">The gauge value.</param>
    /// <param name="metric">The associated metric descriptor.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(GaugeValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        _ = WriteAsync(BuildPayloadRuntime((IGauge)metric,
                new Dictionary<string, object?> { ["value"] = value.Value }));
    }

    /// <summary>
    /// Serializes a <see cref="CounterValue"/> as a single JSON line.
    /// </summary>
    /// <param name="value">The counter value.</param>
    /// <param name="metric">The associated metric descriptor.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(CounterValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        _ = WriteAsync(BuildPayloadRuntime((ICounterMetric)metric,
                new Dictionary<string, object?> { ["value"] = value.Value }));
    }

    /// <summary>
    /// Serializes a <see cref="DistributionValue"/> as a single JSON line.
    /// </summary>
    /// <param name="value">The distribution value.</param>
    /// <param name="metric">The associated metric descriptor.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(DistributionValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        _ = WriteAsync(BuildPayload(metric, new Dictionary<string, object?>
        {
            ["count"] = value.Count,
            ["min"] = value.Min,
            ["p50"] = value.P50,
            ["p90"] = value.P90,
            ["p99"] = value.P99,
            ["max"] = value.Max,
        }));
    }

    /// <summary>
    /// Serializes a <see cref="SummaryValue"/> as a single JSON line.
    /// </summary>
    /// <param name="value">The summary value.</param>
    /// <param name="metric">The associated metric descriptor.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(SummaryValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        _ = WriteAsync(BuildPayload(metric, new Dictionary<string, object?>
        {
            ["count"] = value.Count,
            ["min"] = value.Min,
            ["max"] = value.Max,
            ["quantiles"] = value.Quantiles
        }));
    }

    /// <summary>
    /// Serializes a <see cref="BucketHistogramValue"/> as a single JSON line.
    /// </summary>
    /// <param name="value">The bucketed histogram value.</param>
    /// <param name="metric">The associated metric descriptor.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(BucketHistogramValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        _ = WriteAsync(BuildPayload(metric, new Dictionary<string, object?>
        {
            ["count"] = value.Count,
            ["min"] = value.Min,
            ["max"] = value.Max,
            ["buckets"] = value.Buckets,
            ["counts"] = value.Counts,
            ["sum"] = value.Sum
        }));
    }

    /// <summary>
    /// Serializes a <see cref="MultiSampleValue"/> as a single JSON line.
    /// </summary>
    /// <param name="value">The multi-sample value.</param>
    /// <param name="metric">The associated metric descriptor.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(MultiSampleValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        _ = WriteAsync(BuildPayload(metric, new Dictionary<string, object?>
        {
            ["items"] = value.Items
        }));
    }

    /// <summary>
    /// Serializes an unrecognized <see cref="MetricValue"/> by emitting the runtime type name.
    /// </summary>
    /// <param name="value">The metric value.</param>
    /// <param name="metric">The associated metric descriptor.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="metric"/> is <see langword="null"/>.
    /// </exception>
    public void Visit(MetricValue value, IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metric);

        _ = WriteAsync(BuildPayload(metric, new Dictionary<string, object?>
        {
            ["unknown"] = value.GetType().Name
        }));
    }

    /// <summary>
    /// Builds the common JSON payload for a metric line and merges it with metric-specific fields.
    /// </summary>
    /// <typeparam name="TMetric">The concrete metric type. Its public properties must be preserved under trimming.</typeparam>
    /// <param name="metric">The metric definition (id, name, tags, and optional metadata).</param>
    /// <param name="extra">Metric-specific fields to include.</param>
    /// <returns>A fully populated <see cref="MetricPayload"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="metric"/> or <paramref name="extra"/> is <see langword="null"/>.
    /// </exception>
    private MetricPayload BuildPayload<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    TMetric
    >(
        TMetric metric,
        IReadOnlyDictionary<string, object?> extra
    ) where TMetric : IMetric
    {
        var meta = MetricIntrospection.ReadMeta(metric);

        return new MetricPayload(
            ts: _nowUtc().ToString("O"),
            id: metric.Id,
            name: metric.Name,
            kind: meta.Kind,
            unit: meta.Unit,
            desc: meta.Description,
            tags: metric.Tags,
            extra: extra
        );
    }

    /// <summary>
    /// Helper that preserves runtime type information for trimming when building payloads.
    /// </summary>
    private MetricPayload BuildPayloadRuntime
        <[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TMetric>
        (TMetric metric, IReadOnlyDictionary<string, object?> extra) where TMetric : IMetric
            => BuildPayload<TMetric>(metric, extra);

    /// <summary>
    /// Serializes the given payload as a single line and writes it to the underlying writer.
    /// </summary>
    /// <param name="payload">The metric payload to serialize.</param>
    /// <returns>A task that completes when the line is written.</returns>
    private Task WriteAsync(MetricPayload payload)
    {
        // Trim/AOT-safe serialization via source-generated type info.
        string line = JsonSerializer.Serialize(payload, _payloadTypeInfo);
        return _writer.WriteLineAsync(line);
    }

    /// <summary>
    /// Releases resources used by the exporter.
    /// </summary>
    /// <remarks>
    /// If constructed with <c>leaveOpen</c> set to <see langword="true"/> in the constructor,
    /// the underlying <see cref="TextWriter"/> is <em>not</em> disposed.
    /// </remarks>
    public void Dispose()
    {
        if (!_leaveOpen)
            _writer.Dispose();
    }
}
