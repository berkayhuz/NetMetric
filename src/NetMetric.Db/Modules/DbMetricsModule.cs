// <copyright file="DbMetricsModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Threading;
using Microsoft.Extensions.Options;

namespace NetMetric.Db.Modules;

/// <summary>
/// Provides first-class metrics instrumentation for database client operations,
/// including active connections, connection pool statistics, query latency,
/// and failed query counts.
/// </summary>
/// <remarks>
/// <para>
/// This module eagerly creates the following metric instruments:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><c>db.client.connections.active</c> (<see cref="IGauge"/>): Current number of active database connections.</description>
///   </item>
///   <item>
///     <description><c>db.client.pool</c> (<see cref="IMultiGauge"/>): Pool-related samples such as size, in-use, and idle counts.</description>
///   </item>
///   <item>
///     <description><c>db.client.query.duration</c> (<see cref="ITimerMetric"/>): Query execution duration measured in milliseconds.</description>
///   </item>
///   <item>
///     <description><c>db.client.query.errors_total</c> (<see cref="ICounterMetric"/>): Cumulative number of failed queries.</description>
///   </item>
/// </list>
/// <para>
/// The module exposes a set of <see cref="IMetricCollector"/> instances via <see cref="GetCollectors"/>,
/// which hosting environments can invoke on their collection cadence.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> Active connection mutations use <see cref="Interlocked"/> to ensure atomicity,
/// and reads use <see cref="Volatile.Read{T}(ref readonly T)"/> for a consistent view. Instruments themselves must be
/// thread-safe per their own contracts.
/// </para>
/// </remarks>
/// <example>
/// The following example shows basic registration and usage:
/// <code language="csharp"><![CDATA[
/// var services = new ServiceCollection();
/// services.Configure<DbMetricsOptions>(opts =>
/// {
///     opts.DefaultTags = new Dictionary<string, string>
///     {
///         ["service.name"] = "orders-api",
///         ["db.system"] = "postgres"
///     };
///     opts.PoolSamplePeriodMs = 5_000;
/// });
///
/// services.AddSingleton<IMetricFactory, DefaultMetricFactory>();
/// services.AddSingleton<DbMetricsModule>();
///
/// var sp = services.BuildServiceProvider();
/// var module = sp.GetRequiredService<DbMetricsModule>();
///
/// // Measure a query with a using-scope:
/// using (module.StartQuery())
/// {
///     // Execute command...
/// }
///
/// // Or equivalently:
/// using var _ = module.StartQuery();
/// // Execute command...
///
/// // Track active connections around a connection lifecycle:
/// module.IncActive();
/// try
/// {
///     // open/use connection...
/// }
/// finally
/// {
///     module.DecActive();
/// }
///
/// // Record a pool sample (provider-specific reader decides when/what):
/// module.AddPoolSample("pool-1", "in_use", 12);
///
/// // Record an error when a query fails:
/// module.IncError();
/// ]]></code>
/// </example>
/// <seealso cref="DbMetricsOptions"/>
/// <seealso cref="IMetricCollector"/>
public sealed class DbMetricsModule : IModule, IModuleLifecycle
{
    private readonly IMetricFactory _f;
    private readonly DbMetricsOptions _opts;

    // Instruments
    private readonly IGauge _activeConnectionsGauge;
    private readonly IMultiGauge _poolGauge;
    private readonly ITimerMetric _queryTimer;
    private readonly ICounterMetric _failedQueries;

    // Counter backing field (distinct from gauge)
    private int _activeConnectionCount;

    /// <summary>
    /// Gets the current active connection count as tracked by the module.
    /// </summary>
    /// <remarks>
    /// Maintained independently from the gauge instrument to enable atomic updates and
    /// contention-free reads by internal collectors.
    /// </remarks>
    internal int ActiveConnections => Volatile.Read(ref _activeConnectionCount);

    /// <summary>
    /// Gets the logical name of this module, used when registering or inspecting modules.
    /// </summary>
    /// <value>The constant value <c>"NetMetric.Db"</c>.</value>
    public string Name => "NetMetric.Db";

    /// <summary>
    /// Initializes a new instance of the <see cref="DbMetricsModule"/> class.
    /// </summary>
    /// <param name="f">The metric factory used to create instruments.</param>
    /// <param name="opts">Typed options controlling module behavior and default tags.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="f"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// If <see cref="DbMetricsOptions.DefaultTags"/> are provided, they are attached during instrument creation.
    /// The query duration timer uses a cumulative window policy by default.
    /// </remarks>
    public DbMetricsModule(IMetricFactory f, IOptions<DbMetricsOptions> opts)
    {
        _f = f ?? throw new ArgumentNullException(nameof(f));
        _opts = opts?.Value ?? new();

        var baseTags = _opts.DefaultTags ?? new Dictionary<string, string>();

        _activeConnectionsGauge = _f.Gauge("db.client.connections.active", "Active DB connections")
            .WithTags(t => { foreach (var kv in baseTags) t.Add(kv.Key, kv.Value); })
            .Build();

        _poolGauge = _f.MultiGauge("db.client.pool", "DB pool stats")
            .WithResetOnGet(true)
            .WithInitialCapacity(16)
            .WithTags(t => { foreach (var kv in baseTags) t.Add(kv.Key, kv.Value); })
            .Build();

        _queryTimer = _f.Timer("db.client.query.duration", "DB query duration (ms)")
            .WithWindow(MetricWindowPolicy.Cumulative)
            .Build();

        _failedQueries = _f.Counter("db.client.query.errors_total", "DB query error count").Build();
    }

