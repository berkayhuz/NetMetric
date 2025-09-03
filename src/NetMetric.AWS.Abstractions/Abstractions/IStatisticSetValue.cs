// <copyright file="IStatisticSetValue.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AWS.Abstractions.Abstractions;

/// <summary>
/// Represents a statistical summary of a set of metric values.
/// Provides the sample count, sum, minimum, and maximum values, which
/// can be used to construct an Amazon CloudWatch <c>StatisticSet</c>.
/// </summary>
/// <remarks>
/// <para>
/// Implementations of this interface expose aggregated statistical
/// information about a distribution of metric values. These values
/// are typically consumed by exporters that support distribution metrics
/// (e.g., a CloudWatch metric exporter).
/// </para>
/// <para>
/// The properties should reflect the entire sample set:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="SampleCount"/> — total number of observations</description></item>
///   <item><description><see cref="Sum"/> — arithmetic sum of all observations</description></item>
///   <item><description><see cref="Min"/> — smallest observed value</description></item>
///   <item><description><see cref="Max"/> — largest observed value</description></item>
/// </list>
/// <para>
/// <b>Conventions &amp; constraints (CloudWatch-friendly):</b>
/// </para>
/// <list type="number">
///   <item><description><see cref="SampleCount"/> SHOULD be &gt; 0 for a valid distribution.</description></item>
///   <item><description>If <see cref="SampleCount"/> is 1, then <see cref="Min"/> and <see cref="Max"/> SHOULD equal the single value and <see cref="Sum"/> SHOULD equal that value.</description></item>
///   <item><description>When <see cref="SampleCount"/> &gt; 1, it SHOULD hold that <c>Min ≤ Max</c> and typically <c>Sum ≥ Min</c> and <c>Sum ≤ Max × SampleCount</c>.</description></item>
/// </list>
/// <para>
/// Exporters MAY validate and normalize values (e.g., clamp impossible ranges or
/// drop invalid statistic sets) before sending to a backend.
/// </para>
/// </remarks>
/// <example>
/// The following example shows a trivial implementation that can be
/// consumed by a CloudWatch exporter to build a <c>StatisticSet</c>.
/// <code language="csharp"><![CDATA[
/// public sealed class StatisticSetValue : IStatisticSetValue
/// {
///     public StatisticSetValue(double count, double sum, double min, double max)
///     {
///         SampleCount = count;
///         Sum = sum;
///         Min = min;
///         Max = max;
///     }
///
///     public double SampleCount { get; }
///     public double Sum { get; }
///     public double Min { get; }
///     public double Max { get; }
/// }
///
/// // Usage
/// var stats = new StatisticSetValue(
///     count: 5,
///     sum:   123.4,
///     min:   10.0,
///     max:   50.0);
/// ]]></code>
/// </example>
/// <example>
/// Computing a statistic set from raw samples:
/// <code language="csharp"><![CDATA[
/// static IStatisticSetValue FromSamples(IEnumerable<double> samples)
/// {
///     double sum = 0;
///     double? min = null, max = null;
///     long count = 0;
///
///     foreach (var v in samples)
///     {
///         if (min is null || v < min) min = v;
///         if (max is null || v > max) max = v;
///         sum += v;
///         count++;
///     }
///
///     if (count == 0) return new StatisticSetValue(0, 0, 0, 0); // empty set
///     return new StatisticSetValue(count, sum, min!.Value, max!.Value);
/// }
/// ]]></code>
/// </example>
public interface IStatisticSetValue
{
    /// <summary>
    /// Gets the number of samples (observations) in the set.
    /// </summary>
    /// <value>
    /// The total count of observed values that contributed to the statistic set.
    /// Should be greater than zero for a meaningful distribution.
    /// </value>
    double SampleCount { get; }

    /// <summary>
    /// Gets the sum of all observed values.
    /// </summary>
    /// <value>
    /// The arithmetic sum (Σxᵢ) of the observations. Typically non-negative for non-negative metrics
    /// such as durations or sizes, but no sign restriction is imposed by this interface.
    /// </value>
    double Sum { get; }

    /// <summary>
    /// Gets the minimum observed value.
    /// </summary>
    /// <value>
    /// The smallest value seen among all observations. For a single-sample set,
    /// this should equal the sample itself.
    /// </value>
    double Min { get; }

    /// <summary>
    /// Gets the maximum observed value.
    /// </summary>
    /// <value>
    /// The largest value seen among all observations. For a single-sample set,
    /// this should equal the sample itself.
    /// </value>
    double Max { get; }
}
