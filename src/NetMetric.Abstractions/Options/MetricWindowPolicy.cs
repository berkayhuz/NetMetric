// <copyright file="MetricWindowPolicy.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Represents a policy that defines how a metric aggregates values over time.
/// <para>
/// A window policy specifies whether metrics are aggregated cumulatively
/// or reset at regular, fixed intervals (tumbling).
/// </para>
/// </summary>
public readonly record struct MetricWindowPolicy(MetricWindowPolicy.WindowKind Kind, TimeSpan Period)
    : IMetricWindowPolicy
{
    /// <summary>
    /// A cumulative window policy that aggregates values for the entire
    /// lifetime of the metric. Values are only reset when the process
    /// restarts or the metric is explicitly cleared.
    /// </summary>
    public static readonly MetricWindowPolicy Cumulative = new(WindowKind.Cumulative, default);

    /// <summary>
    /// Creates a tumbling window policy that resets values at the specified interval.
    /// </summary>
    /// <param name="period">
    /// The duration of each window. At the end of every interval,
    /// the window resets and a new aggregation period begins.
    /// </param>
    /// <returns>
    /// A <see cref="MetricWindowPolicy"/> representing the tumbling window.
    /// </returns>
    public static MetricWindowPolicy Tumbling(TimeSpan period) => new(WindowKind.Tumbling, period);

    /// <summary>
    /// Enumerates the supported kinds of metric windows.
    /// </summary>
    public enum WindowKind
    {
        /// <summary>
        /// Aggregates values continuously without resetting,
        /// except when the process restarts or the metric is cleared.
        /// </summary>
        Cumulative = 0,

        /// <summary>
        /// Aggregates values in fixed, non-overlapping intervals (e.g., 1 minute).
        /// After each interval ends, the window resets and a new one begins.
        /// </summary>
        Tumbling = 1
    }

    /// <inheritdoc />
    MetricWindowKind IMetricWindowPolicy.Kind =>
        Kind == WindowKind.Cumulative ? MetricWindowKind.Cumulative : MetricWindowKind.Tumbling;
}
