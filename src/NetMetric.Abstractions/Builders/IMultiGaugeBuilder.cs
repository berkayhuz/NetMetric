// <copyright file="IMultiGaugeBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a fluent builder contract for configuring and creating multi-gauge metrics.
/// <para>
/// A multi-gauge allows tracking multiple gauge values under different tag sets
/// or dimensions, making it suitable for scenarios such as per-user, per-connection,
/// or per-resource measurements.
/// </para>
/// </summary>
public interface IMultiGaugeBuilder : IInstrumentBuilder<IMultiGauge>
{
    /// <summary>
    /// Sets the initial capacity for the underlying storage of the multi-gauge.
    /// <para>
    /// Pre-allocating capacity can reduce memory reallocations and improve performance
    /// when the expected number of distinct tag combinations is known in advance.
    /// </para>
    /// </summary>
    /// <param name="capacity">
    /// The anticipated number of unique gauge instances to preallocate.
    /// Must be greater than or equal to zero.
    /// </param>
    /// <returns>
    /// The same <see cref="IMultiGaugeBuilder"/> instance for method chaining.
    /// </returns>
    IMultiGaugeBuilder WithInitialCapacity(int capacity);

    /// <summary>
    /// Configures whether the gauge values should automatically reset after retrieval.
    /// </summary>
    /// <param name="reset">
    /// If <c>true</c>, gauge values are reset to their default after each <c>Get</c> operation.
    /// If <c>false</c>, values are preserved until explicitly modified.
    /// Defaults to <c>true</c>.
    /// </param>
    /// <returns>
    /// The same <see cref="IMultiGaugeBuilder"/> instance for method chaining.
    /// </returns>
    IMultiGaugeBuilder WithResetOnGet(bool reset = true);
}
