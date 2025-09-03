// <copyright file="QuicMetricSet.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Quic.Internal;

/// <summary>
/// Provides a cached set of QUIC metric instruments and convenience methods to update them.
/// </summary>
/// <remarks>
/// <para>
/// This type backs both <em>known</em> QUIC counters (e.g., smoothed RTT, congestion window,
/// bytes-in-flight, lost packets, datagrams sent/received, streams opened, connection errors)
/// and <em>fallback</em> EventCounters that are not explicitly mapped. Unknown counters are
/// emitted via a multi-gauge instrument with tags that retain the original provider name,
/// counter name, and unit. Cardinality protection is enforced to prevent metric explosion.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> All public methods are safe to call from multiple threads.
/// Internal state used for fallback series accounting is protected with a private lock,
/// and metric instruments are lazily created in a thread-safe manner via <see cref="Lazy{T}"/>.
/// </para>
/// <para>
/// <strong>Performance:</strong> Instruments are created once and reused. Fallback series IDs
/// are compact (16-hex chars) derived from a fast SHA-256 truncation, minimizing label size
/// while remaining stable across process lifetime.
/// </para>
/// </remarks>
/// <example>
/// Registering QUIC metrics and the hosted listener:
/// <code language="csharp"><![CDATA[
/// services.AddSingleton<QuicMetricSet>();
/// services.Configure<QuicOptions>(opt =>
/// {
///     opt.SamplingIntervalSec = 1;          // 1s EventCounter sampling
///     opt.EnableFallback = true;            // publish unknown counters as multi-gauge
///     opt.MaxFallbackSeries = 200;          // cap unique fallback series
/// });
/// services.AddHostedService<QuicMetricsHostedService>();
///
/// // From inside QuicEventListener mapping:
/// _set.SetRttMs(12.5);
/// _set.IncDgramsSent(42);
/// _set.PublishEventCounter(source: "MsQuic", rawName: "send-queue-depth", unit: "count", value: 7);
/// ]]></code>
/// </example>
public sealed class QuicMetricSet
{
    private readonly IMetricFactory _f;
    private readonly QuicOptions _opt;

    private readonly Lazy<IGauge> _rtt;
    private readonly Lazy<IGauge> _cwnd;
    private readonly Lazy<IGauge> _inFlight;

    private readonly Lazy<ICounterMetric> _lost;
    private readonly Lazy<ICounterMetric> _dgramSent;
    private readonly Lazy<ICounterMetric> _dgramRecv;
    private readonly Lazy<ICounterMetric> _streamsOpened;
    private readonly Lazy<ICounterMetric> _connErrors;

    private readonly Lazy<IMultiGauge> _ecGauge;

    private readonly Lazy<IGauge> _listenerActive;
    private readonly Lazy<ICounterMetric> _unknownCounters;
    private readonly Lazy<ICounterMetric> _fallbackDropped;

