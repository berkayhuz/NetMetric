// <copyright file="IMetricMetadata.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines the common metadata associated with a metric instrument.
/// <para>
/// Metadata provides identifying and descriptive information about a metric,
/// including its unique key, human-readable name, unit of measurement,
/// description, and instrument kind.
/// </para>
/// </summary>
public interface IMetricMetadata
{
    /// <summary>
    /// Gets the stable unique identifier for the metric.
    /// <para>
    /// This ID is intended to be immutable and suitable for registry lookups,
    /// serialization, and long-term storage.
    /// </para>
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the human-readable name of the metric.
    /// <para>
    /// Names are intended for display in dashboards, logs, or user interfaces
    /// and may not be guaranteed to be unique across all metrics.
    /// </para>
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the unit of measurement for the metric, if defined.
    /// <para>
    /// Examples include <c>"ms"</c> for milliseconds, <c>"%"</c> for percentages,
    /// or <c>"bytes"</c> for memory sizes.
    /// May be <see langword="null"/> if no unit is specified.
    /// </para>
    /// </summary>
    string? Unit { get; }

    /// <summary>
    /// Gets the optional description of the metric.
    /// <para>
    /// The description provides additional context or explanation about what
    /// the metric represents. May be <see langword="null"/>.
    /// </para>
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Gets the kind of instrument represented by the metric.
    /// <para>
    /// Examples include <see cref="InstrumentKind.Gauge"/>,
    /// <see cref="InstrumentKind.Counter"/>,
    /// <see cref="InstrumentKind.Histogram"/>,
    /// <see cref="InstrumentKind.Summary"/>, and others.
    /// </para>
    /// </summary>
    InstrumentKind Kind { get; }
}
