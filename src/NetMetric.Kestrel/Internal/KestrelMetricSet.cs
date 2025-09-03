// <copyright file="KestrelMetricSet.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kestrel.Internal;

/// <summary>
/// Provides a cohesive set of metric instruments for monitoring Kestrel server internals,
/// including connection lifecycle, TLS handshakes, HTTP/2 streams, and HTTP/3 (QUIC) activity.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="KestrelMetricSet"/> creates and updates gauges, counters, and histograms that
/// reflect the live state of the server. Metrics are consistently tagged with dimensions such as
/// <c>protocol</c>, <c>transport</c>, and <c>reason</c> to enable reliable filtering and aggregation
/// while keeping cardinality bounded.
/// </para>
/// <para>
/// Internally, the type caches metric instruments in thread-safe dictionaries and uses lightweight
/// atomic counters to track active counts across protocols. All public members are safe for concurrent use.
/// </para>
/// <para>
/// Instruments are built via <see cref="IMetricFactory"/> and use canonical names from
/// <see cref="KestrelMetricNames"/> and standardized tag keys from <see cref="KestrelTagKeys"/>.
/// </para>
/// </remarks>
/// <example>
/// Typical wiring with a listener that maps Kestrel/TLS events into this metric set:
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.Hosting;
/// using NetMetric.Kestrel.Configurations;
/// using NetMetric.Kestrel.Diagnostics;
/// using NetMetric.Kestrel.Internal;
///
/// var builder = Host.CreateApplicationBuilder(args);
///
/// // Assume NetMetric core has already been registered so IMetricFactory is available.
/// var factory = builder.Services.BuildServiceProvider().GetRequiredService<IMetricFactory>();
///
/// // Configure TLS handshake histogram buckets (ms) if desired:
/// var options = new KestrelMetricOptions();
/// var set = new KestrelMetricSet(factory, options.TlsHandshakeBucketsMs.ToArray());
///
/// // Hook an EventListener (e.g., KestrelEventListener) that calls into the set:
/// using var listener = new KestrelEventListener(set);
///
/// await builder.Build().RunAsync();
/// ]]></code>
/// </example>
public sealed class KestrelMetricSet
{
    private readonly IMetricFactory _factory;
    private readonly double[] _tlsBucketsMs;

    private readonly ConcurrentDictionary<string, IGauge> _connGauge = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ICounterMetric> _connTotal = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ICounterMetric> _connReset = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ICounterMetric> _connErrors = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, IGauge> _h2Active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ICounterMetric> _h2Total = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, IGauge> _h3ConnActive = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IGauge> _h3Active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ICounterMetric> _h3Total = new(StringComparer.Ordinal);

    private readonly Lazy<IBucketHistogramMetric> _tlsHandshake;
    private readonly Lazy<ICounterMetric> _keepAliveTimeouts;

    /// <summary>
    /// Initializes a new instance of the <see cref="KestrelMetricSet"/> class.
    /// </summary>
    /// <param name="factory">Metric factory used to construct gauges, counters, and histograms.</param>
    /// <param name="tlsHandshakeBucketsMs">
    /// Histogram bucket boundaries (in milliseconds) for TLS handshake duration,
    /// applied to <see cref="KestrelMetricNames.TlsHandshakeMs"/>.
    /// </param>
    /// <remarks>
    /// Instruments are created lazily on the first operation that requires them to avoid unnecessary startup cost.
    /// </remarks>
    public KestrelMetricSet(IMetricFactory factory, double[] tlsHandshakeBucketsMs)
    {
        _factory = factory;
        _tlsBucketsMs = tlsHandshakeBucketsMs;

        _tlsHandshake = new Lazy<IBucketHistogramMetric>(() =>
            _factory.Histogram(KestrelMetricNames.TlsHandshakeMs, "TLS handshake duration (ms)")
                .WithUnit("ms")
                .UseBucketBounds(_tlsBucketsMs)
                .Build());

        _keepAliveTimeouts = new Lazy<ICounterMetric>(() =>
            _factory.Counter(KestrelMetricNames.KeepAliveTimeouts, "Keep-Alive timeouts").Build());
    }

    // ---- Connections (h1/h2/h3) ----

