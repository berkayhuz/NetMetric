// <copyright file="IQuantileEstimator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a contract for an online (streaming) quantile estimator.
/// <para>
/// A quantile estimator processes values incrementally as they arrive and
/// provides approximate quantile calculations without storing the entire dataset.
/// Typical use cases include latency monitoring, percentile-based alerting,
/// and other streaming analytics scenarios.
/// </para>
/// </summary>
public interface IQuantileEstimator
{
    /// <summary>
    /// Gets the total number of observations processed so far.
    /// </summary>
    long Count { get; }

    /// <summary>
    /// Adds a new sample value into the estimator.
    /// </summary>
    /// <param name="value">
    /// The numeric sample to incorporate into the quantile estimation.
    /// </param>
    void Add(double value);

    /// <summary>
    /// Returns the estimated quantile for the specified probability <paramref name="q"/>.
    /// </summary>
    /// <param name="q">
    /// The target quantile, expressed as a fraction between 0.0 and 1.0
    /// (e.g., 0.5 for the median, 0.95 for the 95th percentile).
    /// </param>
    /// <returns>
    /// The estimated value at the requested quantile.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="q"/> is outside the valid range [0.0, 1.0].
    /// </exception>
    double GetQuantile(double q);

    /// <summary>
    /// Resets the internal state of the estimator,
    /// discarding all previously processed observations.
    /// </summary>
    void Reset();
}
