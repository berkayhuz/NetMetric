// <copyright file="QuicMetricNames.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Quic.Internal;

/// <summary>
/// Provides the canonical metric name constants used by the QUIC instrumentation in NetMetric.
/// </summary>
/// <remarks>
/// <para>
/// These names are emitted by <see cref="NetMetric.Quic.Diagnostics.QuicEventListener"/> and recorded
/// through <see cref="NetMetric.Quic.Internal.QuicMetricSet"/>. The names are stable and follow a
/// dotted, lowercase convention with units encoded where appropriate (for example, <c>.ms</c> for milliseconds).
/// </para>
/// <para>
/// Unless explicitly stated otherwise, metrics carry no tags beyond what the sink backend attaches.
/// For fallback (unknown) EventCounters, additional tags are added; see
/// <see cref="QuicTagKeys"/> for the tag keys.
/// </para>
/// <example>
/// The following example shows a custom exporter observing the smoothed RTT gauge:
/// <code language="csharp"><![CDATA[
/// public sealed class MyExporter
/// {
///     private readonly IMetricRegistry _registry;
///
///     public MyExporter(IMetricRegistry registry)
///         => _registry = registry;
///
///     public void Observe()
///     {
///         // Read latest QUIC smoothed RTT (ms) as a gauge value from your registry
///         var rttGauge = _registry.TryGetGauge(NetMetric.Quic.Internal.QuicMetricNames.RttSmoothed);
///         if (rttGauge is not null)
///         {
///             var value = rttGauge.GetValue();
///             Console.WriteLine($"Smoothed RTT: {value} ms");
///         }
///     }
/// }
/// ]]></code>
/// </example>
/// </remarks>
internal static class QuicMetricNames
{
    /// <summary>
    /// Smoothed round-trip time in milliseconds.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Gauge</description></item>
    /// <item><description><b>Unit:</b> milliseconds (<c>ms</c>)</description></item>
    /// </list>
    /// </remarks>
    public const string RttSmoothed = "quic.rtt.smoothed.ms";

    /// <summary>
    /// Congestion window size in bytes.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Gauge</description></item>
    /// <item><description><b>Unit:</b> bytes</description></item>
    /// </list>
    /// </remarks>
    public const string CwndBytes = "quic.cwnd.bytes";

    /// <summary>
    /// Current number of bytes in flight (unacknowledged).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Gauge</description></item>
    /// <item><description><b>Unit:</b> bytes</description></item>
    /// </list>
    /// </remarks>
    public const string BytesInFlight = "quic.bytes_in_flight";

    /// <summary>
    /// Total number of QUIC packets lost since process start.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Counter (monotonic)</description></item>
    /// <item><description><b>Unit:</b> count</description></item>
    /// </list>
    /// </remarks>
    public const string PacketsLost = "quic.packets.lost.total";

    /// <summary>
    /// Total number of QUIC datagrams sent since process start.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Counter (monotonic)</description></item>
    /// <item><description><b>Unit:</b> count</description></item>
    /// </list>
    /// </remarks>
    public const string DatagramsSent = "quic.datagrams.sent.total";

    /// <summary>
    /// Total number of QUIC datagrams received since process start.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Counter (monotonic)</description></item>
    /// <item><description><b>Unit:</b> count</description></item>
    /// </list>
    /// </remarks>
    public const string DatagramsRecv = "quic.datagrams.recv.total";

    /// <summary>
    /// Total number of QUIC streams opened since process start.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Counter (monotonic)</description></item>
    /// <item><description><b>Unit:</b> count</description></item>
    /// </list>
    /// </remarks>
    public const string StreamsOpened = "quic.streams.opened.total";

    /// <summary>
    /// Total number of QUIC connection-level errors observed since process start.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Counter (monotonic)</description></item>
    /// <item><description><b>Unit:</b> count</description></item>
    /// </list>
    /// </remarks>
    public const string ConnErrors = "quic.connection.errors.total";

    /// <summary>
    /// Name prefix used when publishing unmapped EventCounters as multi-gauge series.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fallback EventCounters are published by <see cref="QuicMetricSet.PublishEventCounter(string, string, string, double)"/>
    /// when <c><see cref="QuicOptions.EnableFallback"/> == true</c>. Each time series carries the tags:
    /// <list type="bullet">
    /// <item><description><c><see cref="QuicTagKeys.Source"/></c> – the <c>EventSource.Name</c> (e.g., <c>MsQuic</c>).</description></item>
    /// <item><description><c><see cref="QuicTagKeys.Name"/></c> – the raw counter name as emitted by the provider.</description></item>
    /// <item><description><c><see cref="QuicTagKeys.Unit"/></c> – the unit of the value (e.g., <c>ms</c>, <c>bytes</c>).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Backends that support multi-gauge semantics will expose one logical instrument
    /// with multiple sibling series keyed by the above tags.
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Multi-gauge</description></item>
    /// </list>
    /// </remarks>
    public const string EventCounterGauge = "quic.eventcounter.gauge";

    /// <summary>
    /// Indicates whether the QUIC event listener is currently active.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Gauge</description></item>
    /// <item><description><b>Unit:</b> 0 or 1</description></item>
    /// </list>
    /// The value is set by <see cref="QuicMetricSet.SetListenerActive(bool)"/> when
    /// <see cref="NetMetric.Quic.Diagnostics.QuicEventListener"/> is enabled/disabled.
    /// </remarks>
    public const string ListenerActive = "quic.listener.active";

    /// <summary>
    /// Total number of unknown EventCounters encountered (i.e., not mapped to a known QUIC metric).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Counter (monotonic)</description></item>
    /// <item><description><b>Unit:</b> count</description></item>
    /// </list>
    /// Incremented by <see cref="QuicMetricSet.IncUnknownCounter"/> when
    /// <see cref="NetMetric.Quic.Diagnostics.QuicEventListener"/> cannot map a counter to a known metric.
    /// </remarks>
    public const string UnknownCountersTotal = "quic.unknown_counters.total";

    /// <summary>
    /// Total number of fallback EventCounters dropped due to cardinality limits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see cref="QuicOptions.EnableFallback"/> is true, fallback EventCounters are published
    /// up to <see cref="QuicOptions.MaxFallbackSeries"/> unique series. Any further series are dropped
    /// and counted here.
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Type:</b> Counter (monotonic)</description></item>
    /// <item><description><b>Unit:</b> count</description></item>
    /// </list>
    /// </remarks>
    public const string FallbackDroppedTotal = "quic.fallback.dropped.total";
}
