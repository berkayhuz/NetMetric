// <copyright file="EventCountersExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics.CodeAnalysis;

namespace NetMetric.Export.EventCounters.Exporters;

/// <summary>
/// Publishes NetMetric metrics as .NET <see cref="EventCounter"/> and
/// <see cref="IncrementingEventCounter"/> instances, enabling real-time
/// observation via tools such as <c>dotnet-counters</c>, PerfView, or Visual Studio.
/// </summary>
/// <remarks>
/// <para>
/// <b>Value mapping.</b> Metric value shapes are mapped to EventCounters as follows:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><c>GaugeValue</c> → <see cref="EventCounter"/> (snapshot values).</description>
///   </item>
///   <item>
///     <description><c>CounterValue</c> (cumulative) → <see cref="IncrementingEventCounter"/> (delta is computed).</description>
///   </item>
///   <item>
///     <description><c>DistributionValue</c> / <c>SummaryValue</c> → multiple gauges
///     (e.g., count, min, max, p50/p90/p99 or user-provided quantiles).</description>
///   </item>
///   <item>
///     <description><c>BucketHistogramValue</c> → gauges for count/min/max/sum and optional bucket counts (named as <c>le_*</c>).</description>
///   </item>
///   <item>
///     <description><c>MultiSampleValue</c> → each item is exported as an individual metric by composing identifiers.</description>
///   </item>
/// </list>
/// <para>
/// <b>Lifecycle.</b> The exporter is intended to be long-lived. It maintains internal caches of counters and
/// last-seen counter values to compute deltas. Dispose the instance during application shutdown
/// to release the underlying <see cref="EventSource"/> and associated resources.
/// </para>
/// </remarks>
/// <threadsafety>
/// All write paths rely on thread-safe collections (e.g., <see cref="ConcurrentDictionary{TKey, TValue}"/>).
/// The type is safe for concurrent use from multiple threads.
/// </threadsafety>
/// <example>
/// <code language="csharp"><![CDATA[
/// Create and use the exporter directly (simplified):
/// IMetricExporter exporter = new EventCountersExporter();
/// await exporter.ExportAsync(new[]
/// {
///     Metric.Gauge("app.workers", 12),
///     Metric.Counter("requests.total", 1),
/// });
///
/// // Observe via:
/// //   dotnet-counters monitor --process-id <pid>
/// ]]></code>
/// </example>
/// <seealso cref="EventCounter"/>
/// <seealso cref="IncrementingEventCounter"/>
public sealed class EventCountersExporter : IMetricExporter, IDisposable
{
    /// <summary>
    /// The backing <see cref="EventSource"/> hosting the counters.
    /// </summary>
    private readonly NmEventSource _es = new("NetMetric");

    /// <summary>
    /// Cache of <see cref="EventCounter"/> instances keyed by sanitized metric identifier.
    /// </summary>
    private readonly ConcurrentDictionary<string, EventCounter> _gauges = new(StringComparer.Ordinal);

    /// <summary>
    /// Cache of <see cref="IncrementingEventCounter"/> instances keyed by sanitized metric identifier.
    /// </summary>
    private readonly ConcurrentDictionary<string, IncrementingEventCounter> _incs = new(StringComparer.Ordinal);

    /// <summary>
    /// Last-seen cumulative values for counters; used to compute deltas.
    /// Keyed by raw metric identifier (unsanitized) to avoid collisions across different sanitized names.
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _lastCounter = new(StringComparer.Ordinal);

