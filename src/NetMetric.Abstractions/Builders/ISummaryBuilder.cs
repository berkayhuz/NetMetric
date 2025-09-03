// <copyright file="ISummaryBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a fluent builder contract for configuring and creating summary metrics.
/// <para>
/// A summary metric records a stream of observations and provides configurable
/// quantile estimates (e.g., median, 95th percentile). Unlike histograms,
/// summaries do not require predefined bucket boundaries and instead use
/// statistical algorithms to approximate requested quantiles.
/// </para>
/// </summary>
public interface ISummaryBuilder : IInstrumentBuilder<ISummaryMetric>
{
    /// <summary>
    /// Configures the quantiles to be tracked by the summary metric.
    /// </summary>
    /// <param name="quantiles">
    /// An array of target quantiles, each expressed as a fraction between 0.0 and 1.0
    /// (for example, <c>0.5</c> for the median, <c>0.95</c> for the 95th percentile).
    /// </param>
    /// <returns>
    /// The same <see cref="ISummaryBuilder"/> instance for method chaining.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown if any value in <paramref name="quantiles"/> is outside the range [0.0, 1.0].
    /// </exception>
    ISummaryBuilder WithQuantiles(params double[] quantiles);
}
