// <copyright file="IisMetricSet.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.IIS.Internal;

/// <summary>
/// Provides lazily constructed metric instruments and fast-path update methods
/// for IIS (Internet Information Services) hosting signals.
/// </summary>
/// <remarks>
/// <para>
/// Instruments are created on first use via <see cref="Lazy{T}"/> to avoid unnecessary
/// allocations and registration costs during application startup. This is especially
/// beneficial in high-throughput web applications where cold-start performance matters.
/// </para>
/// <para>
/// <strong>Thread-safety:</strong> All public members are safe for concurrent use. The
/// internally maintained active-connection count uses <see cref="Interlocked"/> to ensure
/// correctness under contention. Per-reason error counters are cached in a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> so that repeated updates avoid additional
/// allocations and registry lookups.
/// </para>
/// <para>
/// <strong>Semantics:</strong>
/// <list type="bullet">
///   <item><description><see cref="ConnectionStart"/> increments the total connections counter and updates the active connections gauge.</description></item>
///   <item><description><see cref="ConnectionStop"/> decrements the active connections gauge (clamped to zero if it would become negative).</description></item>
///   <item><description><see cref="Request"/> increments the total HTTP requests counter.</description></item>
///   <item><description><see cref="Error(string)"/> increments a per-reason errors counter, tagged with the provided reason.</description></item>
///   <item><description><see cref="RecordListenerFault"/> increments the listener-faults counter for defensive monitoring.</description></item>
/// </list>
/// </para>
/// <para>
/// Typical event sources that drive these methods include IIS / ASP.NET Core Module ETW providers,
/// translated by <see cref="Diagnostics.IisEventListener"/> into metric updates.
/// </para>
/// </remarks>
/// <example>
/// <para>
/// <strong>Registering in DI and using in an IIS listener</strong>
/// </para>
/// <code language="csharp"><![CDATA[
/// // In your composition root:
/// services.AddSingleton<IisMetricSet>();
///
/// // Elsewhere (e.g., inside an EventListener callback):
/// _iisMetricSet.ConnectionStart();   // when a new connection is accepted
/// _iisMetricSet.Request();           // when a request begins
/// _iisMetricSet.Error(IisTagKeys.Reason_Timeout);
/// _iisMetricSet.ConnectionStop();    // when a connection is closed
/// ]]></code>
/// <para>
/// <strong>Exported metric names</strong> are defined in <see cref="IisMetricNames"/> and include:
/// <list type="bullet">
/// <item><description><c>iis.connections.active</c> (gauge)</description></item>
/// <item><description><c>iis.connections.total</c> (counter)</description></item>
/// <item><description><c>iis.requests.total</c> (counter)</description></item>
/// <item><description><c>iis.errors.total{reason}</c> (counter)</description></item>
/// <item><description><c>iis.listener.faults</c> (counter)</description></item>
/// </list>
/// Use <see cref="IisTagKeys"/> for canonical tag values such as <c>bad_request</c>, <c>timeout</c>, <c>reset</c>, and <c>app_error</c>.
/// </para>
/// </example>
/// <seealso cref="IisMetricNames"/>
/// <seealso cref="IisTagKeys"/>
/// <seealso cref="Diagnostics.IisEventListener"/>
public sealed class IisMetricSet
{
    private readonly IMetricFactory _f;

    private readonly Lazy<IGauge> _active;
    private readonly Lazy<ICounterMetric> _total;
    private readonly Lazy<ICounterMetric> _requests;
    private readonly Lazy<ICounterMetric> _faults;

    private readonly ConcurrentDictionary<string, ICounterMetric> _errors = new();

    private long _activeCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="IisMetricSet"/> class.
    /// </summary>
    /// <param name="f">The metric factory used to construct counters and gauges.</param>
    /// <remarks>
    /// Instruments are created lazily upon first use to minimize startup overhead.
    /// </remarks>
    public IisMetricSet(IMetricFactory f)
    {
        _f = f;

        _active = new(() => _f.Gauge(IisMetricNames.ConnActive, "IIS active connections").Build());
        _total = new(() => _f.Counter(IisMetricNames.ConnTotal, "IIS total connections").Build());
        _requests = new(() => _f.Counter(IisMetricNames.Requests, "IIS requests total").Build());
        _faults = new(() => _f.Counter(IisMetricNames.ListenerFaults, "IIS listener faults").Build());
    }

    /// <summary>
    /// Records the start of a new connection: increments the total connection counter
    /// and updates the active connections gauge.
    /// </summary>
    /// <remarks>
    /// This method is typically invoked when the IIS provider emits a
    /// <c>ConnectionStart</c> event.
    /// </remarks>
    public void ConnectionStart()
    {
        var v = Interlocked.Increment(ref _activeCount);
        _active.Value.SetValue(v);
        _total.Value.Increment();
    }

    /// <summary>
    /// Records the end of a connection: decrements the active connections gauge.
    /// </summary>
    /// <remarks>
    /// If the internal counter would become negative due to out-of-order calls,
    /// it is clamped back to zero to keep the gauge non-negative. This preserves
    /// monotonic correctness of the gauge even under rare event reordering.
    /// </remarks>
    public void ConnectionStop()
    {
        var v = Interlocked.Add(ref _activeCount, -1);
        if (v < 0)
        {
            Interlocked.Exchange(ref _activeCount, 0);
            v = 0;
        }
        _active.Value.SetValue(v);
    }

    /// <summary>
    /// Increments the total HTTP requests counter.
    /// </summary>
    /// <remarks>
    /// This method is typically invoked when a <c>RequestStart</c> event is observed.
    /// </remarks>
    public void Request() => _requests.Value.Increment();

    /// <summary>
    /// Increments an error counter tagged with the specified <paramref name="reason"/>.
    /// </summary>
    /// <param name="reason">
    /// A short, stable label describing the error cause (used as a tag value), e.g.
    /// <see cref="IisTagKeys.Reason_BadRequest"/>, <see cref="IisTagKeys.Reason_Timeout"/>,
    /// <see cref="IisTagKeys.Reason_Reset"/>, or <see cref="IisTagKeys.Reason_AppError"/>.
    /// </param>
    /// <remarks>
    /// The per-reason counter is created on first use and cached for subsequent
    /// increments to avoid repeated instrument registration overhead.
    /// </remarks>
    public void Error(string reason)
    {
        var c = _errors.GetOrAdd(reason, r =>
            _f.Counter(IisMetricNames.Errors, "IIS errors total")
              .WithTags(t => t.Add(IisTagKeys.Reason, r))
              .Build());
        c.Increment();
    }

    /// <summary>
    /// Increments the listener-faults counter.
    /// </summary>
    /// <remarks>
    /// Intended for defensive monitoring of the metrics listener itself. This
    /// value should ideally remain near zero; increases may indicate issues in
    /// event handling or provider stability.
    /// </remarks>
    public void RecordListenerFault() => _faults.Value.Increment();
}
