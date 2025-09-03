// <copyright file="SignalRMetricSet.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SignalR.Internal;

/// <summary>
/// Default <see cref="ISignalRMetrics"/> implementation that materializes and caches metric instruments
/// per (hub, transport, reason, method, direction, group, etc.) key.
/// </summary>
/// <remarks>
/// <para>
/// This type is designed to be used concurrently across SignalR hubs. It relies on
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/> to lazily create and
/// reuse instruments with stable tag sets.
/// </para>
/// <para>
/// Transport names can be normalized (e.g., "ws" → "websockets") depending on
/// <see cref="SignalRMetricsOptions.NormalizeTransport"/>.
/// </para>
/// </remarks>
internal sealed class SignalRMetricSet : ISignalRMetrics
{
    private readonly IMetricFactory _factory;
    private readonly SignalRMetricsOptions _opt;
    private readonly ConcurrentDictionary<HubTransportKey, IGauge> _connGauge = new();
    private readonly ConcurrentDictionary<HubTransportKey, ICounterMetric> _connTotal = new();
    private readonly ConcurrentDictionary<HubTransportReasonKey, ICounterMetric> _discTotal = new();
    private readonly ConcurrentDictionary<HubTransportReasonKey, IBucketHistogramMetric> _connDur = new();
    private readonly ConcurrentDictionary<HubMethodKey, IBucketHistogramMetric> _methodDur = new();
    private readonly ConcurrentDictionary<HubMethodOutcomeKey, ICounterMetric> _msgTotal = new();
    private readonly ConcurrentDictionary<HubMethodDirectionKey, IBucketHistogramMetric> _msgSize = new();
    private readonly ConcurrentDictionary<string, ICounterMetric> _negotiate = new();
    private readonly ConcurrentDictionary<string, IBucketHistogramMetric> _negotiateDur = new();
    private readonly ConcurrentDictionary<string, ICounterMetric> _groupsAdd = new();
    private readonly ConcurrentDictionary<string, ICounterMetric> _groupsRemove = new();
    private readonly ConcurrentDictionary<string, ICounterMetric> _groupsSend = new();
    // private readonly ConcurrentDictionary<string, IGauge> _groupsActive = new(); // UNUSED -> removed
    private readonly ConcurrentDictionary<string, IGauge> _usersActive = new();
    private readonly ConcurrentDictionary<string, ICounterMetric> _streamItems = new();
    private readonly ConcurrentDictionary<string, ICounterMetric> _errors = new();
    private readonly ConcurrentDictionary<string, ICounterMetric> _authOutcome = new();
    private readonly ConcurrentDictionary<HubTransportKey, long> _active = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRMetricSet"/> class.
    /// </summary>
    /// <param name="factory">Metric factory used to create instruments (counters, gauges, histograms).</param>
    /// <param name="options">Behavioral options such as bucket bounds, sampling, and normalization.</param>
    public SignalRMetricSet(IMetricFactory factory, SignalRMetricsOptions options)
    {
        _factory = factory;
        _opt = options;
    }

    /// <summary>
    /// Casts a raw instrument builder to a strongly typed builder.
    /// </summary>
    /// <typeparam name="T">Metric interface type (e.g., <see cref="IGauge"/>, <see cref="ICounterMetric"/>).</typeparam>
    /// <param name="raw">Raw builder returned by the factory or a chained <c>WithXxx</c> call.</param>
    /// <returns>The typed instrument builder.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the provided object is not a compatible builder.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IInstrumentBuilder<T> CastBuilder<T>(object raw) where T : class, IMetric
        => raw is IInstrumentBuilder<T> b ? b : throw new InvalidOperationException();

    /// <summary>
    /// Normalizes transport strings to a canonical tag value if possible.
    /// </summary>
    /// <param name="t">Original transport string (e.g., "ws", "sse", "longpolling").</param>
    /// <returns>Normalized transport tag value or <see cref="SignalRTagValues.TUnknown"/> if not recognized.</returns>
    private static string NormTransport(string? t)
        => t?.ToUpperInvariant() switch
        {
            "WEBSOCKETS" or "WS" => SignalRTagValues.TWs,
            "SERVERSENTEVENTS" or "SSE" => SignalRTagValues.TSse,
            "LONGPOLLING" or "LP" => SignalRTagValues.TLp,
            _ => SignalRTagValues.TUnknown
        };

