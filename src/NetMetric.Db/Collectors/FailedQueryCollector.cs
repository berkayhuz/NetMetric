// <copyright file="FailedQueryCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Db.Collectors;

/// <summary>
/// Collects and exposes a counter metric that tracks the total number of failed database queries.
/// </summary>
/// <remarks>
/// <para>
/// This collector simply returns the provided <see cref="ICounterMetric"/> instance as-is from
/// <see cref="CollectAsync(System.Threading.CancellationToken)"/>; it does not transform, reset,
/// or snapshot the value. The counter itself should be incremented by the application code whenever
/// a query fails (for example due to timeouts, deadlocks, or exceptions thrown by the data provider).
/// </para>
/// <para><strong>Thread Safety:</strong>
/// The collector performs no mutation and is therefore thread-safe by construction. The underlying
/// <see cref="ICounterMetric"/> is expected to be thread-safe according to its contract.
/// </para>
/// <para>
/// If you are using <see cref="Db.Modules.DbMetricsModule"/>, prefer calling
/// <c>DbMetricsModule.IncError()</c> to increment the failure counter in a consistent way across
/// your application.
/// </para>
/// </remarks>
/// <example>
/// The following example demonstrates how to increment the counter upon a query failure and expose
/// the metric via the collector:
/// <code language="csharp"><![CDATA[
/// // Setup (typically in DI/bootstrap)
/// var failedQueryCounter = metricFactory.Counter("db.client.query.errors_total", "DB query error count").Build();
/// var collector = new FailedQueryCollector(failedQueryCounter);
///
/// // In your data access code
/// try
/// {
///     // Execute a DB command...
/// }
/// catch (Exception)
/// {
///     // Record the failure
///     failedQueryCounter.Increment(1);
///     throw;
/// }
///
/// // During a collection cycle run by your host:
/// var metric = await collector.CollectAsync();
/// // 'metric' is the same ICounterMetric instance (or null if unavailable).
/// ]]></code>
/// </example>
/// <seealso cref="ICounterMetric"/>
/// <seealso cref="IMetricCollector"/>
internal sealed class FailedQueryCollector : IMetricCollector
{
    private readonly ICounterMetric _counter;

    /// <summary>
    /// Initializes a new instance of the <see cref="FailedQueryCollector"/> class.
    /// </summary>
    /// <param name="counter">
    /// The underlying counter metric that accumulates the number of failed queries.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="counter"/> is <see langword="null"/>.
    /// </exception>
    public FailedQueryCollector(ICounterMetric counter)
        => _counter = counter ?? throw new ArgumentNullException(nameof(counter));

    /// <summary>
    /// Returns the underlying failed-query counter metric without modification.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A completed task containing the <see cref="ICounterMetric"/> that represents
    /// the number of failed queries, or <see langword="null"/> if not available.
    /// </returns>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
        => Task.FromResult<IMetric?>(_counter);

    /// <summary>
    /// Not supported for <see cref="FailedQueryCollector"/> since it only exposes a counter metric.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="quantiles">Requested quantiles.</param>
    /// <param name="tags">Optional metric tags.</param>
    /// <param name="resetOnGet">Whether the summary resets on retrieval.</param>
    /// <returns>Never returns; this method always throws.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown because <see cref="FailedQueryCollector"/> does not create or manage summary metrics.
    /// </exception>
    ISummaryMetric IMetricCollector.CreateSummary(
        string id, string name, IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
        => throw new NotSupportedException("FailedQueryCollector does not create summaries.");

    /// <summary>
    /// Not supported for <see cref="FailedQueryCollector"/> since it only exposes a counter metric.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="bucketUpperBounds">Bucket upper bounds.</param>
    /// <param name="tags">Optional metric tags.</param>
    /// <returns>Never returns; this method always throws.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown because <see cref="FailedQueryCollector"/> does not create or manage bucket histograms.
    /// </exception>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id, string name, IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
        => throw new NotSupportedException("FailedQueryCollector does not create bucket histograms.");
}