    /// <summary>
    /// Records the start of a connection: increments totals and updates the active connection gauge.
    /// </summary>
    /// <param name="protocol">The application protocol (e.g., <c>"h1"</c>, <c>"h2"</c>, <c>"h3"</c>).</param>
    /// <param name="transport">The underlying transport (e.g., <c>"tcp"</c>, <c>"quic"</c>).</param>
    /// <remarks>
    /// Creates and caches protocol/transport-specific instruments on first use:
    /// <list type="bullet">
    /// <item><description><see cref="KestrelMetricNames.ConnectionsActive"/> (<c>gauge</c>)</description></item>
    /// <item><description><see cref="KestrelMetricNames.ConnectionsTotal"/> (<c>counter</c>)</description></item>
    /// </list>
    /// Both are tagged with <see cref="KestrelTagKeys.Protocol"/> and <see cref="KestrelTagKeys.Transport"/>.
    /// </remarks>
    public void IncConnection(string protocol, string transport)
    {
        string key = string.Concat(protocol, "|", transport);

        IGauge g = _connGauge.GetOrAdd(key, _ =>
            _factory.Gauge(KestrelMetricNames.ConnectionsActive, "Active connections")
                .WithTags(t => { t.Add(KestrelTagKeys.Protocol, protocol); t.Add(KestrelTagKeys.Transport, transport); })
                .Build());

        ICounterMetric c = _connTotal.GetOrAdd(key, _ =>
            _factory.Counter(KestrelMetricNames.ConnectionsTotal, "Total connections")
                .WithTags(t => { t.Add(KestrelTagKeys.Protocol, protocol); t.Add(KestrelTagKeys.Transport, transport); })
                .Build());

        long count = _active.AddOrUpdate(key, 1, (_, old) => old + 1);
        g.SetValue(count);
        c.Increment();
    }

    /// <summary>
    /// Records the end of a connection: decrements the active connection gauge and,
    /// if a termination reason is provided, increments the reset counter for that reason.
    /// </summary>
    /// <param name="protocol">The application protocol (e.g., <c>"h1"</c>, <c>"h2"</c>, <c>"h3"</c>).</param>
    /// <param name="transport">The underlying transport (e.g., <c>"tcp"</c>, <c>"quic"</c>).</param>
    /// <param name="reason">Optional termination reason (e.g., <c>"timeout"</c>, <c>"reset"</c>).</param>
    /// <remarks>
    /// When <paramref name="reason"/> is provided, increments
    /// <see cref="KestrelMetricNames.ConnectionResets"/> tagged with
    /// <see cref="KestrelTagKeys.Protocol"/> and <see cref="KestrelTagKeys.Reason"/>.
    /// </remarks>
    public void DecConnection(string protocol, string transport, string? reason = null)
    {
        string key = string.Concat(protocol, "|", transport);
        IGauge g = _connGauge.GetOrAdd(key, _ =>
            _factory.Gauge(KestrelMetricNames.ConnectionsActive, "Active connections")
                .WithTags(t => { t.Add(KestrelTagKeys.Protocol, protocol); t.Add(KestrelTagKeys.Transport, transport); })
                .Build());

        long val = _active.AddOrUpdate(key, 0, (_, old) => Math.Max(0, old - 1));
        g.SetValue(val);

        if (!string.IsNullOrWhiteSpace(reason))
        {
            string rkey = string.Concat(protocol, "|", reason);
            ICounterMetric r = _connReset.GetOrAdd(rkey, _ =>
                _factory.Counter(KestrelMetricNames.ConnectionResets, "Connection resets")
                    .WithTags(t => { t.Add(KestrelTagKeys.Protocol, protocol); t.Add(KestrelTagKeys.Reason, reason!); })
                    .Build());
            r.Increment();
        }
    }

    /// <summary>
    /// Records a connection-level error with a standardized <paramref name="reason"/>.
    /// </summary>
    /// <param name="reason">Reason label/category (e.g., <c>bad_request</c>, <c>app_error</c>).</param>
    /// <remarks>
    /// Increments <see cref="KestrelMetricNames.ConnectionErrors"/> with tag
    /// <see cref="KestrelTagKeys.Reason"/>. The per-reason counter is created on first use.
    /// </remarks>
    public void Error(string reason)
    {
        ICounterMetric c = _connErrors.GetOrAdd(reason, _ =>
            _factory.Counter(KestrelMetricNames.ConnectionErrors, "Connection errors")
                .WithTags(t => t.Add(KestrelTagKeys.Reason, reason))
                .Build());
        c.Increment();
    }

    /// <summary>
    /// Increments the counter for keep-alive timeouts.
    /// </summary>
    /// <remarks>
    /// Increments <see cref="KestrelMetricNames.KeepAliveTimeouts"/>.
    /// </remarks>
    public void KeepAliveTimeout() => _keepAliveTimeouts.Value.Increment();

    /// <summary>
    /// Observes a single TLS handshake latency sample.
    /// </summary>
    /// <param name="ms">Handshake duration in milliseconds.</param>
    /// <remarks>
    /// Records into <see cref="KestrelMetricNames.TlsHandshakeMs"/> using the configured bucket bounds.
    /// Values should be non-negative; invalid or extreme values should be filtered at the call site.
    /// </remarks>
    public void ObserveTlsHandshake(double ms) => _tlsHandshake.Value.Observe(ms);

