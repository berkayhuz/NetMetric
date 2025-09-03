// <copyright file="IBucketHistogramBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a fluent builder contract for configuring and creating
/// bucket-based histogram metrics.
/// <para>
/// A bucket histogram aggregates numeric observations into predefined
/// ranges ("buckets") and records counts per bucket, enabling efficient
/// percentile/quantile approximations and distribution analysis.
/// </para>
/// </summary>
public interface IBucketHistogramBuilder : IInstrumentBuilder<IBucketHistogramMetric>
{
    /// <summary>
    /// Configures a set of linearly spaced buckets.
    /// </summary>
    /// <param name="start">
    /// The lower bound of the first bucket.
    /// </param>
    /// <param name="width">
    /// The width of each bucket interval.
    /// </param>
    /// <param name="count">
    /// The total number of buckets to create.
    /// </param>
    /// <returns>
    /// The same <see cref="IBucketHistogramBuilder"/> instance for chaining.
    /// </returns>
    IBucketHistogramBuilder Linear(double start, double width, int count);

    /// <summary>
    /// Configures a set of exponentially growing buckets.
    /// </summary>
    /// <param name="start">
    /// The lower bound of the first bucket.
    /// </param>
    /// <param name="factor">
    /// The growth factor applied to each successive bucket width.
    /// Must be greater than 1.0.
    /// </param>
    /// <param name="count">
    /// The total number of buckets to create.
    /// </param>
    /// <returns>
    /// The same <see cref="IBucketHistogramBuilder"/> instance for chaining.
    /// </returns>
    IBucketHistogramBuilder Exponential(double start, double factor, int count);

    /// <summary>
    /// Configures an explicit set of bucket boundaries.
    /// </summary>
    /// <param name="bounds">
    /// The ordered list of bucket upper bounds. Values must be strictly increasing.
    /// </param>
    /// <returns>
    /// The same <see cref="IBucketHistogramBuilder"/> instance for chaining.
    /// </returns>
    IBucketHistogramBuilder WithBounds(params double[] bounds);
}
