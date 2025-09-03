// <copyright file="IMetricWindowPolicy.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a contract for configuring how a metric aggregates
/// or rolls over its values across time windows.
/// <para>
/// Windowing policies determine how observations are grouped
/// (for example, sliding or tumbling windows) and how often
/// these groups are rotated or reset.
/// </para>
/// </summary>
public interface IMetricWindowPolicy
{
    /// <summary>
    /// Gets the kind of windowing policy applied to the metric.
    /// <para>
    /// <see cref="MetricWindowKind.Tumbling"/>.
    /// </para>
    /// </summary>
    MetricWindowKind Kind { get; }

    /// <summary>
    /// Gets the time period that governs the windowing behavior.
    /// <para>
    /// For a tumbling window, this represents the fixed duration
    /// before a new window begins.  
    /// For a sliding window, this represents the interval
    /// at which the window advances.
    /// </para>
    /// </summary>
    TimeSpan Period { get; }
}
