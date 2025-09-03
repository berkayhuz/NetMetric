// <copyright file="InstrumentedCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Redis.Collectors;

/// <summary>
/// Wraps an inner metric collector and emits <em>self-metrics</em> that describe the collection pipeline itself.
/// </summary>
/// <remarks>
/// <para>
/// This collector delegates metric collection to an <see cref="IMetricCollector"/> and, for each invocation of
/// <see cref="CollectAsync(System.Threading.CancellationToken)"/>, records the following auxiliary metrics:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
/// <c>redis.collect.duration_ms.{name}</c> (<see cref="ITimerMetric"/>): a timer that measures the duration of the collection.
///     The enclosing <see cref="ITimerMetric.StartMeasurement"/> scope is used to record the elapsed time.
///     </description>
///   </item>
///   <item>
///     <description>
/// <c>redis.collect.errors_total.{name}</c> (<see cref="IGauge"/>): a monotonically increasing counter (backed by an atomic
///     <see cref="long"/>) that reflects the total number of failed collection attempts since startup.
///     </description>
///   </item>
///   <item>
///     <description>
/// <c>redis.collect.last_success_unixtime.{name}</c> (<see cref="IGauge"/>): the Unix epoch timestamp (UTC, seconds) of the
///     most recent successful collection.
///     </description>
///   </item>
///   <item>
///     <description>
/// <c>redis.collect.last_duration_ms.{name}</c> (<see cref="IGauge"/>): the duration, in milliseconds, of the last collection
///     attempt (successful or failed).
///     </description>
///   </item>
/// </list>
/// <para>
/// Thread safety: the error counter is updated using <see cref="Interlocked.Increment(ref long)"/>, allowing safe concurrent usage
/// across multiple threads invoking <see cref="CollectAsync(System.Threading.CancellationToken)"/>.
/// </para>
/// <para>
/// Typical usage is to compose this wrapper around a production collector to gain visibility into its health and performance
/// without altering the collector’s core behavior.
/// </para>
/// <example>
/// <code language="csharp"><![CDATA[
/// // IMetricFactory factory = ...;
/// // IMetricCollector redisCollector = new RedisKeysCollector(factory, ...);
/// 
/// var instrumented = new InstrumentedCollector(factory, "redis_keys", redisCollector);
/// 
/// // Inside your scrape loop / background service:
/// var metric = await instrumented.CollectAsync(ct);
/// if (metric is not null)
/// {
///     // Publish or export the collected metric(s) as appropriate.
/// }
/// // Regardless of success/failure, self-metrics are updated automatically:
/// //   redis.collect.duration_ms.redis_keys
/// //   redis.collect.errors_total.redis_keys
/// //   redis.collect.last_success_unixtime.redis_keys
/// //   redis.collect.last_duration_ms.redis_keys
/// ]]></code>
/// </example>
/// </remarks>
internal sealed class InstrumentedCollector : MetricCollectorBase
{
    private readonly IMetricCollector _inner;
    private readonly string _name;

    private readonly ITimerMetric _duration;
    private readonly IGauge _errorsTotal;
    private readonly IGauge _lastSuccessUnix;
    private readonly IGauge _lastDurationMs;

    private long _errors; // Atomic counter for errors

    /// <summary>
    /// Initializes a new instance of the <see cref="InstrumentedCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create timer and gauge instances for self-instrumentation.</param>
    /// <param name="name">
    /// A short, stable identifier appended to the self-metric names (for example, <c>redis_keys</c>).
    /// If null or whitespace, the value defaults to <c>"unknown"</c>.
    /// </param>
    /// <param name="inner">The inner <see cref="IMetricCollector"/> to be wrapped and instrumented.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The constructor eagerly creates the following self-metrics (names incorporate <paramref name="name"/>):
    /// <list type="bullet">
    ///   <item><description><c>redis.collect.duration_ms.{name}</c> (<see cref="ITimerMetric"/>)</description></item>
    ///   <item><description><c>redis.collect.errors_total.{name}</c> (<see cref="IGauge"/>)</description></item>
    ///   <item><description><c>redis.collect.last_success_unixtime.{name}</c> (<see cref="IGauge"/>)</description></item>
    ///   <item><description><c>redis.collect.last_duration_ms.{name}</c> (<see cref="IGauge"/>)</description></item>
    /// </list>
    /// </remarks>
    public InstrumentedCollector(IMetricFactory factory, string name, IMetricCollector inner) : base(factory)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _name = string.IsNullOrWhiteSpace(name) ? "unknown" : name.Trim();
        _duration = Factory.Timer($"redis.collect.duration_ms.{_name}", "Collector duration (ms)").Build();
        _errorsTotal = Factory.Gauge($"redis.collect.errors_total.{_name}", "Collector error counter (total)").Build();
        _lastSuccessUnix = Factory.Gauge($"redis.collect.last_success_unixtime.{_name}", "Last successful collection time (unix)").Build();
        _lastDurationMs = Factory.Gauge($"redis.collect.last_duration_ms.{_name}", "Last collection duration (ms)").Build();
    }

#pragma warning disable CA1031
    /// <summary>
    /// Executes the wrapped collector and updates self-metrics for duration, success, and errors.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result is the metric produced by the inner collector,
    /// or <see langword="null"/> if the inner collector threw an exception.
    /// </returns>
    /// <remarks>
    /// <para>
    /// On success:
    /// <list type="bullet">
    ///   <item><description>Updates <c>last_success_unixtime</c> with the current UTC epoch seconds.</description></item>
    ///   <item><description>Records <c>last_duration_ms</c> with the elapsed time for this invocation.</description></item>
    ///   <item><description>Emits a timing sample to <c>duration_ms</c> via the timer scope.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// On failure:
    /// <list type="bullet">
    ///   <item><description>Atomically increments and publishes <c>errors_total</c>.</description></item>
    ///   <item><description>Records <c>last_duration_ms</c> for the failed attempt.</description></item>
    ///   <item><description>Returns <see langword="null"/> to indicate collection failure.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">The operation was canceled via <paramref name="ct"/>.</exception>
    public override async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        using (_duration.StartMeasurement())
        {
            try
            {
                var metric = await _inner.CollectAsync(ct).ConfigureAwait(false);

                _lastSuccessUnix.SetValue(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                return metric;
            }
            catch
            {
                var total = Interlocked.Increment(ref _errors);

                _errorsTotal.SetValue(total);

                return null;
            }
            finally
            {
                sw.Stop();
                _lastDurationMs.SetValue((long)sw.Elapsed.TotalMilliseconds);
            }
        }
    }
#pragma warning restore CA1031
}
