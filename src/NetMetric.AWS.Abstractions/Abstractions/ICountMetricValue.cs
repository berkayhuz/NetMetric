// <copyright file="ICountMetricValue.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AWS.Abstractions;

/// <summary>
/// Represents a metric payload whose primary value is an integer <see cref="Count"/>.
/// </summary>
/// <remarks>
/// <para>
/// This interface is typically used by counters, gauges, or distribution-like metrics where the
/// total number of observations, items, or events is the core signal. Implementations should
/// ensure that <see cref="Count"/> is non-negative unless the specific semantic of the metric
/// explicitly permits otherwise (for example, a delta that may go negative during compensation).
/// </para>
/// <para>
/// Consumers can treat <see cref="Count"/> as the canonical numeric value when exporting to
/// backends that expect a single scalar per data point (e.g., CloudWatch <c>Count</c> unit).
/// </para>
/// <para><b>Thread-safety:</b> This interface does not prescribe concurrency guarantees.
/// Implementations that are updated from multiple threads are responsible for their own
/// synchronization semantics (e.g., using <see cref="System.Threading.Interlocked"/> operations).
/// </para>
/// </remarks>
/// <example>
/// <para><b>Implementing a simple count metric</b></para>
/// <code language="csharp"><![CDATA[
/// A minimal immutable value object that carries a count.
/// public sealed class CountMetricValue : ICountMetricValue
/// {
///     public CountMetricValue(long count)
///     {
///         if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
///         Count = count;
///     }
///
///     public long Count { get; }
/// }
/// ]]></code>
///
/// <para><b>Using in an exporter</b></para>
/// <code language="csharp"><![CDATA[
/// Suppose 'metric' is an IMetric<ICountMetricValue> from your pipeline.
/// if (metric.GetValue() is ICountMetricValue v)
/// {
///     var asDouble = (double)v.Count;
///     // Forward to the backend as a scalar value with "Count" semantics.
///     // e.g., CloudWatch MetricDatum.Value = asDouble; Unit = StandardUnit.Count;
/// }
/// ]]></code>
/// </example>
/// <seealso href="https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/cloudwatch_concepts.html">
/// Amazon CloudWatch metric concepts</seealso>
public interface ICountMetricValue
{
    /// <summary>
    /// Gets the total number of occurrences, items, or events represented by this metric value.
    /// </summary>
    /// <value>
    /// A <see cref="long"/> integer representing the count. Exporters may cast this to
    /// a floating-point value when targeting systems that require <c>double</c> payloads.
    /// </value>
    long Count { get; }
}
