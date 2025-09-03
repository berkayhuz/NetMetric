// <copyright file="QuicEventListener.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Logging;

namespace NetMetric.Quic.Diagnostics;

/// <summary>
/// Best-effort <see cref="EventListener"/> implementation that subscribes to QUIC-related
/// <c>EventCounters</c> produced by MsQuic / <c>System.Net.Quic</c> providers and forwards them
/// to <see cref="QuicMetricSet"/>.
/// </summary>
/// <remarks>
/// <para>
/// This listener is intentionally conservative and allocation-aware:
/// it enables only providers whitelisted via <see cref="QuicOptions.AllowedProviders"/> and    
/// configures their sampling period by setting the <c>"EventCounterIntervalSec"</c> argument
/// to <see cref="QuicOptions.SamplingIntervalSec"/> when calling <see cref="EventListener.EnableEvents(EventSource, EventLevel, EventKeywords, System.Collections.Generic.IDictionary{string, string})"/>.
/// </para>
/// <para>
/// On each <c>EventCounters</c> payload, the listener extracts common fields
/// (<c>Name</c>, <c>DisplayUnits</c>, <c>CounterType</c>, <c>ActualValue</c>, <c>Mean</c>, <c>Increment</c>)
/// and resolves a single numeric value using <see cref="SelectValue(string, double?, double?, double?)"/>.
/// It then maps well-known counters (RTT, congestion window, bytes-in-flight, loss, datagrams, streams, errors)
/// to strongly-typed methods on <see cref="QuicMetricSet"/> via <see cref="MapKnown(string, string, double, string)"/>.
/// Unknown counters are published through the multi-gauge fallback in <see cref="QuicMetricSet.PublishEventCounter(string, string, string, double)"/>.
/// </para>
/// <para>
/// Error handling is best-effort: common transient exceptions from ETW/event delivery
/// are swallowed to avoid disrupting the process. All callbacks are thread-safe and
/// designed to be re-entrant.
/// </para>
/// <para>
/// <b>Thread safety:</b> provider enable/disable operations are guarded by a private lock to ensure
/// idempotency when multiple <see cref="EventSource"/> instances surface for the same provider.
/// </para>
/// </remarks>
/// <example>
/// Typical usage is to create and hold a single instance for the lifetime of the process or host:
/// <code language="csharp"><![CDATA[
/// var metricSet = new QuicMetricSet(factory, options);
/// using var listener = new QuicEventListener(metricSet, options);
/// // listener stays alive; metrics are emitted into metricSet
/// ]]></code>
/// In ASP.NET Core, prefer wiring through a hosted service (see <c>QuicMetricsHostedService</c>).
/// </example>
internal sealed class QuicEventListener : EventListener
{
    private readonly QuicMetricSet _set;
    private readonly QuicOptions _opt;
    private readonly ILogger? _log;

    private readonly object _sync = new();
    private readonly HashSet<EventSource> _enabledSources = new();

    private long _errCountWindow;
    private long _lastLogUnixSec;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuicEventListener"/> class.
    /// </summary>
    /// <param name="set">The <see cref="QuicMetricSet"/> sink that will be populated with metrics.</param>
    /// <param name="opt">Configuration controlling provider filtering, sampling, and fallback behavior.</param>
    /// <param name="log">Optional logger for internal diagnostic messages (throttled).</param>
    public QuicEventListener(QuicMetricSet set, QuicOptions opt, ILogger<QuicEventListener>? log = null)
    {
        _set = set;
        _opt = opt;
        _log = log;
    }

    /// <summary>
    /// Called by the runtime whenever a new <see cref="EventSource"/> is created.
    /// Enables the source if it is listed in <see cref="QuicOptions.AllowedProviders"/>.
    /// </summary>
    /// <param name="eventSource">The created event source.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="eventSource"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// When enabling, the listener sets <c>"EventCounterIntervalSec"</c> to
    /// <see cref="QuicOptions.SamplingIntervalSec"/> so that counter payloads are delivered periodically.
    /// Multiple invocations for the same source are coalesced.
    /// </remarks>
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        ArgumentNullException.ThrowIfNull(eventSource);