    /// <summary>
    /// Increments active connection gauge and total connections counter for a given hub/transport.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="transport">Transport used for the connection.</param>
    /// <remarks>
    /// Tags: <c>hub</c>, <c>transport</c>. The gauge reflects the current active count per key;
    /// the counter reflects the cumulative total.
    /// </remarks>
    public void IncConnection(string hub, string transport)
    {
        var key = new HubTransportKey(hub, _opt.NormalizeTransport ? NormTransport(transport) : transport);

        var gauge = _connGauge.GetOrAdd(key, static (k, s) =>
        {
            var gb = CastBuilder<IGauge>(s._factory.Gauge(SignalRMetricNames.ConnectionGauge, "SignalR active connections"));
            gb = CastBuilder<IGauge>(gb.WithTags(t => { t.Add(SignalRTagKeys.Hub, k.Hub); t.Add(SignalRTagKeys.Transport, k.Transport); }));
            return gb.Build();
        }, this);

        var total = _connTotal.GetOrAdd(key, static (k, s) =>
        {
            var cb = CastBuilder<ICounterMetric>(s._factory.Counter(SignalRMetricNames.ConnectionsTotal, "SignalR connections total"));
            cb = CastBuilder<ICounterMetric>(cb.WithTags(t => { t.Add(SignalRTagKeys.Hub, k.Hub); t.Add(SignalRTagKeys.Transport, k.Transport); }));
            return cb.Build();
        }, this);

        var val = _active.AddOrUpdate(key, 1, static (_, old) => old + 1);
        gauge.SetValue(val);
        total.Increment();
    }

    /// <summary>
    /// Decrements active connection gauge for a given hub/transport and increments disconnect counter by reason.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="transport">Transport used for the connection.</param>
    /// <param name="reason">Disconnect reason (source/framework dependent).</param>
    /// <remarks>
    /// Tags:
    /// <list type="bullet">
    /// <item><description>Gauge/Total: <c>hub</c>, <c>transport</c></description></item>
    /// <item><description>Disconnect counter: <c>hub</c>, <c>transport</c>, <c>reason</c></description></item>
    /// </list>
    /// </remarks>
    public void DecConnection(string hub, string transport, string reason)
    {
        var key = new HubTransportKey(hub, _opt.NormalizeTransport ? NormTransport(transport) : transport);
        var rkey = new HubTransportReasonKey(key.Hub, key.Transport, reason);

        var disc = _discTotal.GetOrAdd(rkey, static (k, s) =>
        {
            var cb = CastBuilder<ICounterMetric>(s._factory.Counter(SignalRMetricNames.DisconnectsTotal, "SignalR disconnects total"));
            cb = CastBuilder<ICounterMetric>(cb.WithTags(t => { t.Add(SignalRTagKeys.Hub, k.Hub); t.Add(SignalRTagKeys.Transport, k.Transport); t.Add(SignalRTagKeys.Reason, k.Reason); }));
            return cb.Build();
        }, this);

        var gauge = _connGauge.GetOrAdd(key, static (k, s) =>
        {
            var gb = CastBuilder<IGauge>(s._factory.Gauge(SignalRMetricNames.ConnectionGauge, "SignalR active connections"));
            gb = CastBuilder<IGauge>(gb.WithTags(t => { t.Add(SignalRTagKeys.Hub, k.Hub); t.Add(SignalRTagKeys.Transport, k.Transport); }));
            return gb.Build();
        }, this);

        var val = _active.AddOrUpdate(key, 0, static (_, old) => old > 0 ? old - 1 : 0);
        gauge.SetValue(val);
        disc.Increment();
    }

