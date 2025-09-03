// <copyright file="IInstrumentBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a fluent builder contract for configuring and creating metric instruments.
/// <para>
/// An instrument represents a specific type of metric (counter, gauge, histogram, etc.).
/// The builder pattern allows developers to declaratively specify metadata and options
/// before constructing the final metric instance.
/// </para>
/// </summary>
/// <typeparam name="TMetric">
/// The concrete metric type that will be produced by this builder.
/// </typeparam>
public interface IInstrumentBuilder<out TMetric> where TMetric : IMetric
{
    /// <summary>
    /// Sets the unit of measurement for the metric.
    /// </summary>
    /// <param name="unit">
    /// A string representing the unit (e.g., <c>"ms"</c> for milliseconds,
    /// <c>"bytes"</c> for memory, <c>"requests"</c> for counts).
    /// </param>
    /// <returns>
    /// The same <see cref="IInstrumentBuilder{TMetric}"/> instance for method chaining.
    /// </returns>
    IInstrumentBuilder<TMetric> WithUnit(string unit);

    /// <summary>
    /// Sets a human-readable description for the metric.
    /// </summary>
    /// <param name="description">
    /// A short description explaining the purpose or meaning of the metric.
    /// </param>
    /// <returns>
    /// The same <see cref="IInstrumentBuilder{TMetric}"/> instance for method chaining.
    /// </returns>
    IInstrumentBuilder<TMetric> WithDescription(string description);

    /// <summary>
    /// Adds a static key-value tag to the metric.
    /// </summary>
    /// <param name="key">The tag key (must be non-empty).</param>
    /// <param name="value">The tag value (may be empty or null depending on conventions).</param>
    /// <returns>
    /// The same <see cref="IInstrumentBuilder{TMetric}"/> instance for method chaining.
    /// </returns>
    IInstrumentBuilder<TMetric> WithTag(string key, string value);

    /// <summary>
    /// Adds or modifies tags for the metric using a <see cref="TagList"/> builder delegate.
    /// </summary>
    /// <param name="build">
    /// A callback that receives a mutable <see cref="TagList"/> to configure multiple tags.
    /// </param>
    /// <returns>
    /// The same <see cref="IInstrumentBuilder{TMetric}"/> instance for method chaining.
    /// </returns>
    IInstrumentBuilder<TMetric> WithTags(Action<TagList> build);

    /// <summary>
    /// Sets a windowing policy that governs how the metric aggregates
    /// observations over time (e.g., sliding or tumbling windows).
    /// </summary>
    /// <param name="window">The metric window policy to apply.</param>
    /// <returns>
    /// The same <see cref="IInstrumentBuilder{TMetric}"/> instance for method chaining.
    /// </returns>
    IInstrumentBuilder<TMetric> WithWindow(IMetricWindowPolicy window);

    /// <summary>
    /// Builds and returns the configured metric instrument instance.
    /// </summary>
    /// <returns>
    /// A fully constructed <typeparamref name="TMetric"/> ready for use.
    /// </returns>
    TMetric Build();
}
