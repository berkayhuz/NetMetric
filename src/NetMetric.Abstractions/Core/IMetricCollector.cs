// <copyright file="IMetricCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// All collectors are asynchronous and support cancellation.
/// </summary>
public interface IMetricCollector
{
    /// <summary>
    /// Collects a metric asynchronously.
    /// </summary>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>
    /// A task that completes with the collected metric, or <c>null</c> if no metric was produced.
    /// </returns>
    Task<IMetric?> CollectAsync(CancellationToken ct = default);

    ISummaryMetric CreateSummary(
    string id,
    string name,
    IEnumerable<double> quantiles,
    IReadOnlyDictionary<string, string>? tags = null,
    bool resetOnGet = false);

    IBucketHistogramMetric CreateBucketHistogram(
    string id,
    string name,
    IEnumerable<double> bucketUpperBounds,
    IReadOnlyDictionary<string, string>? tags = null);
}