    /// <summary>
    /// Creates the collectors that publish metrics for this module.
    /// </summary>
    /// <returns>
    /// A sequence of <see cref="IMetricCollector"/> instances invoked by the hosting environment.
    /// </returns>
    /// <remarks>
    /// The returned collectors include:
    /// <list type="bullet">
    ///   <item><description><see cref="ActiveConnectionsCollector"/> — publishes <see cref="ActiveConnections"/> to <see cref="_activeConnectionsGauge"/>.</description></item>
    ///   <item><description><see cref="PoolCollector"/> — published when <see cref="DbMetricsOptions.PoolSamplePeriodMs"/> &gt; 0.</description></item>
    ///   <item><description><see cref="QueryLatencyCollector"/> — exposes the query duration timer.</description></item>
    ///   <item><description><see cref="FailedQueryCollector"/> — exposes the failed query counter.</description></item>
    /// </list>
    /// </remarks>
    public IEnumerable<IMetricCollector> GetCollectors()
    {
        yield return new ActiveConnectionsCollector(this, _activeConnectionsGauge);
        if (_opts.PoolSamplePeriodMs > 0)
            yield return new PoolCollector(this, _poolGauge, _opts.PoolSamplePeriodMs);
        yield return new QueryLatencyCollector(_queryTimer);
        yield return new FailedQueryCollector(_failedQueries);
    }

    /// <inheritdoc/>
    public void OnInit() { }

    /// <inheritdoc/>
    public void OnBeforeCollect() { }

    /// <inheritdoc/>
    public void OnAfterCollect() { }

    /// <inheritdoc/>
    public void OnDispose() { }

    // ---- Internal instrumentation API ----

    /// <summary>
    /// Starts a timed scope that measures query execution duration using the configured timer.
    /// </summary>
    /// <returns>
    /// An <see cref="IDisposable"/> scope that records the elapsed time on dispose.
    /// </returns>
    /// <remarks>
    /// Prefer the <c>using</c> pattern to ensure disposal even when exceptions occur.
    /// </remarks>
    internal IDisposable StartQuery() => new QueryScope(_queryTimer);

    /// <summary>
    /// Atomically increments the active connection count.
    /// </summary>
    internal void IncActive() => Interlocked.Increment(ref _activeConnectionCount);

    /// <summary>
    /// Atomically decrements the active connection count.
    /// </summary>
    internal void DecActive() => Interlocked.Decrement(ref _activeConnectionCount);

    /// <summary>
    /// Adds a pool-related sample as a sibling measurement to the multi-gauge instrument.
    /// </summary>
    /// <param name="id">Stable identifier for the pool or sub-metric (e.g., pool instance id).</param>
    /// <param name="name">
    /// Logical sample name (for example, <c>size</c>, <c>in_use</c>, <c>idle</c>). Providers may define additional names.
    /// </param>
    /// <param name="value">Numeric value of the sample.</param>
    /// <param name="tags">Optional tags attached to this specific sibling measurement.</param>
    internal void AddPoolSample(string id, string name, double value, IReadOnlyDictionary<string, string>? tags = null)
        => _poolGauge.AddSibling(id, name, value, tags ?? new Dictionary<string, string>());

    /// <summary>
    /// Increments the failed query counter by one.
    /// </summary>
    internal void IncError() => _failedQueries.Increment(1);

    /// <summary>
    /// Disposable scope used to measure query duration with <see cref="ITimerMetric"/>.
    /// </summary>
    private sealed class QueryScope : IDisposable
    {
        private readonly ITimerScope _s;

        /// <summary>
        /// Initializes a new instance of the <see cref="QueryScope"/> class.
        /// </summary>
        /// <param name="t">The timer metric used to create the underlying timing scope.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="t"/> is <see langword="null"/>.
        /// </exception>
        public QueryScope(ITimerMetric t)
        {
            ArgumentNullException.ThrowIfNull(t);
            _s = t.Start();
        }

        /// <summary>
        /// Disposes the scope and records the elapsed duration to the timer metric.
        /// </summary>
        public void Dispose() => _s.Dispose();
    }
}
