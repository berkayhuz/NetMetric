// <copyright file="HistogramBoundsExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AspNetCore.Extensions;

/// <summary>
/// Provides extension methods for configuring histogram bucket bounds
/// on <see cref="IBucketHistogramMetric"/> instruments.
/// </summary>
/// <remarks>
/// <para>
/// These helpers act as conveniences over builders that implement
/// <see cref="IBucketHistogramBuilder"/>. When the provided builder does not support bucket
/// configuration, the call fails fast with a <see cref="NotSupportedException"/>.
/// </para>
/// <para><strong>Best practices:</strong> Bucket boundaries should be non-negative, strictly
/// increasing, and appropriate for the instrument's unit (for example, milliseconds for
/// request duration, bytes for payload sizes).
/// </para>
/// <para><strong>Fluent usage:</strong> The returned builder may be the same instance or a
/// more specialized builder; either way it is safe to continue chaining configuration calls.
/// </para>
/// </remarks>
/// <seealso cref="IBucketHistogramMetric"/>
/// <seealso cref="IBucketHistogramBuilder"/>
public static class HistogramBoundsExtensions
{
    /// <summary>
    /// Configures the histogram to use the specified bucket bounds.
    /// </summary>
    /// <param name="builder">The instrument builder for <see cref="IBucketHistogramMetric"/>.</param>
    /// <param name="bounds">
    /// An array of bucket boundaries applied to the histogram. Must be non-empty to take effect.
    /// Values should be sorted in ascending order and expressed in the instrument's unit.
    /// </param>
    /// <returns>
    /// The builder (potentially specialized) with bucket bounds applied, enabling further fluent configuration.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the provided builder does not support bucket bounds (i.e., is not an <see cref="IBucketHistogramBuilder"/>),
    /// or when <paramref name="bounds"/> is empty.
    /// </exception>
    /// <example>
    /// Configure custom millisecond buckets for a request-duration histogram:
    /// <code>
    /// var histogram = factory.Histogram("http.request.duration", "Request duration (ms)")
    ///                        .UseBucketBounds(new[] { 0.5, 1, 2, 4, 8, 15, 30, 60, 120, 250, 500, 1000 })
    ///                        .Build();
    /// </code>
    /// </example>
    public static IInstrumentBuilder<IBucketHistogramMetric> UseBucketBounds(
        this IInstrumentBuilder<IBucketHistogramMetric> builder, double[] bounds)
    {
        if (builder is IBucketHistogramBuilder hb && bounds is { Length: > 0 })
        {
            hb.WithBounds(bounds);
            return hb;
        }
        throw new NotSupportedException("This histogram builder does not support bucket bounds.");
    }
}
