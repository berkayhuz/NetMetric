// <copyright file="KestrelEventListener.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kestrel.Diagnostics;

/// <summary>
/// Best-effort <see cref="EventListener"/> for Kestrel (<c>"Microsoft-AspNetCore-Server-Kestrel"</c>)
/// and TLS (<c>"System.Net.Security"</c>) <see cref="EventSource"/>s. It maps commonly used event
/// and counter names to <see cref="Internal.KestrelMetricSet"/>. Unknown or unsupported events are
/// ignored to keep the hot path allocation- and CPU-friendly.
/// </summary>
/// <remarks>
/// <para>
/// Design goals:
/// <list type="bullet">
///   <item><description><strong>Safety</strong>: never throw from event callbacks; swallow benign errors.</description></item>
///   <item><description><strong>Compatibility</strong>: tolerate missing/renamed events across framework versions.</description></item>
///   <item><description><strong>Performance</strong>: use lightweight string checks (no regex) on hot paths.</description></item>
/// </list>
/// </para>
/// <para>
/// Event routing:
/// <list type="bullet">
///   <item><description>Kestrel events (source <c>"Microsoft-AspNetCore-Server-Kestrel"</c>) are handled by <see cref="HandleKestrelEvent(EventWrittenEventArgs)"/>.</description></item>
///   <item><description>TLS events (source <c>"System.Net.Security"</c>) are handled by <see cref="HandleTlsEvent(EventWrittenEventArgs)"/>.</description></item>
/// </list>
/// </para>
/// <para>
/// Thread safety:
/// <see cref="OnEventSourceCreated(EventSource)"/> and <see cref="OnEventWritten(EventWrittenEventArgs)"/> may be invoked
/// concurrently by the eventing infrastructure. This type uses thread-safe collections to coordinate state.
/// </para>
/// </remarks>
/// <example>
/// <para>
/// Registering the listener via the hosted service (recommended):
/// </para>
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using Microsoft.Extensions.Hosting;
/// using NetMetric.Kestrel.Hosting;
///
/// var builder = Host.CreateApplicationBuilder(args);
/// // Inside AddNetMetricKestrel() you would register KestrelMetricSet and KestrelMetricsHostedService
/// // which owns a KestrelEventListener for the app lifetime.
/// builder.Services.AddNetMetricKestrel();
///
/// await builder.Build().RunAsync();
/// ]]></code>
/// <para>
/// Manually creating a listener (advanced / testing):
/// </para>
/// <code language="csharp"><![CDATA[
/// var set = new KestrelMetricSet(factory, tlsBuckets);
/// using var listener = new KestrelEventListener(set);
/// // The listener auto-enables when Kestrel/TLS sources appear.
/// // Dispose when finished to disable sources and stop receiving events.
/// ]]></code>
/// </example>
internal sealed class KestrelEventListener : EventListener
{
    /// <summary>
    /// Target metric sink used to record connection/stream counters and TLS timing.
    /// </summary>
    private readonly KestrelMetricSet _metrics;

    /// <summary>
    /// Tracks <see cref="EventSource"/>s enabled by this listener so they can be disabled during <see cref="Dispose"/>.
    /// Keyed by <see cref="EventSource.Guid"/>.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, EventSource> _enabledSources = new();

    /// <summary>
    /// Stack of TLS handshake start timestamps (<see cref="DateTime.Ticks"/>).
    /// Used to safely pair <c>HandshakeStart</c>/<c>HandshakeStop</c> across threads.
    /// </summary>
    private readonly ConcurrentStack<long> _tlsStarts = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="KestrelEventListener"/> class.
    /// </summary>
    /// <param name="metrics">Metric set to which derived measurements will be reported.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metrics"/> is <see langword="null"/>.</exception>
    public KestrelEventListener(KestrelMetricSet metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <summary>
    /// Enables events for Kestrel and TLS sources as they appear.
    /// </summary>
    /// <param name="eventSource">Newly created <see cref="EventSource"/>.</param>
    /// <remarks>
    /// When the source name is <c>Microsoft-AspNetCore-Server-Kestrel</c> or <c>System.Net.Security</c>,
    /// events are enabled with <see cref="EventLevel.Informational"/> and <see cref="EventKeywords.All"/>.
    /// Enabled sources are remembered for later cleanup.
    /// </remarks>
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        ArgumentNullException.ThrowIfNull(eventSource);

        var name = eventSource.Name;

        if (string.Equals(name, "Microsoft-AspNetCore-Server-Kestrel", StringComparison.Ordinal) ||
            string.Equals(name, "System.Net.Security", StringComparison.Ordinal))
        {
            EnableEvents(eventSource, EventLevel.Informational, EventKeywords.All);
            _enabledSources.TryAdd(eventSource.Guid, eventSource);
        }

        base.OnEventSourceCreated(eventSource);
    }