    /// <summary>
    /// Observes connection lifetime duration for a given hub/transport/reason.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="transport">Transport used for the connection.</param>
    /// <param name="reason">Disconnect reason associated with the ended connection.</param>
    /// <param name="duration">Connection duration.</param>
    /// <remarks>
    /// Histogram unit: <c>ms</c>.
    /// Tags: <c>hub</c>, <c>transport</c>, <c>reason</c>.
    /// Bucket bounds are taken from <see cref="SignalRMetricsOptions.ConnectionDurationBucketsMs"/>.
    /// </remarks>
    public void ObserveConnectionDuration(string hub, string transport, string reason, TimeSpan duration)
    {
        var k = new HubTransportReasonKey(hub, _opt.NormalizeTransport ? NormTransport(transport) : transport, reason);
        var hist = _connDur.GetOrAdd(k, static (kk, s) =>
        {
            var hb = CastBuilder<IBucketHistogramMetric>(s._factory.Histogram(SignalRMetricNames.ConnectionDuration, "SignalR connection duration (ms)"));
            hb = CastBuilder<IBucketHistogramMetric>(hb.WithUnit("ms"));
            hb = CastBuilder<IBucketHistogramMetric>(hb.WithBounds(s._opt.ConnectionDurationBucketsMs.ToArray()));
            hb = CastBuilder<IBucketHistogramMetric>(hb.WithTags(t => { t.Add(SignalRTagKeys.Hub, kk.Hub); t.Add(SignalRTagKeys.Transport, kk.Transport); t.Add(SignalRTagKeys.Reason, kk.Reason); }));
            return hb.Build();
        }, this);
        hist.Observe(duration.TotalMilliseconds);
    }

    /// <summary>
    /// Records an error occurrence for a hub/scope/exception-type triple.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="scope">Logical scope (e.g., <c>invoke</c>, <c>negotiate</c>, <c>transport</c>).</param>
    /// <param name="exceptionType">CLR exception type name. May be truncated by options.</param>
    /// <remarks>
    /// <para>
    /// If <see cref="SignalRMetricsOptions.CaptureExceptionType"/> is disabled, this method is a no-op.
    /// The exception type is truncated to <see cref="SignalRMetricsOptions.MaxExceptionTypeLength"/> characters.
    /// </para>
    /// <para>
    /// Tags: <c>hub</c>, <c>scope</c>, <c>exception_type</c>.
    /// </para>
    /// </remarks>
    public void ObserveError(string hub, string scope, string exceptionType)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        if (!_opt.CaptureExceptionType) return;

        if (exceptionType.Length > _opt.MaxExceptionTypeLength)
            exceptionType = exceptionType.AsSpan(0, _opt.MaxExceptionTypeLength).ToString();

