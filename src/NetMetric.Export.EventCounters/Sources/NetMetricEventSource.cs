// <copyright file="NetMetricEventSource.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.EventCounters.Sources;

/// <summary>
/// Provides a reusable <see cref="EventSource"/> that dynamically creates and caches
/// <see cref="EventCounter"/> and <see cref="IncrementingEventCounter"/> instances at runtime.
/// </summary>
/// <remarks>
/// <para>
/// This type exists to simplify publishing lightweight, observable runtime metrics via the
/// .NET <see cref="EventSource"/>/<see cref="EventCounter"/> infrastructure. It lazily creates
/// counters on first use and caches them for subsequent writes, avoiding repeated allocations.
/// </para>
/// <para>
/// <b>Naming and sanitization.</b> Counter names are passed through <see cref="Sanitize(string)"/>
/// to comply with <see cref="EventSource"/> naming rules (alphanumeric and underscore). If a raw name
/// is <see langword="null"/> or whitespace, <c>"nm_unknown"</c> is used. Names are truncated to 200 characters.
/// </para>
/// <para>
/// <b>Dimensions and tags.</b> By default, high-cardinality dimensions (e.g., tags) SHOULD NOT be embedded
/// in counter names because they multiply the number of distinct counters and can degrade performance and UX
/// in tools like <c>dotnet-counters</c>. If needed, such behavior can be enabled by the calling layer
/// (e.g., via an <c>EventCountersExporterOptions.IncludeTagsInCounterNames</c> setting) by pre-concatenating
/// the desired suffix before calling <see cref="WriteGauge(string,double,string?)"/> or
/// <see cref="WriteIncrement(string,double,string?)"/> and then passing the result through
/// <see cref="Sanitize(string)"/>.
/// </para>
/// <para>
/// <b>Thread-safety.</b> All public members are safe for concurrent use. Internal state is managed with
/// <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </para>
/// <para>
/// <b>Disposal.</b> Disposing this instance disposes all created <see cref="EventCounter"/> and
/// <see cref="IncrementingEventCounter"/> objects and then calls the base <see cref="EventSource.Dispose(bool)"/>.
/// After disposal, further writes are not supported.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// using System;
/// using System.Diagnostics.Tracing;
/// using NetMetric.Export.EventCounters.Sources;
///
/// // Create the source with a stable name; tools subscribe to this.
/// using var src = new NetMetricEventSource("NetMetric.Sample");
///
/// // Record a snapshot-style gauge value (e.g., queue depth).
/// src.WriteGauge(NetMetricEventSource.Sanitize("app.queue_depth"), value: 42, displayUnits: "items");
///
/// // Record an incrementing sample (tools display it as a rate per interval).
/// src.WriteIncrement(NetMetricEventSource.Sanitize("http.requests"), delta: 10, displayUnits: "req/s");
///
/// // In a timer callback you might update gauges periodically:
/// var timer = new System.Threading.Timer(_ =>
/// {
///     var cpuMs = GetCpuMillisecondsSinceStart();
///     src.WriteGauge("cpu_ms".Sanitize(), cpuMs, "ms"); // See extension method example below.
/// }, null, dueTime: 0, period: 1000);
///
/// // Helper to demonstrate ergonomic sanitization.
/// public static class NameExtensions
/// {
///     public static string Sanitize(this string s) => NetMetricEventSource.Sanitize(s);
/// }
/// ]]></code>
/// </example>
/// <seealso cref="EventSource"/>
/// <seealso cref="EventCounter"/>
/// <seealso cref="IncrementingEventCounter"/>
internal sealed class NetMetricEventSource : EventSource
{
    /// <summary>
    /// Holds lazily-created gauge counters keyed by sanitized counter name.
    /// </summary>
    private readonly ConcurrentDictionary<string, EventCounter> _gauges = new(StringComparer.Ordinal);

    /// <summary>
    /// Holds lazily-created incrementing counters keyed by sanitized counter name.
    /// </summary>
    private readonly ConcurrentDictionary<string, IncrementingEventCounter> _incr = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="NetMetricEventSource"/> class with the specified name.
    /// </summary>
    /// <param name="name">
    /// The event source name under which counters will be published.
    /// Tools such as <c>dotnet-counters</c> or <c>dotnet-monitor</c> subscribe to this name.
    /// </param>
    /// <remarks>
    /// Choose a stable, descriptive name (e.g., <c>"Company.Product.Subsystem"</c>) and avoid per-instance variability
    /// to prevent proliferation of sources in diagnostic tools.
    /// </remarks>
    public NetMetricEventSource(string name) : base(name) { }

