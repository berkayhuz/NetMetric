// <copyright file="ActiveConnectionsCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Db.Collectors;

/// <summary>
/// Collects and publishes a point-in-time gauge value representing the number of
/// currently active database connections as tracked by the associated
/// <see cref="NetMetric.Db.Modules.DbMetricsModule"/>.
/// </summary>
/// <remarks>
/// <para>
/// This collector reads the latest active-connection count from
/// <see cref="NetMetric.Db.Modules.DbMetricsModule"/> and writes it into a
/// provided <see cref="NetMetric.Abstractions.IGauge"/> instrument during each
/// collection cycle.
/// </para>
/// <para>
/// The collector is intentionally minimal: it neither starts background tasks nor
/// maintains additional state beyond references to the module and gauge.
/// </para>
/// <para><strong>Thread safety:</strong> The module exposes the active-connection
/// count via atomic reads; setting the gauge value is expected to be thread-safe
/// according to the <c>IGauge</c> contract.
/// </para>
/// </remarks>
/// <example>
/// The following example registers the DB metrics module and uses a hosting loop
/// to drive periodic collection (error handling omitted for brevity):
/// <code language="csharp"><![CDATA[
/// var services = new ServiceCollection();
/// services.AddSingleton<IMetricFactory, DefaultMetricFactory>();
/// services.AddSingleton<DbMetricsModule>();
///
/// var sp = services.BuildServiceProvider();
/// var module = sp.GetRequiredService<DbMetricsModule>();
///
/// // Build instruments and collectors once
/// var collectors = module.GetCollectors().ToList();
///
/// // Somewhere in your DB access layer:
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
/// // Periodic collection (e.g., by a hosted service)
/// foreach (var c in collectors)
/// {
///     // Each collector returns its metric; exporters can pull and publish them
///     var metric = await c.CollectAsync();
/// }
/// ]]></code>
/// </example>
internal sealed class ActiveConnectionsCollector : NetMetric.Abstractions.IMetricCollector
{
    private readonly NetMetric.Db.Modules.DbMetricsModule _m;
    private readonly NetMetric.Abstractions.IGauge _g;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActiveConnectionsCollector"/> class.
    /// </summary>
    /// <param name="m">
    /// The <see cref="NetMetric.Db.Modules.DbMetricsModule"/> that tracks active DB connections.
    /// </param>
    /// <param name="g">
    /// The <see cref="NetMetric.Abstractions.IGauge"/> instrument to receive the active connection count.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="m"/> or <paramref name="g"/> is <see langword="null"/>.
    /// </exception>
    public ActiveConnectionsCollector(NetMetric.Db.Modules.DbMetricsModule m, NetMetric.Abstractions.IGauge g)
        => (_m, _g) = (m ?? throw new System.ArgumentNullException(nameof(m)),
                       g ?? throw new System.ArgumentNullException(nameof(g)));

    /// <summary>
    /// Reads the current number of active database connections from the module and
    /// updates the gauge to that value.
    /// </summary>
    /// <param name="ct">A <see cref="System.Threading.CancellationToken"/> to observe.</param>
    /// <returns>
    /// A completed task whose result is the updated <see cref="NetMetric.Abstractions.IMetric"/>
    /// (i.e., the same <see cref="NetMetric.Abstractions.IGauge"/> instance).
    /// </returns>
    /// <remarks>
    /// If the collector cannot produce a metric (e.g., due to shutdown), it may return
    /// <see langword="null"/>; current implementation always returns the gauge.
    /// </remarks>
    public System.Threading.Tasks.Task<NetMetric.Abstractions.IMetric?> CollectAsync(System.Threading.CancellationToken ct = default)
    {
        _g.SetValue(_m.ActiveConnections);
        return System.Threading.Tasks.Task.FromResult<NetMetric.Abstractions.IMetric?>(_g);
    }

    /// <summary>
    /// Not supported for <see cref="ActiveConnectionsCollector"/>.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="quantiles">Requested quantiles.</param>
    /// <param name="tags">Optional metric tags.</param>
    /// <param name="resetOnGet">Whether the summary resets on retrieval.</param>
    /// <returns>This method never returns.</returns>
    /// <exception cref="System.NotSupportedException">
    /// Always thrown because <see cref="ActiveConnectionsCollector"/> does not create summary metrics.
    /// </exception>
    NetMetric.Abstractions.ISummaryMetric NetMetric.Abstractions.IMetricCollector.CreateSummary(
        string id, string name, System.Collections.Generic.IEnumerable<double> quantiles,
        System.Collections.Generic.IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
        => throw new System.NotSupportedException("ActiveConnectionsCollector does not create summaries.");

    /// <summary>
    /// Not supported for <see cref="ActiveConnectionsCollector"/>.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="bucketUpperBounds">Bucket upper bounds.</param>
    /// <param name="tags">Optional metric tags.</param>
    /// <returns>This method never returns.</returns>
    /// <exception cref="System.NotSupportedException">
    /// Always thrown because <see cref="ActiveConnectionsCollector"/> does not create bucket histograms.
    /// </exception>
    NetMetric.Abstractions.IBucketHistogramMetric NetMetric.Abstractions.IMetricCollector.CreateBucketHistogram(
        string id, string name, System.Collections.Generic.IEnumerable<double> bucketUpperBounds,
        System.Collections.Generic.IReadOnlyDictionary<string, string>? tags)
        => throw new System.NotSupportedException("ActiveConnectionsCollector does not create bucket histograms.");
}