        var key = $"{hub}|{scope}|{exceptionType}";
        var c = _errors.GetOrAdd(key, static (k, s) =>
        {
            var parts = k.Split('|');
            var cb = CastBuilder<ICounterMetric>(
                s._factory.Counter(SignalRMetricNames.ErrorsTotal, "SignalR errors total"));
            cb = CastBuilder<ICounterMetric>(cb.WithTags(t =>
            {
                t.Add(SignalRTagKeys.Hub, parts[0]);
                t.Add(SignalRTagKeys.Scope, parts[1]);
                t.Add(SignalRTagKeys.ExceptionType, parts[2]);
            }));
            return cb.Build();
        }, this);
        c.Increment();
    }

    /// <summary>
    /// Records a negotiation attempt, optionally observing its duration and whether a fallback path was used.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="chosenTransport">Chosen transport (may be normalized).</param>
    /// <param name="duration">Optional negotiation duration.</param>
    /// <param name="fallback">Whether transport fallback occurred.</param>
    /// <remarks>
    /// Counter tags: <c>hub</c>, <c>transport</c>, optional <c>fallback</c>.
    /// Duration histogram uses unit <c>ms</c> and bounds from <see cref="SignalRMetricsOptions.LatencyBucketsMs"/>.
    /// </remarks>
    public void Negotiated(string hub, string? chosenTransport, TimeSpan? duration = null, bool? fallback = null)
    {
        var transport = _opt.NormalizeTransport ? NormTransport(chosenTransport) : (chosenTransport ?? SignalRTagValues.TUnknown);
        var baseKey = $"{hub}|{transport}|{fallback?.ToString().ToUpperInvariant() ?? ""}";

        var c = _negotiate.GetOrAdd(baseKey, static (k, s) =>
        {
            var parts = k.Split('|');
            var cb = CastBuilder<ICounterMetric>(s._factory.Counter(SignalRMetricNames.NegotiationsTotal, "SignalR negotiations total"));
            cb = CastBuilder<ICounterMetric>(cb.WithTags(t =>
            {
                t.Add(SignalRTagKeys.Hub, parts[0]);
                t.Add(SignalRTagKeys.Transport, parts[1]);
                if (parts.Length > 2 && !string.IsNullOrEmpty(parts[2])) t.Add(SignalRTagKeys.Fallback, parts[2]);
            }));
            return cb.Build();
        }, this);
        c.Increment();

        if (duration is { } d)
        {
            var h = _negotiateDur.GetOrAdd(baseKey, static (k, s) =>
            {
                var parts = k.Split('|');
                var hb = CastBuilder<IBucketHistogramMetric>(s._factory.Histogram(SignalRMetricNames.NegotiationDur, "SignalR negotiation duration (ms)"));
                hb = CastBuilder<IBucketHistogramMetric>(hb.WithUnit("ms"));
                hb = CastBuilder<IBucketHistogramMetric>(hb.WithBounds(s._opt.LatencyBucketsMs.ToArray()));
                hb = CastBuilder<IBucketHistogramMetric>(hb.WithTags(t =>
                {
                    t.Add(SignalRTagKeys.Hub, parts[0]);
                    t.Add(SignalRTagKeys.Transport, parts[1]);
                    if (parts.Length > 2 && !string.IsNullOrEmpty(parts[2])) t.Add(SignalRTagKeys.Fallback, parts[2]);
                }));
                return hb.Build();
            }, this);
            h.Observe(d.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Observes a hub method invocation: duration histogram and outcome counter (ok/error).
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="method">Method name.</param>
    /// <param name="elapsed">Elapsed time of the invocation.</param>
    /// <param name="ok"><see langword="true"/> if the invocation succeeded; otherwise <see langword="false"/>.</param>
    /// <remarks>
    /// Sampling is controlled by <see cref="SignalRMetricsOptions.MethodSampleRate"/>.
    /// Tags:
    /// <list type="bullet">
    /// <item><description>Duration histogram: <c>hub</c>, <c>method</c></description></item>
    /// <item><description>Outcome counter: <c>hub</c>, <c>method</c>, <c>outcome</c> (ok/error)</description></item>
    /// </list>
    /// </remarks>
    public void ObserveMethod(string hub, string method, TimeSpan elapsed, bool ok)
    {
        // Secure sampling (avoids CA5394 "Random is insecure for security" warning)
        if (_opt.MethodSampleRate < 1.0)
        {
            // Produce a cryptographically secure double in [0,1)
            var r = RandomNumberGenerator.GetInt32(0, int.MaxValue) / (double)int.MaxValue;
            if (r > _opt.MethodSampleRate) return;
        }

        var dur = _methodDur.GetOrAdd(new HubMethodKey(hub, method), static (k, s) =>
        {
            var hb = CastBuilder<IBucketHistogramMetric>(s._factory.Histogram(SignalRMetricNames.MethodDuration, "SignalR hub method duration (ms)"));
            hb = CastBuilder<IBucketHistogramMetric>(hb.WithUnit("ms"));
            hb = CastBuilder<IBucketHistogramMetric>(hb.WithBounds(s._opt.LatencyBucketsMs.ToArray()));
            hb = CastBuilder<IBucketHistogramMetric>(hb.WithTags(t => { t.Add(SignalRTagKeys.Hub, k.Hub); t.Add(SignalRTagKeys.Method, k.Method); }));
            return hb.Build();
        }, this);
        dur.Observe(elapsed.TotalMilliseconds);

        var res = _msgTotal.GetOrAdd(new HubMethodOutcomeKey(hub, method, ok ? SignalRTagValues.Ok : SignalRTagValues.Error), static (k, s) =>
        {
            var cb = CastBuilder<ICounterMetric>(s._factory.Counter(SignalRMetricNames.MessagesTotal, "SignalR hub messages total"));
            cb = CastBuilder<ICounterMetric>(cb.WithTags(t => { t.Add(SignalRTagKeys.Hub, k.Hub); t.Add(SignalRTagKeys.Method, k.Method); t.Add(SignalRTagKeys.Outcome, k.Outcome); }));
            return cb.Build();
        }, this);
        res.Increment();
    }

    /// <summary>
    /// Increments the total number of stream items for a hub method, direction-aware.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="method">Method name.</param>
    /// <param name="outbound"><see langword="true"/> for server-to-client; <see langword="false"/> for client-to-server.</param>
    /// <remarks>
    /// Tags: <c>hub</c>, <c>method</c>, <c>direction</c> (in/out).
    /// </remarks>
    public void ObserveStreamItem(string hub, string method, bool outbound)
    {
        var key = $"{hub}|{method}|{(outbound ? SignalRTagValues.DirOut : SignalRTagValues.DirIn)}";
        var c = _streamItems.GetOrAdd(key, static (k, s) =>
        {
            var parts = k.Split('|');
            var cb = CastBuilder<ICounterMetric>(s._factory.Counter(SignalRMetricNames.StreamItemsTotal, "SignalR stream items total"));
            cb = CastBuilder<ICounterMetric>(cb.WithTags(t => { t.Add(SignalRTagKeys.Hub, parts[0]); t.Add(SignalRTagKeys.Method, parts[1]); t.Add(SignalRTagKeys.Direction, parts[2]); }));
            return cb.Build();
        }, this);
        c.Increment();
    }

    /// <summary>
    /// Observes message size for a hub method and direction.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="method">Method name.</param>
    /// <param name="direction">Direction tag value (e.g., <c>in</c> or <c>out</c>).</param>
    /// <param name="bytes">Message size in bytes.</param>
    /// <remarks>
    /// Histogram unit: <c>bytes</c>. Default bounds target typical SignalR payload sizes.
    /// Tags: <c>hub</c>, <c>method</c>, <c>direction</c>.
    /// </remarks>
    public void ObserveMessageSize(string hub, string method, string direction, int bytes)
    {
        var key = new HubMethodDirectionKey(hub, method, direction);
        var h = _msgSize.GetOrAdd(key, static (k, s) =>
        {
            var hb = CastBuilder<IBucketHistogramMetric>(s._factory.Histogram(SignalRMetricNames.MessageSize, "SignalR message size (bytes)"));
            hb = CastBuilder<IBucketHistogramMetric>(hb.WithUnit("bytes"));
            hb = CastBuilder<IBucketHistogramMetric>(hb.WithBounds(new double[] { 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 65536 }));
            hb = CastBuilder<IBucketHistogramMetric>(hb.WithTags(t => { t.Add(SignalRTagKeys.Hub, k.Hub); t.Add(SignalRTagKeys.Method, k.Method); t.Add(SignalRTagKeys.Direction, k.Direction); }));
            return hb.Build();
        }, this);
        h.Observe(bytes);
    }

    /// <summary>
    /// Increments the "group added" counter for a hub/group pair.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="group">Group name.</param>
    /// <remarks>Tags: <c>hub</c>, <c>group</c>.</remarks>
    public void GroupAdded(string hub, string group)
    {
        var c = _groupsAdd.GetOrAdd($"{hub}|{group}", static (k, s) =>
        {
            var parts = k.Split('|');
            var cb = CastBuilder<ICounterMetric>(s._factory.Counter(SignalRMetricNames.GroupsAddTotal, "SignalR groups add total"));
            cb = CastBuilder<ICounterMetric>(cb.WithTags(t => { t.Add(SignalRTagKeys.Hub, parts[0]); t.Add(SignalRTagKeys.Group, parts[1]); }));
            return cb.Build();
        }, this);
        c.Increment();
    }

    /// <summary>
    /// Increments the "group removed" counter for a hub/group pair.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="group">Group name.</param>
    /// <remarks>Tags: <c>hub</c>, <c>group</c>.</remarks>
    public void GroupRemoved(string hub, string group)
    {
        var c = _groupsRemove.GetOrAdd($"{hub}|{group}", static (k, s) =>
        {
            var parts = k.Split('|');
            var cb = CastBuilder<ICounterMetric>(s._factory.Counter(SignalRMetricNames.GroupsRemoveTotal, "SignalR groups remove total"));
            cb = CastBuilder<ICounterMetric>(cb.WithTags(t => { t.Add(SignalRTagKeys.Hub, parts[0]); t.Add(SignalRTagKeys.Group, parts[1]); }));
            return cb.Build();
        }, this);
        c.Increment();
    }

    /// <summary>
    /// Increments the "group sent" counter for a hub/group pair.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="group">Group name.</param>
    /// <remarks>Tags: <c>hub</c>, <c>group</c>.</remarks>
    public void GroupSent(string hub, string group)
    {
        var c = _groupsSend.GetOrAdd($"{hub}|{group}", static (k, s) =>
        {
            var parts = k.Split('|');
            var cb = CastBuilder<ICounterMetric>(s._factory.Counter(SignalRMetricNames.GroupsSendTotal, "SignalR groups send total"));
            cb = CastBuilder<ICounterMetric>(cb.WithTags(t => { t.Add(SignalRTagKeys.Hub, parts[0]); t.Add(SignalRTagKeys.Group, parts[1]); }));
            return cb.Build();
        }, this);
        c.Increment();
    }

    /// <summary>
    /// Sets the active users gauge for a hub.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="count">Current number of active users.</param>
    /// <remarks>Tags: <c>hub</c>.</remarks>
    public void UserActiveGauge(string hub, long count)
    {
        var g = _usersActive.GetOrAdd(hub, static (h, s) =>
        {
            var gb = CastBuilder<IGauge>(s._factory.Gauge(SignalRMetricNames.UsersActiveGauge, "SignalR active users"));
            gb = CastBuilder<IGauge>(gb.WithTags(t => t.Add(SignalRTagKeys.Hub, h)));
            return gb.Build();
        }, this);
        g.SetValue(count);
    }

    /// <summary>
    /// Increments authentication outcome counter for a hub, optionally tagged with a policy name.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="outcome">Outcome tag value (e.g., "success", "failure").</param>
    /// <param name="policy">Optional authorization policy or scheme name.</param>
    /// <remarks>
    /// Tags: <c>hub</c>, <c>outcome</c>, optional <c>policy</c>.
    /// </remarks>
    public void AuthOutcome(string hub, string outcome, string? policy = null)
    {
        var key = $"{hub}|{outcome}|{policy ?? string.Empty}";
        var c = _authOutcome.GetOrAdd(key, static (k, s) =>
        {
            var parts = k.Split('|');
            var cb = CastBuilder<ICounterMetric>(s._factory.Counter(SignalRMetricNames.AuthOutcomeTotal, "SignalR auth outcomes total"));
            cb = CastBuilder<ICounterMetric>(cb.WithTags(t => { t.Add(SignalRTagKeys.Hub, parts[0]); t.Add(SignalRTagKeys.Outcome, parts[1]); if (!string.IsNullOrEmpty(parts[2])) t.Add(SignalRTagKeys.Policy, parts[2]); }));
            return cb.Build();
        }, this);
        c.Increment();
    }

    /// <summary>
    /// Records an error occurrence for a hub/scope/exception-type triple.
    /// </summary>
    /// <param name="hub">Hub name.</param>
    /// <param name="scope">Logical scope (e.g., "invoke", "negotiate", "transport").</param>
    /// <param name="exceptionType">Exception type name. May be truncated by options.</param>
    /// <remarks>
    /// If <see cref="SignalRMetricsOptions.CaptureExceptionType"/> is disabled, this method is a no-op.
    /// The exception type is truncated to <see cref="SignalRMetricsOptions.MaxExceptionTypeLength"/> characters.
    /// Tags: <c>hub</c>, <c>scope</c>, <c>exception_type</c>.
    /// </remarks>
    public void Error(string hub, string scope, string exceptionType)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);

        if (!_opt.CaptureExceptionType)
            return;

        if (exceptionType.Length > _opt.MaxExceptionTypeLength)
        {
            exceptionType = exceptionType.AsSpan(0, _opt.MaxExceptionTypeLength).ToString();
        }

        var key = $"{hub}|{scope}|{exceptionType}";
        var c = _errors.GetOrAdd(key, static (k, s) =>
        {
            var parts = k.Split('|');
            var cb = CastBuilder<ICounterMetric>(s._factory.Counter(SignalRMetricNames.ErrorsTotal, "SignalR errors total"));
            cb = CastBuilder<ICounterMetric>(cb.WithTags(t => { t.Add(SignalRTagKeys.Hub, parts[0]); t.Add(SignalRTagKeys.Scope, parts[1]); t.Add(SignalRTagKeys.ExceptionType, parts[2]); }));
            return cb.Build();
        }, this);
        c.Increment();
    }
}