    /// <summary>
    /// Records the start of an HTTP/2 stream: updates the active gauge and increments totals.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="KestrelMetricNames.H2StreamsActive"/> (<c>gauge</c>) and
    /// <see cref="KestrelMetricNames.H2StreamsTotal"/> (<c>counter</c>).
    /// </remarks>
    public void H2StreamStart()
    {
        IGauge g = _h2Active.GetOrAdd("h2", _ => _factory.Gauge(KestrelMetricNames.H2StreamsActive, "HTTP/2 active streams").Build());
        ICounterMetric c = _h2Total.GetOrAdd("h2", _ => _factory.Counter(KestrelMetricNames.H2StreamsTotal, "HTTP/2 total streams").Build());
        long val = _h2Count.Increment();
        g.SetValue(val);
        c.Increment();
    }

    /// <summary>
    /// Records the end of an HTTP/2 stream: decrements the active stream gauge.
    /// </summary>
    public void H2StreamStop()
    {
        IGauge g = _h2Active.GetOrAdd("h2", _ => _factory.Gauge(KestrelMetricNames.H2StreamsActive, "HTTP/2 active streams").Build());
        long val = Math.Max(0, _h2Count.Decrement());
        g.SetValue(val);
    }

    /// <summary>
    /// Records the start of an HTTP/3 connection: increments the active connection gauge.
    /// </summary>
    public void H3ConnectionStart()
    {
        IGauge g = _h3ConnActive.GetOrAdd("h3", _ => _factory.Gauge(KestrelMetricNames.H3ConnectionsActive, "HTTP/3 active connections").Build());
        long val = _h3ConnCount.Increment();
        g.SetValue(val);
    }

    /// <summary>
    /// Records the end of an HTTP/3 connection: decrements the active connection gauge.
    /// </summary>
    public void H3ConnectionStop()
    {
        IGauge g = _h3ConnActive.GetOrAdd("h3", _ => _factory.Gauge(KestrelMetricNames.H3ConnectionsActive, "HTTP/3 active connections").Build());
        long val = Math.Max(0, _h3ConnCount.Decrement());
        g.SetValue(val);
    }

    /// <summary>
    /// Records the start of an HTTP/3 stream: updates the active gauge and increments totals.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="KestrelMetricNames.H3StreamsActive"/> (<c>gauge</c>) and
    /// <see cref="KestrelMetricNames.H3StreamsTotal"/> (<c>counter</c>).
    /// </remarks>
    public void H3StreamStart()
    {
        IGauge g = _h3Active.GetOrAdd("h3", _ => _factory.Gauge(KestrelMetricNames.H3StreamsActive, "HTTP/3 active streams").Build());
        ICounterMetric c = _h3Total.GetOrAdd("h3", _ => _factory.Counter(KestrelMetricNames.H3StreamsTotal, "HTTP/3 total streams").Build());
        long val = _h3Count.Increment();
        g.SetValue(val);
        c.Increment();
    }

    /// <summary>
    /// Records the end of an HTTP/3 stream: decrements the active stream gauge.
    /// </summary>
    public void H3StreamStop()
    {
        IGauge g = _h3Active.GetOrAdd("h3", _ => _factory.Gauge(KestrelMetricNames.H3StreamsActive, "HTTP/3 active streams").Build());
        long val = Math.Max(0, _h3Count.Decrement());
        g.SetValue(val);
    }

    /// <summary>
    /// Resets all active gauges and internal counters to zero.
    /// </summary>
    /// <remarks>
    /// Intended to be called on service shutdown to clear cached state so that subsequent startups
    /// begin from a clean slate.
    /// </remarks>
    public void Reset()
    {
        foreach (var kv in _connGauge)
        {
            kv.Value.SetValue(0);
            _active.AddOrUpdate(kv.Key, 0, (_, __) => 0);
        }

        foreach (var g in _h2Active.Values)
            g.SetValue(0);
        foreach (var g in _h3Active.Values)
            g.SetValue(0);
        foreach (var g in _h3ConnActive.Values)
            g.SetValue(0);

        _h2Count.Reset();
        _h3Count.Reset();
        _h3ConnCount.Reset();
    }

    /// <summary>
    /// Lightweight atomic counter used to track active counts safely under concurrency.
    /// </summary>
    private sealed class Atomic
    {
        private long _v;

        /// <summary>Atomically increments the counter and returns the updated value.</summary>
        public long Increment() => Interlocked.Increment(ref _v);

        /// <summary>Atomically decrements the counter and returns the updated value.</summary>
        public long Decrement() => Interlocked.Decrement(ref _v);

        /// <summary>Reads the current value using a volatile read.</summary>
        public long Value => System.Threading.Volatile.Read(ref _v);

        /// <summary>Resets the counter to zero atomically.</summary>
        public void Reset() => Interlocked.Exchange(ref _v, 0);
    }

    private readonly ConcurrentDictionary<string, long> _active = new(StringComparer.Ordinal);
    private readonly Atomic _h2Count = new();
    private readonly Atomic _h3Count = new();
    private readonly Atomic _h3ConnCount = new();
}