    /// <summary>
    /// Dispatches incoming events to the appropriate handler based on the source.
    /// </summary>
    /// <param name="eventData">Event data.</param>
    /// <remarks>
    /// Any exception is caught and logged at Debug level to ensure the listener never
    /// throws on the eventing thread.
    /// </remarks>
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        try
        {
            var source = eventData.EventSource?.Name;
            if (source == "Microsoft-AspNetCore-Server-Kestrel")
            {
                HandleKestrelEvent(eventData);
            }
            else if (source == "System.Net.Security")
            {
                HandleTlsEvent(eventData);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {

        }
    }

    /// <summary>
    /// Handles Kestrel events and updates connection/stream/error metrics.
    /// </summary>
    /// <param name="eventData">Event data originating from Kestrel.</param>
    /// <remarks>
    /// Classification (by <see cref="EventWrittenEventArgs.EventName"/>):
    /// <list type="bullet">
    /// <item><description><c>*ConnectionStart</c>/<c>*ConnectionStop</c> → connection counters (by protocol/transport).</description></item>
    /// <item><description><c>Reset</c>, <c>KeepAliveTimeout</c> → specific counters.</description></item>
    /// <item><description><c>Http2Stream*</c>, <c>Http3Stream*</c> → stream counters.</description></item>
    /// <item><description><c>BadRequest</c>, <c>ApplicationError</c> → error categorization.</description></item>
    /// </list>
    /// Protocol is inferred by name contains <c>Http2</c>/<c>Http3</c>; transport is <c>quic</c> for HTTP/3, otherwise <c>tcp</c>.
    /// Unknown names are ignored to minimize overhead.
    /// </remarks>
    private void HandleKestrelEvent(EventWrittenEventArgs eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var name = eventData.EventName ?? string.Empty;

        bool isH2 = name.Contains("Http2", StringComparison.OrdinalIgnoreCase);
        bool isH3 = !isH2 && name.Contains("Http3", StringComparison.OrdinalIgnoreCase);
        string protocol = isH2 ? "h2" : isH3 ? "h3" : "h1";
        string transport = isH3 ? "quic" : "tcp";

        if (name.EndsWith("ConnectionStart", StringComparison.OrdinalIgnoreCase))
        {
            _metrics.IncConnection(protocol, transport);
            if (isH3) _metrics.H3ConnectionStart();
            return;
        }
        if (name.EndsWith("ConnectionStop", StringComparison.OrdinalIgnoreCase))
        {
            _metrics.DecConnection(protocol, transport);
            if (isH3) _metrics.H3ConnectionStop();
            return;
        }
        if (name.Contains("Reset", StringComparison.OrdinalIgnoreCase))
        {
            _metrics.DecConnection(protocol, transport, reason: KestrelReasonValues.Reset);
            return;
        }
        if (name.Contains("KeepAliveTimeout", StringComparison.OrdinalIgnoreCase))
        {
            _metrics.KeepAliveTimeout();
            return;
        }

        if (name.Equals("Http2StreamStart", StringComparison.OrdinalIgnoreCase)) { _metrics.H2StreamStart(); return; }
        if (name.Equals("Http2StreamStop", StringComparison.OrdinalIgnoreCase)) { _metrics.H2StreamStop(); return; }
        if (name.Equals("Http3StreamStart", StringComparison.OrdinalIgnoreCase)) { _metrics.H3StreamStart(); return; }
        if (name.Equals("Http3StreamStop", StringComparison.OrdinalIgnoreCase)) { _metrics.H3StreamStop(); return; }

        if (name.Contains("BadRequest", StringComparison.OrdinalIgnoreCase)) { _metrics.Error(KestrelReasonValues.BadRequest); return; }
        if (name.Contains("ApplicationError", StringComparison.OrdinalIgnoreCase)) { _metrics.Error(KestrelReasonValues.ApplicationError); return; }
    }

    /// <summary>
    /// Handles TLS handshake events from <c>System.Net.Security</c> and observes handshake latency.
    /// </summary>
    /// <param name="eventData">Event data originating from TLS.</param>
    /// <remarks>
    /// Primary path pairs <c>HandshakeStart</c>/<c>HandshakeStop</c> via a thread-safe stack using UTC ticks.
    /// If pairing fails, it attempts to read a duration payload (e.g., <c>ElapsedMilliseconds</c> or <c>Duration</c>).
    /// Invalid or implausible values are skipped to avoid contaminating metrics.
    /// </remarks>
    private void HandleTlsEvent(EventWrittenEventArgs eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var name = eventData.EventName ?? string.Empty;

        if (name.Equals("HandshakeStart", StringComparison.OrdinalIgnoreCase))
        {
            _tlsStarts.Push(DateTime.UtcNow.Ticks);
            return;
        }

        if (name.Equals("HandshakeStop", StringComparison.OrdinalIgnoreCase))
        {
            if (_tlsStarts.TryPop(out long startTicks))
            {
                double ms = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - startTicks).TotalMilliseconds;
                ObserveTlsSafe(ms, "pop");
                return;
            }

            var dur = TryGetPayloadDouble(eventData, "ElapsedMilliseconds")
                      ?? TryGetPayloadDouble(eventData, "Duration")
                      ?? -1;
            if (dur >= 0)
            {
                ObserveTlsSafe(dur, "payload");
            }
        }
    }

