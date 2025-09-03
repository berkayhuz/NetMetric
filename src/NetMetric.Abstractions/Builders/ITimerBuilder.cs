// <copyright file="ITimerBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a fluent builder contract for configuring and creating timer metrics.
/// <para>
/// A timer metric measures the duration of operations or code blocks, typically
/// recording values in milliseconds. Timers are often backed by histograms or
/// summaries to provide distribution analysis (e.g., average, percentiles).
/// </para>
/// </summary>
public interface ITimerBuilder : IInstrumentBuilder<ITimerMetric>
{
    /// <summary>
    /// Sets the initial capacity of the underlying histogram used by the timer metric.
    /// <para>
    /// Pre-allocating capacity can reduce memory reallocations and improve performance
    /// when a large number of recorded measurements is expected.
    /// </para>
    /// </summary>
    /// <param name="capacity">
    /// The anticipated number of recorded values to preallocate. Must be greater than or equal to zero.
    /// </param>
    /// <returns>
    /// The same <see cref="ITimerBuilder"/> instance for method chaining.
    /// </returns>
    ITimerBuilder WithHistogramCapacity(int capacity);
}