    private readonly object _sync = new();
    private int _fallbackSeriesCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuicMetricSet"/> class using default <see cref="QuicOptions"/>.
    /// </summary>
    /// <param name="f">The <see cref="IMetricFactory"/> used to create metric instruments.</param>
    /// <remarks>
    /// This overload is convenient for tests or simple setups. In production, prefer configuring
    /// <see cref="QuicOptions"/> and calling <see cref="QuicMetricSet(IMetricFactory, QuicOptions)"/>.
    /// </remarks>
    public QuicMetricSet(IMetricFactory f) : this(f, new QuicOptions()) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="QuicMetricSet"/> class.
    /// </summary>
    /// <param name="f">The <see cref="IMetricFactory"/> used to create metric instruments.</param>
    /// <param name="opt">The QUIC metric configuration <see cref="QuicOptions"/>.</param>
    /// <remarks>
    /// Instruments are created lazily upon first use. The supplied <paramref name="opt"/> controls
    /// fallback behavior, sampling expectations, and cardinality limits.
    /// </remarks>
    public QuicMetricSet(IMetricFactory f, QuicOptions opt)
    {
        _f = f;
        _opt = opt;

        _rtt = new(() => f.Gauge(QuicMetricNames.RttSmoothed, "QUIC smoothed RTT (ms)").WithUnit("ms").Build());
        _cwnd = new(() => f.Gauge(QuicMetricNames.CwndBytes, "QUIC congestion window (bytes)").WithUnit("bytes").Build());
        _inFlight = new(() => f.Gauge(QuicMetricNames.BytesInFlight, "QUIC bytes in flight").WithUnit("bytes").Build());

        _lost = new(() => f.Counter(QuicMetricNames.PacketsLost, "QUIC packets lost").Build());
        _dgramSent = new(() => f.Counter(QuicMetricNames.DatagramsSent, "QUIC datagrams sent").Build());
        _dgramRecv = new(() => f.Counter(QuicMetricNames.DatagramsRecv, "QUIC datagrams received").Build());
        _streamsOpened = new(() => f.Counter(QuicMetricNames.StreamsOpened, "QUIC streams opened").Build());
        _connErrors = new(() => f.Counter(QuicMetricNames.ConnErrors, "QUIC connection errors").Build());

        _ecGauge = new(() => f.MultiGauge(QuicMetricNames.EventCounterGauge, "QUIC eventcounters (misc)").Build());

        _listenerActive = new(() => f.Gauge(QuicMetricNames.ListenerActive, "QUIC listener active (0/1)").Build());
        _unknownCounters = new(() => f.Counter(QuicMetricNames.UnknownCountersTotal, "Unknown QUIC counters observed").Build());
        _fallbackDropped = new(() => f.Counter(QuicMetricNames.FallbackDroppedTotal, "Dropped fallback metrics due to cardinality limit").Build());
    }

    // Known mappings

    /// <summary>
    /// Sets the current smoothed round-trip time value.
    /// </summary>
    /// <param name="ms">The RTT value in milliseconds.</param>
    /// <remarks>Mapped from counters like <c>SmoothedRtt</c>, <c>rtt</c>, or <c>rttMs</c>.</remarks>
    public void SetRttMs(double ms) => _rtt.Value.SetValue(ms);

    /// <summary>
    /// Sets the current congestion window size.
    /// </summary>
    /// <param name="bytes">The congestion window in bytes.</param>
    /// <remarks>Mapped from counters such as <c>cwnd</c> or <c>congestionWindow</c>.</remarks>
    public void SetCwndBytes(double bytes) => _cwnd.Value.SetValue(bytes);

    /// <summary>
    /// Sets the number of bytes currently in flight.
    /// </summary>
    /// <param name="bytes">The bytes-in-flight value.</param>
    public void SetInFlight(double bytes) => _inFlight.Value.SetValue(bytes);

    /// <summary>
    /// Increments the total number of lost QUIC packets.
    /// </summary>
    /// <param name="v">The increment to apply (usually a rounded counter increment).</param>
    public void IncLost(long v) => _lost.Value.Increment(v);

    /// <summary>
    /// Increments the number of datagrams sent.
    /// </summary>
    /// <param name="v">The increment to apply.</param>
    public void IncDgramsSent(long v) => _dgramSent.Value.Increment(v);

    /// <summary>
    /// Increments the number of datagrams received.
    /// </summary>
    /// <param name="v">The increment to apply.</param>
    public void IncDgramsRecv(long v) => _dgramRecv.Value.Increment(v);

    /// <summary>
    /// Increments the number of streams opened.
    /// </summary>
    /// <param name="v">The increment to apply.</param>
    public void IncStreamsOpened(long v) => _streamsOpened.Value.Increment(v);

    /// <summary>
    /// Increments the number of connection-level errors.
    /// </summary>
    /// <param name="v">The increment to apply.</param>
    public void IncConnErrors(long v) => _connErrors.Value.Increment(v);

    // Self metrics

    /// <summary>
    /// Sets whether the QUIC listener (EventListener) is currently active.
    /// </summary>
    /// <param name="active"><see langword="true"/> to mark active; otherwise <see langword="false"/>.</param>
    /// <remarks>Exposed as a gauge with values 0 (inactive) and 1 (active).</remarks>
    public void SetListenerActive(bool active) => _listenerActive.Value.SetValue(active ? 1 : 0);

    /// <summary>
    /// Increments the count of unknown EventCounter metrics encountered.
    /// </summary>
    /// <remarks>
    /// Useful to monitor drift between framework/provider versions and the mapping maintained in
    /// the listener. A rising trend suggests adding explicit mappings.
    /// </remarks>
    public void IncUnknownCounter() => _unknownCounters.Value.Increment();

