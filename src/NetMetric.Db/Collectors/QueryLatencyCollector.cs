// <copyright file="QueryLatencyCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Db.Collectors;

/// <summary>
/// Collects metrics that represent database query latency using a timer metric.
/// </summary>
/// <remarks>
/// <para>
/// This collector provides read-through access to the underlying <see cref="ITimerMetric"/> that
/// records query execution duration. It is intentionally minimal: the collector does not compute
/// quantiles, summaries, or bucketed histograms itself; instead, exporters or backends can derive
/// those views from the raw timer samples if desired.
/// </para>
/// <para>
/// Typical usage is to acquire the timer via the hosting module (e.g., <c>DbMetricsModule</c>)
/// and measure query execution with a scoped timer (see the <see cref="CollectAsync(System.Threading.CancellationToken)"/>
/// example). The explicit <see cref="IMetricCollector"/> members related to summaries and histograms
/// throw <see cref="NotSupportedException"/> to make the limitations explicit.
/// </para>
/// <para><strong>Thread Safety:</strong> The collector itself is stateless; thread safety characteristics
/// are delegated to the wrapped <see cref="ITimerMetric"/> implementation.</para>
/// </remarks>
/// <example>
/// The following example shows how a data-access layer might time a query using the timer that this
/// collector exposes through the module:
/// <code language="csharp"><![CDATA[
/// // Resolve the module and start a timed scope for a query operation.
/// using (module.StartQuery())
/// {
///     // Execute the database command...
///     await command.ExecuteNonQueryAsync(ct);
/// }
/// // When the scope is disposed, the elapsed time is recorded in the ITimerMetric.
/// ]]></code>
/// </example>
/// <seealso cref="ITimerMetric"/>
/// <seealso cref="IMetricCollector"/>
internal sealed class QueryLatencyCollector : IMetricCollector
{
    private readonly ITimerMetric _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryLatencyCollector"/> class.
    /// </summary>
    /// <param name="timer">The timer metric that measures query latency.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timer"/> is <see langword="null"/>.</exception>
    public QueryLatencyCollector(ITimerMetric timer)
        => _timer = timer ?? throw new ArgumentNullException(nameof(timer));

    /// <summary>
    /// Returns the underlying timer metric used for query latency.
    /// </summary>
    /// <param name="ct">A cancellation token to observe while collecting.</param>
    /// <returns>
    /// A completed task containing the <see cref="ITimerMetric"/> for query latency,
    /// or <see langword="null"/> if unavailable.
    /// </returns>
    /// <remarks>
    /// The returned metric can be used by exporters to read or flush accumulated timing data.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Collector is scheduled/run by the hosting environment:
    /// var metric = await collector.CollectAsync(ct);
    /// // Exporter can now snapshot/publish the timer metric's current window.
    /// ]]></code>
    /// </example>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
        => Task.FromResult<IMetric?>(_timer);

    /// <summary>
    /// Not supported for <see cref="QueryLatencyCollector"/>.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="bucketUpperBounds">Bucket upper bounds.</param>
    /// <param name="tags">Optional metric tags.</param>
    /// <returns>Never returns; this method always throws.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown because <see cref="QueryLatencyCollector"/> does not create bucket histograms.
    /// </exception>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id, string name, IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
        => throw new NotSupportedException("QueryLatencyCollector does not create bucket histograms.");

    /// <summary>
    /// Not supported for <see cref="QueryLatencyCollector"/>.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="quantiles">Requested quantiles.</param>
    /// <param name="tags">Optional metric tags.</param>
    /// <param name="resetOnGet">Whether the summary resets on retrieval.</param>
    /// <returns>Never returns; this method always throws.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown because <see cref="QueryLatencyCollector"/> does not create summaries.
    /// </exception>
    ISummaryMetric IMetricCollector.CreateSummary(
        string id, string name, IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
        => throw new NotSupportedException("QueryLatencyCollector does not create summaries.");
}