        base.OnEventSourceCreated(eventSource);

        if (!_opt.AllowedProvidersFrozen.Contains(eventSource.Name))
            return;

        lock (_sync)
        {
            if (_enabledSources.Contains(eventSource))
                return;

            EnableEvents(
                eventSource,
                EventLevel.Informational,
                EventKeywords.All,
                new Dictionary<string, string?> { ["EventCounterIntervalSec"] = _opt.SamplingIntervalSec.ToString() });

            _enabledSources.Add(eventSource);
            _set.SetListenerActive(true);
        }
    }

    /// <summary>
    /// Handles incoming events and processes <c>EventCounters</c> payloads emitted by enabled providers.
    /// </summary>
    /// <param name="eventData">The event metadata and payload.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="eventData"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Only events with <see cref="EventWrittenEventArgs.EventName"/> equal to <c>"EventCounters"</c> are processed.
    /// The method extracts the value using <see cref="SelectValue(string, double?, double?, double?)"/>,
    /// applies unit adjustments (e.g., microseconds → milliseconds for RTT), and updates the appropriate metrics.
    /// </para>
    /// <para>
    /// Expected payload schema (first element is a dictionary):
    /// <list type="table">
    /// <item><term>Name</term><description>Counter name.</description></item>
    /// <item><term>DisplayUnits</term><description>Unit string such as <c>"ms"</c>, <c>"us"</c>, <c>"bytes"</c>.</description></item>
    /// <item><term>CounterType</term><description><c>"Sum"</c>, <c>"Mean"</c>/<c>"Average"</c>, or provider-specific.</description></item>
    /// <item><term>ActualValue</term><description>Current value (when available).</description></item>
    /// <item><term>Mean</term><description>Mean value over the interval.</description></item>
    /// <item><term>Increment</term><description>Delta observed in the last sampling interval.</description></item>
    /// </list>
    /// Unknown or unmapped counters are forwarded to <see cref="QuicMetricSet.PublishEventCounter(string, string, string, double)"/>.
    /// </para>
    /// </remarks>
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        try
        {
            if (!string.Equals(eventData.EventName, "EventCounters", StringComparison.Ordinal))
                return;

            var src = eventData.EventSource?.Name ?? "unknown";
            if (eventData.Payload is null || eventData.Payload.Count == 0)
                return;

            if (eventData.Payload[0] is not IDictionary<string, object?> f)
                return;

            var name = GetString(f, "Name") ?? GetString(f, "name") ?? "unknown";
            var displayUnits = GetString(f, "DisplayUnits") ?? string.Empty;
            var counterType = GetString(f, "CounterType") ?? string.Empty;

            var mean = GetDouble(f, "Mean");
            var increment = GetDouble(f, "Increment");
            var actual = GetDouble(f, "ActualValue");

            double value = SelectValue(counterType, actual, mean, increment);

            MapKnown(name, displayUnits, value, src);
        }
        catch (ObjectDisposedException)
        {
            // Best-effort resilience to racey disposal during shutdown.
        }
        catch (InvalidOperationException)
        {
            // Defensive: ignore malformed or unexpected payloads.
        }
        catch (FormatException)
        {
        }
        catch (InvalidCastException)
        {
        }
    }

    /// <summary>
    /// Chooses the most appropriate numeric value from a counter payload according to its type.
    /// </summary>
    /// <param name="counterType">Counter classification such as <c>"Sum"</c>, <c>"Mean"</c>, or <c>"Average"</c>.</param>
    /// <param name="actual">The <c>ActualValue</c> field, if present.</param>
    /// <param name="mean">The <c>Mean</c> field, if present.</param>
    /// <param name="increment">The <c>Increment</c> field, if present.</param>
    /// <returns>
    /// For <c>"Sum"</c>, prefers <paramref name="increment"/>; for <c>"Mean"</c>/<c>"Average"</c>, prefers
    /// <paramref name="mean"/>; otherwise falls back to <paramref name="actual"/> then others. Returns <c>0</c> if none is available.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="counterType"/> is <see langword="null"/>.</exception>
    private static double SelectValue(string counterType, double? actual, double? mean, double? increment)
    {
        ArgumentNullException.ThrowIfNull(counterType);

        if (counterType.Equals("Sum", StringComparison.OrdinalIgnoreCase))
        {
            return increment ?? actual ?? mean ?? 0d;
        }

        if (counterType.Equals("Mean", StringComparison.OrdinalIgnoreCase) ||
            counterType.Equals("Average", StringComparison.OrdinalIgnoreCase))
        {
            return mean ?? actual ?? increment ?? 0d;
        }

        return actual ?? mean ?? increment ?? 0d;
    }

    /// <summary>
    /// Maps known QUIC counter names to strongly-typed metrics; forwards unknown counters to the fallback multi-gauge.
    /// </summary>
    /// <param name="name">Provider-emitted counter name.</param>
    /// <param name="units">Unit string associated with the payload (e.g., <c>"us"</c>, <c>"ms"</c>, <c>"bytes"</c>).</param>
    /// <param name="value">Resolved numeric value for the current sampling period.</param>
    /// <param name="src">The source provider (<see cref="EventSource.Name"/>).</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is <see langword="null"/>.</exception>
    /// <remarks>
    /// Recognized patterns (case/spacing/underscore insensitive):
    /// <list type="bullet">
    /// <item><description><c>smoothedrtt</c>, <c>rtt</c>, <c>rttms</c> → RTT (microseconds converted to milliseconds when <paramref name="units"/> is <c>"us"</c>).</description></item>
    /// <item><description><c>cwnd</c>, <c>congestionwindow</c> → Congestion window in bytes.</description></item>
    /// <item><description><c>bytesinflight</c> → Bytes in flight.</description></item>
    /// <item><description><c>packetslost</c>, <c>lost</c> → Packet loss counter (rounded to integer).</description></item>
    /// <item><description><c>datagramssent</c> / <c>datagramsreceived</c> / <c>datagramsrecv</c> → Datagram counters.</description></item>
    /// <item><description><c>streamsopened</c> / <c>streamopened</c> → Stream open events.</description></item>
    /// <item><description><c>connectionerrors</c> / <c>connerrors</c> → Connection-level errors.</description></item>
    /// </list>
    /// Any other name increments the unknown-counter self-metric and is published via
    /// <see cref="QuicMetricSet.PublishEventCounter(string, string, string, double)"/>.
    /// </remarks>
    private void MapKnown(string name, string units, double value, string src)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(src);

        var n = NormalizeName(name.AsSpan());

        if (n.Contains("smoothedrtt", StringComparison.Ordinal) ||
            n.Equals("rtt", StringComparison.Ordinal) ||
            n.Contains("rttms", StringComparison.Ordinal))
        {
            var ms = units.Contains("us", StringComparison.OrdinalIgnoreCase) ? value / 1000.0 : value;
            _set.SetRttMs(ms);
            return;
        }

        if (n.Contains("cwnd", StringComparison.Ordinal) ||
            n.Contains("congestionwindow", StringComparison.Ordinal))
        {
            _set.SetCwndBytes(value);
            return;
        }

        if (n.Contains("bytesinflight", StringComparison.Ordinal))
        {
            _set.SetInFlight(value);
            return;
        }

        if (n.Contains("packetslost", StringComparison.Ordinal) ||
            n.Equals("lost", StringComparison.Ordinal))
        {
            _set.IncLost((long)Math.Round(value));
            return;
        }

        if (n.Contains("datagramssent", StringComparison.Ordinal))
        {
            _set.IncDgramsSent((long)Math.Round(value));
            return;
        }

        if (n.Contains("datagramsreceived", StringComparison.Ordinal) ||
            n.Contains("datagramsrecv", StringComparison.Ordinal))
        {
            _set.IncDgramsRecv((long)Math.Round(value));
            return;
        }

        if (n.Contains("streamsopened", StringComparison.Ordinal) ||
            n.Contains("streamopened", StringComparison.Ordinal))
        {
            _set.IncStreamsOpened((long)Math.Round(value));
            return;
        }

        if (n.Contains("connectionerrors", StringComparison.Ordinal) ||
            n.Contains("connerrors", StringComparison.Ordinal))
        {
            _set.IncConnErrors((long)Math.Round(value));
            return;
        }

        _set.IncUnknownCounter();
        _set.PublishEventCounter(src, name, units, value);
    }

    /// <summary>
    /// Attempts to retrieve a string value from an <see cref="IDictionary{TKey, TValue}"/> payload.
    /// </summary>
    /// <param name="d">The dictionary representing the counter fields.</param>
    /// <param name="k">The key to look up.</param>
    /// <returns>The string representation if present; otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="d"/> or <paramref name="k"/> is <see langword="null"/>.</exception>
    private static string? GetString(IDictionary<string, object?> d, string k)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(k);

        return d.TryGetValue(k, out var v) ? v?.ToString() : null;
    }

    /// <summary>
    /// Attempts to coerce a payload field to a <see cref="double"/> value.
    /// </summary>
    /// <param name="d">The dictionary representing the counter fields.</param>
    /// <param name="k">The key to look up.</param>
    /// <returns>The numeric value if present and convertible; otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="d"/> or <paramref name="k"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Supports common numeric primitives and falls back to <see cref="double.TryParse(string?, out double)"/> if needed.
    /// </remarks>
    private static double? GetDouble(IDictionary<string, object?> d, string k)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(k);

        if (!d.TryGetValue(k, out var v) || v is null)
            return null;

        return v switch
        {
            double dd => dd,
            float ff => ff,
            int ii => ii,
            long ll => ll,
            decimal mm => (double)mm,
            _ => double.TryParse(v.ToString(), out var p) ? p : null
        };
    }

    /// <summary>
    /// Produces a normalized identifier from a human-friendly counter name by removing separators
    /// (space, hyphen, underscore) and lower-casing ASCII characters.
    /// </summary>
    /// <param name="s">The source span to normalize.</param>
    /// <returns>A normalized, lower-case string with separators removed (max 128 chars).</returns>
    private string NormalizeName(ReadOnlySpan<char> s)
    {
        Span<char> buf = stackalloc char[Math.Min(s.Length, 128)];
        int j = 0;
        for (int i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == ' ' || ch == '-' || ch == '_')
                continue;
            if (ch <= 127)
                ch = char.ToLowerInvariant(ch);
            if (j < buf.Length)
                buf[j++] = ch;
        }
        return new string(buf[..j]);
    }

    /// <summary>
    /// Records a throttled internal error signal used to limit log spam over <see cref="QuicOptions.LogThrottleSeconds"/>.
    /// </summary>
    /// <remarks>
    /// When a logger is provided, this method updates an internal window to decide whether
    /// a warning should be emitted; actual logging sites should consult this throttle signal.
    /// </remarks>
    private void ThrottledError()
    {
        if (_log is null) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now - _lastLogUnixSec >= _opt.LogThrottleSeconds)
        {
            _lastLogUnixSec = now;
            _errCountWindow = 0;
        }
        else
        {
            _errCountWindow++;
        }
    }

    /// <summary>
    /// Disables all previously enabled providers and releases underlying resources.
    /// </summary>
    /// <remarks>
    /// This method is idempotent and safe to call multiple times, including during application shutdown.
    /// Any <see cref="ObjectDisposedException"/> or <see cref="InvalidOperationException"/> thrown by the eventing
    /// subsystem is caught to preserve process stability.
    /// </remarks>
    public new void Dispose()
    {
        try
        {
            lock (_sync)
            {
                foreach (var src in _enabledSources)
                {
                    try { DisableEvents(src); }
                    catch (ObjectDisposedException) { /* best-effort */ }
                    catch (InvalidOperationException) { /* best-effort */ }
                }
                _enabledSources.Clear();
                _set.SetListenerActive(false);
            }
        }
        finally
        {
            try { base.Dispose(); }
            catch (ObjectDisposedException) { /* best-effort */ }
            catch (InvalidOperationException) { /* best-effort */ }
        }
    }
}