    /// <summary>
    /// Increments the number of fallback metrics that were dropped due to the configured cardinality limit.
    /// </summary>
    /// <param name="v">The increment to apply (normally 1 per drop).</param>
    public void IncFallbackDropped(long v) => _fallbackDropped.Value.Increment(v);

    /// <summary>
    /// Publishes an unmapped EventCounter via the multi-gauge instrument with stable series identity and descriptive tags.
    /// </summary>
    /// <param name="source">The provider name (e.g., <c>MsQuic</c>, <c>System.Net.Quic</c>).</param>
    /// <param name="rawName">The original counter name as emitted by the provider.</param>
    /// <param name="unit">The unit associated with the value (e.g., <c>ms</c>, <c>bytes</c>, <c>count</c>).</param>
    /// <param name="value">The numeric value to publish.</param>
    /// <remarks>
    /// <para>
    /// If <see cref="QuicOptions.EnableFallback"/> is disabled, the call is a no-op.
    /// Otherwise, the method:
    /// </para>
    /// <list type="number">
    ///   <item>Normalizes <paramref name="rawName"/> to a lowercase ASCII identifier.</item>
    ///   <item>Derives a short, stable series ID using <see cref="ShortHash16(string)"/>.</item>
    ///   <item>Checks <see cref="QuicOptions.MaxFallbackSeries"/> and drops the sample if the cap is reached,
    ///   incrementing <see cref="IncFallbackDropped(long)"/>.</item>
    ///   <item>Publishes the value with tags (<c>source</c>, <c>name</c>, <c>unit</c>) via the <see cref="IMultiGauge"/>
    ///   instrument’s <c>AddSibling</c> method.</item>
    /// </list>
    /// </remarks>
    public void PublishEventCounter(string source, string rawName, string unit, double value)
    {
        if (!_opt.EnableFallback)
            return;

        ArgumentNullException.ThrowIfNull(rawName);

        var name = NormalizeAndTrim(rawName, _opt.MaxFallbackNameLength);
        string id = $"{QuicMetricNames.EventCounterGauge}.{ShortHash16(name)}";

        lock (_sync)
        {
            if (_fallbackSeriesCount >= _opt.MaxFallbackSeries)
            {
                IncFallbackDropped(1);
                return;
            }
            _fallbackSeriesCount++;
        }

        _ecGauge.Value.AddSibling(
            id: id,
            name: $"QUIC {name}",
            value: value,
            tags: new Dictionary<string, string>
            {
                [QuicTagKeys.Source] = source,
                [QuicTagKeys.Name] = rawName,
                [QuicTagKeys.Unit] = unit,
            });
    }

    /// <summary>
    /// Normalizes and truncates a metric name into a sanitized, lowercase ASCII identifier with separators removed.
    /// </summary>
    /// <param name="s">The input string to normalize.</param>
    /// <param name="maxLen">The maximum length of the resulting identifier.</param>
    /// <returns>A normalized identifier not exceeding <paramref name="maxLen"/> characters.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="s"/> is <see langword="null"/>.</exception>
    private static string NormalizeAndTrim(string s, int maxLen)
    {
        ArgumentNullException.ThrowIfNull(s);

        Span<char> buf = stackalloc char[Math.Min(s.Length, maxLen)];
        int j = 0;
        foreach (var ch in s)
        {
            if (ch == ' ' || ch == '-' || ch == '_')
                continue;
            char c = ch <= 127 ? char.ToLowerInvariant(ch) : ch;
            if (j < buf.Length)
                buf[j++] = c;
            else
                break;
        }
        return new string(buf[..j]);
    }

    /// <summary>
    /// Produces a stable 16-character uppercase hexadecimal identifier from the SHA-256 of the input.
    /// </summary>
    /// <param name="input">The input string to hash.</param>
    /// <returns>A 16-character (8-byte) uppercase hex string suitable for compact series identity.</returns>
    private static string ShortHash16(string input)
    {
        // Prefer static HashData over ComputeHash, and output upper-hex
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 8))
                     .ToUpperInvariant();
    }
}
