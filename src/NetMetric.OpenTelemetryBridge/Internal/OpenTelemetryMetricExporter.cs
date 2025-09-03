// <copyright file="OpenTelemetryMetricExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.OpenTelemetryBridge.Internal;

/// <summary>
/// Bridges NetMetric metrics to OpenTelemetry by creating and updating instruments
/// on a <see cref="System.Diagnostics.Metrics.Meter"/>.
/// </summary>
/// <remarks>
/// <para>
/// This exporter translates NetMetric primitives to OpenTelemetry instruments and measurements:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><see cref="GaugeValue"/> → <c>ObservableGauge&lt;double&gt;</c></description>
///   </item>
///   <item>
///     <description><see cref="CounterValue"/> → <c>Counter&lt;long&gt;</c></description>
///   </item>
///   <item>
///     <description><see cref="BucketHistogramValue"/> → <c>Histogram&lt;double&gt;</c> (midpoint sampling)</description>
///   </item>
///   <item>
///     <description><see cref="MultiSampleValue"/> → multiple series distinguished by extra attributes</description>
///   </item>
/// </list>
/// <para>
/// <see cref="DistributionValue"/> and <see cref="SummaryValue"/> are currently ignored. They may be
/// mapped to histograms (e.g., using quantile attributes) in future versions.
/// </para>
/// <para>
/// Naming, attribute mapping, series limits, and error handling are controlled by
/// <see cref="OpenTelemetryBridgeOptions"/>.
/// </para>
/// </remarks>
/// <threadsafety>
/// <para>
/// Instances are safe to use from multiple threads concurrently. Internal state is maintained with
/// concurrent collections, and the callback for observable gauges reads from those thread-safe structures.
/// </para>
/// <para>
/// Create at most one instance per OpenTelemetry <see cref="System.Diagnostics.Metrics.Meter"/> namespace/version
/// pair to avoid duplicate instruments.
/// </para>
/// </threadsafety>
/// <example>
/// <code><![CDATA[
/// using NetMetric.OpenTelemetryBridge.Configurations;
/// using NetMetric.OpenTelemetryBridge.Internal;
///
/// var opts = new OpenTelemetryBridgeOptions
/// {
///     MeterName = "NetMetric.Bridge",
///     MeterVersion = "1.0.0",
///     SanitizeMetricNames = true,
///     AttributeMapper = OpenTelemetryAttributeMapper.Default
/// };
///
/// using var exporter = new OpenTelemetryMetricExporter(opts);
///
/// // Gather or build a batch of IMetric instances from your pipeline:
/// IEnumerable<IMetric> batch = GetMetrics();
///
/// // Export (non-blocking, completes synchronously in this implementation):
/// await exporter.ExportAsync(batch, ct);
/// ]]></code>
/// </example>
/// <seealso cref="OpenTelemetryBridgeOptions"/>
/// <seealso cref="System.Diagnostics.Metrics.Meter"/>
internal sealed class OpenTelemetryMetricExporter : IMetricExporter, IDisposable
{
    private readonly Meter _meter;
    private readonly OpenTelemetryBridgeOptions _opts;

    private readonly ConcurrentDictionary<string, Counter<long>> _counters = new();
    private readonly ConcurrentDictionary<string, Histogram<double>> _histos = new();

    // Gauge: id -> (seriesKey -> (value, tags[]))
    private readonly ConcurrentDictionary<string,
        ConcurrentDictionary<string, (double val, KeyValuePair<string, object?>[] tags)>> _gauges = new();

