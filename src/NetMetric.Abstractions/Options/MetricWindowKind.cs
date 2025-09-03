// <copyright file="MetricWindowKind.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Specifies the type of aggregation window used by a metric.
/// <para>
/// Window kinds determine how observations are grouped and rolled over time,
/// influencing how values are reported and reset.
/// </para>
/// </summary>
public enum MetricWindowKind
{
    /// <summary>
    /// A cumulative window that continuously aggregates values
    /// from the beginning of the process or metric lifetime.
    /// Values are only reset when the process restarts or the metric is cleared.
    /// </summary>
    Cumulative = 0,

    /// <summary>
    /// A tumbling window that groups values into fixed, non-overlapping intervals
    /// (e.g., 1 minute, 5 minutes). At the end of each interval, the window resets
    /// and a new aggregation period begins.
    /// </summary>
    Tumbling = 1
}