    /// <summary>
    /// Validates and records TLS handshake duration, discarding non-finite or out-of-range values.
    /// </summary>
    /// <param name="ms">Measured duration in milliseconds.</param>
    /// <param name="source">Source of the measurement (e.g., <c>"pop"</c> or <c>"payload"</c>).</param>
    private void ObserveTlsSafe(double ms, string source)
    {
        _ = source;

        if (double.IsFinite(ms) && ms >= 0 && ms <= TimeSpan.FromMinutes(5).TotalMilliseconds)
        {
            _metrics.ObserveTlsHandshake(ms);
        }
    }

    /// <summary>
    /// Attempts to read a numeric payload value with the specified key and convert it to <see cref="double"/>.
    /// </summary>
    /// <param name="eventData">Event containing payload.</param>
    /// <param name="key">Payload field name to search (case-insensitive).</param>
    /// <returns>
    /// Parsed <see cref="double"/> value when available; otherwise <see langword="null"/>.
    /// </returns>
    private static double? TryGetPayloadDouble(EventWrittenEventArgs eventData, string key)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.PayloadNames is null || eventData.Payload is null) return null;

        for (int i = 0; i < eventData.PayloadNames.Count; i++)
        {
            if (string.Equals(eventData.PayloadNames[i], key, StringComparison.OrdinalIgnoreCase) &&
                eventData.Payload[i] is IConvertible conv)
            {
                try
                {
                    return conv.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (FormatException) { return null; }
                catch (InvalidCastException) { return null; }
                catch (OverflowException) { return null; }
            }
        }
        return null;
    }

    /// <summary>
    /// Disables all previously enabled sources and disposes the base listener.
    /// </summary>
    /// <remarks>
    /// All exceptions during cleanup are swallowed to preserve process stability.
    /// Prefer disposing via the hosting service on orderly shutdown.
    /// </remarks>
    public new void Dispose()
    {
        try
        {
            foreach (var src in _enabledSources.Values)
            {
                try
                {
                    DisableEvents(src);
                }
                catch (ObjectDisposedException) { /* best-effort */ }
                catch (InvalidOperationException) { /* best-effort */ }
            }
            _enabledSources.Clear();
        }
        catch (ObjectDisposedException) { /* best-effort */ }
        catch (InvalidOperationException) { /* best-effort */ }
        finally
        {
            try
            {
                base.Dispose();
            }
            catch (ObjectDisposedException) { /* best-effort */ }
            catch (InvalidOperationException) { /* best-effort */ }
        }
    }
}
