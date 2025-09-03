// <copyright file="BucketHistogramBuilderExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Exposes <see cref="IBucketHistogramBuilder"/> features
/// through the generic <see cref="IInstrumentBuilder{IBucketHistogramMetric}"/>.
/// </summary>
public static class BucketHistogramBuilderExtensions
{
    /// <summary>
    /// Applies the specified bucket boundaries to the histogram.
    /// </summary>
    /// <param name="builder">The histogram builder.</param>
    /// <param name="bounds">The bucket boundaries to use.</param>
    /// <returns>The original builder (for fluent usage).</returns>
    public static IInstrumentBuilder<IBucketHistogramMetric> WithBounds(
        this IInstrumentBuilder<IBucketHistogramMetric> builder,
        params double[] bounds)
    {
        if (builder is IBucketHistogramBuilder typed)
        {
            typed.WithBounds(bounds);

            return typed;
        }

        throw new NotSupportedException($"Underlying builder does not support bucket bounds. Actual: {builder?.GetType().FullName ?? "null"}");
    }

    /// <summary>
    /// An alias for <c>WithBounds</c> provided for backward compatibility.
    /// </summary>
    /// <param name="builder">The histogram builder.</param>
    /// <param name="bounds">The bucket boundaries to use.</param>
    /// <returns>The original builder (for fluent usage).</returns>
    public static IInstrumentBuilder<IBucketHistogramMetric> UseBucketBounds(
        this IInstrumentBuilder<IBucketHistogramMetric> builder,
        double[] bounds)
        => builder.WithBounds(bounds);
}
