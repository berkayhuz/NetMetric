// <copyright file="PoolCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Db.Collectors;

/// <summary>
/// Collects database connection pool metrics at a controlled sampling interval,
/// exposing them through a multi-gauge instrument.
/// </summary>
/// <remarks>
/// <para>
/// This collector does not itself query providers. Instead, provider-specific readers
/// (e.g., ADO.NET, Npgsql, SqlClient, MySql) are expected to observe pool state and push
/// samples into <see cref="DbMetricsModule.AddPoolSample(string, string, double, System.Collections.Generic.IReadOnlyDictionary{string, string}?)"/>
/// using the shared <see cref="DbMetricsModule"/> instance.
/// </para>
/// <para>
/// The collector ensures sampling is rate-limited by the configured sampling interval (<c>_periodMs</c>)
/// so that pool readers do not publish too frequently. The throttling key is based on
/// <see cref="Environment.TickCount64"/> and is safe for long-running services.
/// </para>
/// <para><strong>Thread safety:</strong> The sampling window is guarded by an atomic timestamp
/// (<see cref="_next"/>) updated via <see cref="Interlocked.Exchange(ref long, long)"/>.
/// The underlying <see cref="IMultiGauge"/> must be thread-safe per its contract.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Registration in your DI/bootstrap:
/// services.AddSingleton<IMetricFactory, DefaultMetricFactory>();
/// services.AddSingleton<DbMetricsModule>();
///
/// // Module creates collectors internally; PoolCollector is yielded when PoolSamplePeriodMs > 0.
/// var module = serviceProvider.GetRequiredService<DbMetricsModule>();
///
/// // A provider-specific reader (pseudo-code) scheduled by a Timer/BackgroundService:
/// void ReadPoolAndPublish()
/// {
///     // Example measurements for a pool named "default":
///     module.AddPoolSample("default", "size",   currentSize);
///     module.AddPoolSample("default", "in_use", inUse);
///     module.AddPoolSample("default", "idle",   currentSize - inUse);
/// }
/// ]]></code>
/// </example>
internal sealed class PoolCollector : IMetricCollector
{
    private readonly DbMetricsModule _m;
    private readonly IMultiGauge _mg;
    private readonly int _periodMs;
    private long _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="PoolCollector"/> class.
    /// </summary>
    /// <param name="m">The <see cref="DbMetricsModule"/> used by provider readers to push pool samples.</param>
    /// <param name="mg">The multi-gauge instrument that aggregates pool measurements.</param>
    /// <param name="periodMs">The minimum sampling interval, in milliseconds.</param>
    /// <remarks>
    /// The <paramref name="mg"/> instrument is returned by <see cref="CollectAsync(System.Threading.CancellationToken)"/>
    /// so the hosting environment can export accumulated sibling measurements.
    /// </remarks>
    public PoolCollector(DbMetricsModule m, IMultiGauge mg, int periodMs)
        => (_m, _mg, _periodMs) = (m, mg, periodMs);

    /// <summary>
    /// Returns the multi-gauge with the latest pool measurements, enforcing the sampling window.
    /// </summary>
    /// <param name="ct">A cancellation token observed while collecting (no blocking occurs).</param>
    /// <returns>
    /// A completed task containing the <see cref="IMultiGauge"/> that represents pool metrics,
    /// or <see langword="null"/> if the instrument is unavailable.
    /// </returns>
    /// <remarks>
    /// If the current time is past the next sampling deadline, the deadline is extended by
    /// <c>periodMs</c>. Provider-specific readers are expected to publish via
    /// <see cref="DbMetricsModule.AddPoolSample(string, string, double, System.Collections.Generic.IReadOnlyDictionary{string, string}?)"/>.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var now = Environment.TickCount64;
        if (now >= Interlocked.Read(ref _next))
        {
            // Provider-specific readers push pool samples via DbMetricsModule.AddPoolSample(...)
            Interlocked.Exchange(ref _next, now + _periodMs);
        }
        return Task.FromResult<IMetric?>(_mg);
    }

    /// <summary>
    /// Not supported for <see cref="PoolCollector"/>.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="quantiles">Requested quantiles.</param>
    /// <param name="tags">Optional metric tags.</param>
    /// <param name="resetOnGet">Whether the summary resets on retrieval.</param>
    /// <returns>Never returns; this method always throws.</returns>
    /// <exception cref="System.NotSupportedException">
    /// Thrown because <see cref="PoolCollector"/> does not create summaries.
    /// </exception>
    ISummaryMetric IMetricCollector.CreateSummary(
        string id, string name, IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
        => throw new System.NotSupportedException("PoolCollector does not create summaries.");

    /// <summary>
    /// Not supported for <see cref="PoolCollector"/>.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="bucketUpperBounds">Bucket upper bounds.</param>
    /// <param name="tags">Optional metric tags.</param>
    /// <returns>Never returns; this method always throws.</returns>
    /// <exception cref="System.NotSupportedException">
    /// Thrown because <see cref="PoolCollector"/> does not create bucket histograms.
    /// </exception>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id, string name, IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
        => throw new System.NotSupportedException("PoolCollector does not create bucket histograms.");
}
