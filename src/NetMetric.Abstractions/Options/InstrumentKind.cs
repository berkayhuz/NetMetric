// <copyright file="InstrumentKind.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Specifies the different kinds of metric instruments supported by NetMetric.
/// <para>
/// The instrument kind determines the semantics of how values are reported,
/// aggregated, and interpreted in monitoring and observability systems.
/// </para>
/// </summary>
public enum InstrumentKind
{
    /// <summary>
    /// A metric that represents a value which can arbitrarily go up or down over time,
    /// such as memory usage, queue depth, or active connections.
    /// </summary>
    Gauge = 0,

    /// <summary>
    /// A monotonically increasing count of events, such as requests served,
    /// messages published, or errors encountered.
    /// </summary>
    Counter = 1,

    /// <summary>
    /// A histogram that aggregates observations into predefined buckets,
    /// allowing distribution analysis and approximate percentile calculations.
    /// </summary>
    Histogram = 2,

    /// <summary>
    /// A summary metric that estimates quantiles (e.g., median, 95th percentile)
    /// from a stream of observations without requiring bucket definitions.
    /// </summary>
    Summary = 3,

    /// <summary>
    /// A multi-sample instrument that reports multiple related values together,
    /// each with its own identity and tags (e.g., per-resource or per-connection measurements).
    /// </summary>
    MultiSample = 4,
}