    /// <summary>
    /// Exports the provided metrics by writing them into EventCounters.
    /// </summary>
    /// <param name="metrics">The metrics to export.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe while the operation executes.</param>
    /// <returns>
    /// A completed task once the metrics have been written to the corresponding EventCounters.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled via <paramref name="ct"/>.</exception>
    /// <remarks>
    /// This method updates counters synchronously. EventCounter emission to tooling is managed by the runtime.
    /// </remarks>
    [RequiresUnreferencedCode("IMetricExporter.ExportAsync may use reflection or members trimmed at AOT; keep members or disable trimming for exporters.")]
    public Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        foreach (var m in metrics)
        {
            ct.ThrowIfCancellationRequested();
            WriteMetric(m);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes all created counters and the underlying <see cref="EventSource"/>.
    /// </summary>
    /// <remarks>
    /// After disposal, the instance should not be used to export further metrics.
    /// </remarks>
    public void Dispose()
    {
        foreach (var g in _gauges.Values)
        {
            g.Dispose();
        }

        foreach (var i in _incs.Values)
        {
            i.Dispose();
        }

        _es.Dispose();
    }

    /// <summary>
    /// Routes a single metric to the appropriate EventCounter(s) based on its runtime value type.
    /// </summary>
    /// <param name="m">The metric to write.</param>
    /// <remarks>
    /// Unknown or <see langword="null"/> values are ignored.
    /// </remarks>
    private void WriteMetric(IMetric m)
    {
        ArgumentNullException.ThrowIfNull(m);

        switch (m.GetValue())
        {
            case GaugeValue g:
                Gauge(m, null).WriteMetric((float)g.Value);
                break;

            case CounterValue c:
                {
                    var delta = ComputeDelta(m.Id, c.Value);
                    if (delta > 0)
                    {
                        // EventCounters represent rates via IncrementingEventCounter.
                        Inc(m, "rate").Increment((float)delta);
                    }
                    break;
                }

            case DistributionValue d:
                Gauge(m, "count").WriteMetric(d.Count);
                Gauge(m, "min").WriteMetric((float)d.Min);
                Gauge(m, "p50").WriteMetric((float)d.P50);
                Gauge(m, "p90").WriteMetric((float)d.P90);
                Gauge(m, "p99").WriteMetric((float)d.P99);
                Gauge(m, "max").WriteMetric((float)d.Max);
                break;

            case SummaryValue s:
                Gauge(m, "count").WriteMetric(s.Count);
                Gauge(m, "min").WriteMetric((float)s.Min);
                Gauge(m, "max").WriteMetric((float)s.Max);
                foreach (var kv in s.Quantiles)
                {
                    Gauge(m, $"q{QuantileKey(kv.Key)}").WriteMetric((float)kv.Value);
                }
                break;

            case BucketHistogramValue h:
                // Cumulative aggregates as gauges.
                Gauge(m, "count").WriteMetric(h.Count);
                Gauge(m, "min").WriteMetric((float)h.Min);
                Gauge(m, "max").WriteMetric((float)h.Max);
                Gauge(m, "sum").WriteMetric((float)h.Sum);

                if (h.Buckets is { Count: > 0 })
                {
                    for (int i = 0; i < h.Buckets.Count; i++)
                    {
                        Gauge(m, $"le_{BucketKey(h.Buckets[i])}").WriteMetric(h.Counts[i]);
                    }
                }
                break;

            case MultiSampleValue ms:
                foreach (var item in ms.Items)
                {
                    // Adapt each item to an IMetric shape and recurse.
                    WriteMetric(new MultiItemAsMetric(item, m));
                }
                break;

            default:
                // Unknown / null → ignore.
                break;
        }
    }

    /// <summary>
    /// Gets (or creates) a gauge-like <see cref="EventCounter"/> for the given metric and optional suffix.
    /// </summary>
    /// <param name="m">The metric for which to retrieve the counter.</param>
    /// <param name="suffix">
    /// An optional qualifier appended to the metric identifier (for example, <c>"min"</c> or <c>"max"</c>).
    /// </param>
    /// <returns>An <see cref="EventCounter"/> instance bound to the underlying <see cref="EventSource"/>.</returns>
    private EventCounter Gauge(IMetric m, string? suffix)
    {
        ArgumentNullException.ThrowIfNull(m);
        var key = suffix is null ? m.Id : $"{m.Id}.{suffix}";
        return _gauges.GetOrAdd(key, _ => new EventCounter(CounterName(m, suffix), _es));
    }

    /// <summary>
    /// Gets (or creates) an <see cref="IncrementingEventCounter"/> for the given metric and optional suffix.
    /// </summary>
    /// <param name="m">The metric for which to retrieve the counter.</param>
    /// <param name="suffix">An optional qualifier, such as <c>"rate"</c>.</param>
    /// <returns>An <see cref="IncrementingEventCounter"/> instance bound to the underlying <see cref="EventSource"/>.</returns>
    private IncrementingEventCounter Inc(IMetric m, string? suffix)
    {
        ArgumentNullException.ThrowIfNull(m);
        var key = suffix is null ? m.Id : $"{m.Id}.{suffix}";
        return _incs.GetOrAdd(key, _ => new IncrementingEventCounter(CounterName(m, suffix), _es));
    }

    /// <summary>
    /// Produces a sanitized EventCounter name for the given metric and optional suffix.
    /// </summary>
    /// <param name="m">The source metric.</param>
    /// <param name="suffix">Optional name suffix.</param>
    /// <returns>A sanitized counter name that is safe for EventSource exposure.</returns>
    private static string CounterName(IMetric m, string? suffix)
    {
        ArgumentNullException.ThrowIfNull(m);
        return suffix is null ? Sanitize(m.Id) : $"{Sanitize(m.Id)}.{Sanitize(suffix)}";
    }

    /// <summary>
    /// Converts a quantile (for example, <c>0.99</c>) into a key segment (for example, <c>"0_99"</c>) for counter naming.
    /// </summary>
    /// <param name="q">The quantile in the inclusive range [0, 1].</param>
    /// <returns>A stable, file-name-safe token representing the quantile.</returns>
    private static string QuantileKey(double q)
    {
        // 0.99 -> "0_99", 0.5 -> "0_5"
        var s = q.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return s.Replace('.', '_').Replace('-', 'm');
    }

    /// <summary>
    /// Converts a histogram upper bound into a key segment for counter naming.
    /// </summary>
    /// <param name="upper">The bucket's inclusive upper bound.</param>
    /// <returns>
    /// <c>"Inf"</c> if <paramref name="upper"/> is <see cref="double.NaN"/> or non-finite; otherwise a sanitized token
    /// (for example, <c>"10_0"</c>).
    /// </returns>
    private static string BucketKey(double upper)
    {
        if (double.IsNaN(upper) || double.IsInfinity(upper))
        {
            return "Inf";
        }

        var s = upper.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return s.Replace('.', '_').Replace('-', 'm');
    }

    /// <summary>
    /// Sanitizes an identifier for safe use as an EventCounter name or segment.
    /// </summary>
    /// <param name="s">The raw identifier.</param>
    /// <returns>
    /// A string containing only letters, digits, and underscores. Empty or whitespace-only inputs yield <c>"nm_unknown"</c>.
    /// </returns>
    private static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "nm_unknown";
        }

