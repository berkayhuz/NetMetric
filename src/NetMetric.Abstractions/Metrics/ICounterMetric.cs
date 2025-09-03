// <copyright file="ICounterMetric.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a monotonically increasing counter metric.
/// <para>
/// A counter is typically used to represent the number of times an event has occurred,
/// such as requests processed, errors encountered, or messages published.
/// Counter values can only increase (or reset on restart) and are never decremented.
/// </para>
/// </summary>
public interface ICounterMetric : IMetric
{
    /// <summary>
    /// Increments the counter by the specified value.
    /// </summary>
    /// <param name="value">
    /// The amount to increment the counter by.  
    /// Defaults to <c>1</c>.  
    /// Must be a non-negative value.
    /// </param>
    void Increment(long value = 1);
}