    private readonly ConcurrentDictionary<string, bool> _gaugeRegistered = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenTelemetryMetricExporter"/> class.
    /// </summary>
    /// <param name="opts">
    /// Exporter options controlling meter identity, metric name sanitization, attribute mapping, series limits,
    /// and error reporting. If <paramref name="opts"/> is <see langword="null"/>, defaults are applied.
    /// </param>
    /// <remarks>
    /// A new <see cref="System.Diagnostics.Metrics.Meter"/> is created using
    /// <see cref="OpenTelemetryBridgeOptions.MeterName"/> and <see cref="OpenTelemetryBridgeOptions.MeterVersion"/>.
    /// </remarks>
    public OpenTelemetryMetricExporter(OpenTelemetryBridgeOptions opts)
    {
        _opts = opts ?? new();
        _meter = new Meter(_opts.MeterName, _opts.MeterVersion);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// This method completes synchronously for the current implementation (it iterates the input and records
    /// measurements or updates observable-gauge state).
    /// </para>
    /// <para>
    /// Trimming note: The interface <c>IMetricExporter.ExportAsync</c> is marked with
    /// <see cref="RequiresUnreferencedCodeAttribute"/>, and this implementation preserves that requirement.
    /// The exporter itself avoids reflection; however, callers supplying model types that rely on reflection
    /// should ensure appropriate trimming annotations.
    /// </para>
    /// <para>
    /// Error handling: invalid metric names, attribute violations, or instrument lifecycle issues are caught
    /// and forwarded to <see cref="OpenTelemetryBridgeOptions.OnExportError"/>. Export continues for remaining metrics.
    /// </para>
    /// </remarks>
    /// <param name="metrics">The metrics to export. Must not be <see langword="null"/>.</param>
    /// <param name="ct">A cancellation token used to cooperatively cancel the export loop.</param>
    /// <returns>
    /// A task representing the export operation. The task is already completed when returned.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="metrics"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is signaled during iteration.</exception>
    [RequiresUnreferencedCode("IMetricExporter.ExportAsync is marked RequiresUnreferencedCode on the interface; implementation must match.")]
    public Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        foreach (var m in metrics)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var tags = ToOtelTags(_opts.AttributeMapper.MapTags(m.Tags));
                var id = m.Id;
                var name = MetricName(m.Name);

                switch (m.GetValue())
                {
                    case GaugeValue g:
                        PublishGauge(id, name, g.Value, tags);
                        break;

                    case CounterValue c:
                        var counter = _counters.GetOrAdd(id, _ => _meter.CreateCounter<long>(name));
                        counter.Add(c.Value, tags);
                        break;

                    case BucketHistogramValue bh:
                        {
                            var h = _histos.GetOrAdd(id, _ => _meter.CreateHistogram<double>(name));
                            // Approximate distribution via midpoint sampling (deterministic down-sample).
                            for (int i = 0; i < bh.Buckets.Count && i < bh.Counts.Count; i++)
                            {
                                var count = bh.Counts[i];
                                if (count <= 0) continue;

                                var value = i == 0 ? bh.Buckets[i]
                                                   : (bh.Buckets[i - 1] + bh.Buckets[i]) / 2.0;

                                // Limit per-bucket samples to bound overhead.
                                var n = (int)Math.Clamp(count, 1, 64);
                                for (int k = 0; k < n; k++)
                                    h.Record(value, tags);
                            }
                            break;
                        }

                    case DistributionValue:
                    case SummaryValue:
                        // Not mapped (future work).
                        break;

                    case MultiSampleValue ms:
                        {
                            foreach (var it in ms.Items)
                            {
                                var baseTags = ToOtelTags(_opts.AttributeMapper.MapTags(it.Tags));
                                var itemTags = AppendTags(
                                    baseTags,
                                    new KeyValuePair<string, object?>(_opts.MultiSampleIdKey, it.Id),
                                    new KeyValuePair<string, object?>(_opts.MultiSampleNameKey, it.Name ?? string.Empty));

                                switch (it.Value)
                                {
                                    case GaugeValue gv:
                                        PublishGauge($"{id}:{it.Id}", MetricName(it.Name), gv.Value, itemTags);
                                        break;

                                    case CounterValue cv:
                                        var c2 = _counters.GetOrAdd($"{id}:{it.Id}",
                                            _ => _meter.CreateCounter<long>(MetricName(it.Name)));
                                        c2.Add(cv.Value, itemTags);
                                        break;

                                    case BucketHistogramValue bh2:
                                        var h2 = _histos.GetOrAdd($"{id}:{it.Id}",
                                            _ => _meter.CreateHistogram<double>(MetricName(it.Name)));
                                        if (bh2.Count > 0) h2.Record(bh2.Max, itemTags);
                                        break;
                                }
                            }
                            break;
                        }

                    default:
                        // Unsupported type -> skip.
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Honor cancellation cooperatively.
                throw;
            }
            catch (ArgumentException ex)
            {
                // Name/attribute constraints, etc.
                _opts.OnExportError?.Invoke(ex);
            }
            catch (InvalidOperationException ex)
            {
                // Instrument already disposed / invalid state transitions.
                _opts.OnExportError?.Invoke(ex);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Publishes a gauge value by updating per-series state and ensuring an observable gauge is registered.
    /// </summary>
    /// <param name="id">Metric identifier used to group related gauge series.</param>
    /// <param name="name">Instrument name used when creating the OpenTelemetry observable gauge.</param>
    /// <param name="value">The gauge value to publish.</param>
    /// <param name="tags">Attributes for this measurement. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tags"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Series cardinality is bounded by <see cref="OpenTelemetryBridgeOptions.MaxGaugeSeriesPerId"/>. When the limit is
    /// exceeded, an arbitrary existing series is evicted to make room for a new one.
    /// </para>
    /// <para>
    /// The corresponding <c>ObservableGauge&lt;double&gt;</c> is created on first publication for a given
    /// <paramref name="id"/>. Subsequent publications only update cached series state.
    /// </para>
    /// </remarks>
    private void PublishGauge(string id, string name, double value, KeyValuePair<string, object?>[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var seriesKey = GetSeriesKey(tags);
        var series = _gauges.GetOrAdd(id, _ => new());

        // Cardinality guard: evict one if limit exceeded.
        if (!series.ContainsKey(seriesKey) && series.Count >= _opts.MaxGaugeSeriesPerId)
        {
            foreach (var k in series.Keys) { series.TryRemove(k, out _); break; }
        }

        series[seriesKey] = (value, tags);

        if (_gaugeRegistered.TryAdd(id, true))
        {
            _meter.CreateObservableGauge<double>(
                name,
                () =>
                {
                    if (!_gauges.TryGetValue(id, out var dict) || dict.IsEmpty)
                        return Array.Empty<Measurement<double>>();

                    var list = new List<Measurement<double>>(dict.Count);
                    foreach (var kv in dict.Values)
                        list.Add(new Measurement<double>(kv.val, kv.tags));
                    return list;
                },
                unit: null,
                description: null);
        }
    }

    /// <summary>
    /// Combines base attributes with optional extra attributes into a new array.
    /// </summary>
    /// <param name="baseTags">The base attribute set. Must not be <see langword="null"/>.</param>
    /// <param name="extras">Optional additional attributes to append.</param>
    /// <returns>A new array containing both <paramref name="baseTags"/> and <paramref name="extras"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseTags"/> is <see langword="null"/>.</exception>
    private static KeyValuePair<string, object?>[] AppendTags(
        KeyValuePair<string, object?>[] baseTags,
        params KeyValuePair<string, object?>[]? extras)
    {
        ArgumentNullException.ThrowIfNull(baseTags);
        if (extras is null || extras.Length == 0) return baseTags;

        var arr = new KeyValuePair<string, object?>[baseTags.Length + extras.Length];
        baseTags.CopyTo(arr, 0);
        extras.CopyTo(arr, baseTags.Length);
        return arr;
    }

    /// <summary>
    /// Builds a deterministic key string from attributes to partition gauge series.
    /// </summary>
    /// <param name="tags">The attribute set. Must not be <see langword="null"/>.</param>
    /// <returns>A stable key derived from sorted attribute keys and values; empty if there are no attributes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tags"/> is <see langword="null"/>.</exception>
    private static string GetSeriesKey(KeyValuePair<string, object?>[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return tags.Length == 0
            ? string.Empty
            : string.Join("|",
                tags.OrderBy(t => t.Key, StringComparer.Ordinal)
                    .Select(t => $"{t.Key}={t.Value}"));
    }

    /// <summary>
    /// Converts NetMetric tag pairs to OpenTelemetry attribute pairs.
    /// </summary>
    /// <param name="tags">Source tag pairs (nullable).</param>
    /// <returns>An array of OpenTelemetry attributes (possibly empty).</returns>
    private static KeyValuePair<string, object?>[] ToOtelTags(IEnumerable<KeyValuePair<string, object>>? tags)
        => tags is null
           ? Array.Empty<KeyValuePair<string, object?>>()
           : tags.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value)).ToArray();

    /// <summary>
    /// Normalizes metric names to be OpenTelemetry-friendly, optionally sanitizing invalid characters.
    /// </summary>
    /// <param name="raw">Original metric name (nullable).</param>
    /// <returns>A sanitized metric name suitable for OpenTelemetry instruments.</returns>
    /// <remarks>
    /// When <see cref="OpenTelemetryBridgeOptions.SanitizeMetricNames"/> is <see langword="true"/>, non
    /// <c>[A-Za-z0-9:._]</c> characters are replaced with underscores, and names that do not start with an
    /// alphanumeric character are prefixed with <c>"nm_"</c>.
    /// </remarks>
    private string MetricName(string? raw)
    {
        var input = string.IsNullOrWhiteSpace(raw) ? "nm.metric" : raw.Trim();
        if (!_opts.SanitizeMetricNames) return input;

        var n = Regex.Replace(input, @"[^A-Za-z0-9:._]", "_");
        if (n.Length == 0) n = "nm.metric";
        if (!char.IsLetterOrDigit(n[0])) n = "nm_" + n;
        return n;
        // Note: OTEL SDK also applies its own validation; ArgumentException can still be thrown upstream.
    }

    /// <summary>
    /// Disposes the underlying <see cref="System.Diagnostics.Metrics.Meter"/> and all created instruments.
    /// </summary>
    public void Dispose() => _meter.Dispose();
}