        Span<char> buf = stackalloc char[Math.Min(s.Length, 200)];
        int j = 0;

        foreach (var ch in s)
        {
            if (j >= buf.Length)
            {
                break;
            }

            buf[j++] = char.IsLetterOrDigit(ch) ? ch : '_';
        }

        return new string(buf[..j]);
    }

    /// <summary>
    /// Computes the positive delta between a current cumulative counter value and the last recorded value.
    /// </summary>
    /// <param name="id">The metric identifier used as the cache key.</param>
    /// <param name="current">The current cumulative value reported by the metric.</param>
    /// <returns>
    /// The positive delta since the last observation; returns <c>0</c> for the first sample or when the counter resets or wraps.
    /// </returns>
    /// <remarks>
    /// If the counter value decreases (for example, due to process restart or overflow wrap), the delta is treated as <c>0</c>
    /// and the last-seen value is updated to <paramref name="current"/>.
    /// </remarks>
    private long ComputeDelta(string id, long current)
    {
        // On first sample or wrap/decrease, write zero delta.
        long prev = _lastCounter.AddOrUpdate(id, current, (_, old) => current);
        long delta = current - prev;

        if (delta <= 0)
        {
            _lastCounter[id] = current;
            return 0;
        }

        _lastCounter[id] = current;
        return delta;
    }

    /// <summary>
    /// Minimal <see cref="EventSource"/> used solely as a host for EventCounters.
    /// </summary>
    private sealed class NmEventSource : EventSource
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NmEventSource"/> class.
        /// </summary>
        /// <param name="name">The event source name under which counters are exposed.</param>
        public NmEventSource(string name) : base(name) { }
    }

    /// <summary>
    /// Adapts a <c>MultiSampleItem</c> into an <see cref="IMetric"/> shape.
    /// Only <see cref="IMetric.Id"/>, <see cref="IMetric.Name"/>, <see cref="IMetric.Tags"/>, and <see cref="IMetric.GetValue"/> are implemented.
    /// </summary>
    private sealed class MultiItemAsMetric : IMetric
    {
        private readonly MultiSampleItem _item;
        private readonly IMetric _parent;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiItemAsMetric"/> class.
        /// </summary>
        /// <param name="item">The multi-sample item to expose as a metric.</param>
        /// <param name="parent">The parent metric providing base identifiers and naming.</param>
        public MultiItemAsMetric(MultiSampleItem item, IMetric parent)
        {
            _item = item;
            _parent = parent;
        }

        /// <inheritdoc/>
        public string Id => $"{_parent.Id}.{_item.Id}";

        /// <inheritdoc/>
        public string Name => $"{_parent.Name}/{_item.Name}";

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> Tags => _item.Tags;

        /// <inheritdoc/>
        public object? GetValue() => _item.Value;
    }
}