    /// <summary>
    /// Writes or updates a gauge-like <see cref="EventCounter"/> with the specified value.
    /// </summary>
    /// <param name="name">The sanitized counter name. See <see cref="Sanitize(string)"/>.</param>
    /// <param name="value">The numeric value to record (snapshot semantics).</param>
    /// <param name="displayUnits">
    /// Optional display units for visualization (for example, <c>"ms"</c>, <c>"bytes"</c>, <c>"items"</c>).
    /// This affects presentation only; it does not change metric semantics.
    /// </param>
    /// <remarks>
    /// <para>
    /// If the named counter does not exist, it is created and cached. Subsequent calls reuse the same instance.
    /// </para>
    /// <para>
    /// <b>Performance note:</b> <see cref="EventCounter.WriteMetric(float)"/> batches and emits on an interval configured
    /// by the listener. Frequent writes are acceptable, but avoid redundant high-frequency updates when values do not change.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown if the underlying <see cref="EventSource"/> has been disposed.</exception>
    public void WriteGauge(string name, double value, string? displayUnits = null)
    {
        var c = _gauges.GetOrAdd(name, static (n, self) =>
        {
            var ec = new EventCounter(n, self);
            return ec;
        }, this);

        if (!string.IsNullOrWhiteSpace(displayUnits))
        {
            c.DisplayUnits = displayUnits;
        }

        c.WriteMetric(value);
    }

    /// <summary>
    /// Writes an incrementing counter sample to an <see cref="IncrementingEventCounter"/>.
    /// </summary>
    /// <param name="name">The sanitized counter name. See <see cref="Sanitize(string)"/>.</param>
    /// <param name="delta">
    /// The increment amount since the last sample. Diagnostic tools typically display this as a rate per interval.
    /// </param>
    /// <param name="displayUnits">
    /// Optional display units for visualization (for example, <c>"req/s"</c>, <c>"bytes/s"</c>).
    /// </param>
    /// <remarks>
    /// <para>
    /// If the named counter does not exist, it is created and cached. Subsequent calls reuse the same instance.
    /// </para>
    /// <para>
    /// <b>Semantics:</b> <see cref="IncrementingEventCounter"/> represents a delta sampled on the listener's interval.
    /// Do not pass a cumulative total; pass the increment since the last call or since the previous sampling tick.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown if the underlying <see cref="EventSource"/> has been disposed.</exception>
    public void WriteIncrement(string name, double delta, string? displayUnits = null)
    {
        var c = _incr.GetOrAdd(name, static (n, self) =>
        {
            var ic = new IncrementingEventCounter(n, self);
            return ic;
        }, this);

        if (!string.IsNullOrWhiteSpace(displayUnits))
        {
            c.DisplayUnits = displayUnits;
        }

        // IncrementingEventCounter records deltas; tools interpret as per-interval rate.
        c.Increment(delta);
    }

    /// <summary>
    /// Disposes all cached <see cref="EventCounter"/> and <see cref="IncrementingEventCounter"/> instances,
    /// then disposes the underlying <see cref="EventSource"/>.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to release managed resources in addition to unmanaged resources; otherwise, <see langword="false"/>.
    /// </param>
    /// <remarks>
    /// After disposal, further write attempts can throw <see cref="ObjectDisposedException"/> from the underlying infrastructure.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var g in _gauges.Values)
            {
                g.Dispose();
            }

            foreach (var i in _incr.Values)
            {
                i.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Sanitizes an identifier string for safe use as an <see cref="EventCounter"/> name.
    /// </summary>
    /// <param name="s">The raw identifier.</param>
    /// <returns>
    /// A string containing only alphanumeric characters and underscores. If the input is
    /// <see langword="null"/> or whitespace, returns <c>"nm_unknown"</c>. The result is truncated to 200 characters.
    /// </returns>
    /// <remarks>
    /// Use this method for any dynamic or user-provided metric identifiers to ensure compatibility with
    /// <see cref="EventSource"/> naming rules.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "nm_unknown";
        }

        Span<char> buf = stackalloc char[Math.Min(s.Length, 200)];
        var j = 0;

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
}
