// <copyright file="IMetricValueVisitor.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines the visitor pattern contract for processing metric values of different kinds.
/// <para>
/// This interface allows consumers to handle multiple value representations
/// (e.g., gauges, counters, histograms, summaries) in a type-safe and
/// extensible way without relying on manual type checks or casting.
/// </para>
/// </summary>
public interface IMetricValueVisitor
{
    /// <summary>
    /// Visits a <see cref="GaugeValue"/> associated with the specified metric.
    /// </summary>
    /// <param name="value">The gauge value being visited.</param>
    /// <param name="metric">The metric that produced the value.</param>
    void Visit(GaugeValue value, IMetric metric) => Visit((MetricValue)value, metric);

    /// <summary>
    /// Visits a <see cref="CounterValue"/> associated with the specified metric.
    /// </summary>
    /// <param name="value">The counter value being visited.</param>
    /// <param name="metric">The metric that produced the value.</param>
    void Visit(CounterValue value, IMetric metric) => Visit((MetricValue)value, metric);

    /// <summary>
    /// Visits a <see cref="DistributionValue"/> associated with the specified metric.
    /// </summary>
    /// <param name="value">The distribution value being visited.</param>
    /// <param name="metric">The metric that produced the value.</param>
    void Visit(DistributionValue value, IMetric metric) => Visit((MetricValue)value, metric);

    /// <summary>
    /// Visits a <see cref="SummaryValue"/> associated with the specified metric.
    /// </summary>
    /// <param name="value">The summary value being visited.</param>
    /// <param name="metric">The metric that produced the value.</param>
    void Visit(SummaryValue value, IMetric metric) => Visit((MetricValue)value, metric);

    /// <summary>
    /// Visits a <see cref="BucketHistogramValue"/> associated with the specified metric.
    /// </summary>
    /// <param name="value">The bucket histogram value being visited.</param>
    /// <param name="metric">The metric that produced the value.</param>
    void Visit(BucketHistogramValue value, IMetric metric) => Visit((MetricValue)value, metric);

    /// <summary>
    /// Visits a <see cref="MultiSampleValue"/> associated with the specified metric.
    /// </summary>
    /// <param name="value">The multi-sample value being visited.</param>
    /// <param name="metric">The metric that produced the value.</param>
    void Visit(MultiSampleValue value, IMetric metric) => Visit((MetricValue)value, metric);

    /// <summary>
    /// Visits a generic <see cref="MetricValue"/> associated with the specified metric.
    /// <para>
    /// This method serves as the base entry point for all specific metric value types.
    /// Custom implementations should provide the core logic here, while the overloads
    /// for concrete value types delegate to this method.
    /// </para>
    /// </summary>
    /// <param name="value">The metric value being visited.</param>
    /// <param name="metric">The metric that produced the value.</param>
    void Visit(MetricValue value, IMetric metric);
}
