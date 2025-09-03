// <copyright file="GaugeBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.Gauge;

/// <summary>
/// Fluent builder for creating <see cref="IGauge"/> instruments.
/// </summary>
/// <remarks>
/// <para>
/// A gauge metric stores the <b>current value</b> of a measurement at a point in time.  
/// Unlike counters, gauges can go up or down.  
/// Typical examples include:
/// <list type="bullet">
///   <item><description>CPU usage percentage.</description></item>
///   <item><description>Number of active users.</description></item>
///   <item><description>Queue depth.</description></item>
/// </list>
/// </para>
/// <para>
/// This builder is obtained from <see cref="IMetricFactory.Gauge(string, string)"/> and supports
/// fluent configuration of metadata:
/// <list type="bullet">
///   <item><see cref="InstrumentBuilderBase{TMetric}.WithUnit(string)"/></item>
///   <item><see cref="InstrumentBuilderBase{TMetric}.WithDescription(string)"/></item>
///   <item><see cref="InstrumentBuilderBase{TMetric}.WithTag(string, string)"/></item>
///   <item><see cref="InstrumentBuilderBase{TMetric}.WithTags(System.Action{NetMetric.Abstractions.TagList})"/></item>
/// </list>
/// Call <see cref="Build"/> to produce the final <see cref="GaugeMetric"/> instance.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example: creating a gauge for memory usage
/// var memoryGauge = factory.Gauge("system.memory.usage", "Memory Usage")
///     .WithUnit("bytes")
///     .WithDescription("Current process memory usage in bytes")
///     .WithTag("service", "orders-api")
///     .Build();
///
/// memoryGauge.SetValue(42_000_000);
/// </code>
/// </example>
internal sealed class GaugeBuilder : InstrumentBuilderBase<IGauge>, IGaugeBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GaugeBuilder"/> class.
    /// </summary>
    /// <param name="id">Stable unique identifier of the gauge (e.g., <c>"system.memory.usage"</c>).</param>
    /// <param name="name">Human-readable name of the gauge (e.g., <c>"Memory Usage"</c>).</param>
    /// <param name="options">Metric configuration options (e.g., global tags, limits).</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> or <paramref name="name"/> is null or whitespace.
    /// </exception>
    public GaugeBuilder(string id, string name, MetricOptions options)
        : base(id, name, options) { }

    /// <summary>
    /// Finalizes configuration and creates a new <see cref="IGauge"/> instance.
    /// </summary>
    /// <returns>
    /// A thread-safe <see cref="GaugeMetric"/> initialized with the configured identifier,
    /// name, tags, unit, and description.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The created gauge supports <see cref="GaugeMetric.SetValue(double)"/> to overwrite its value
    /// and <see cref="MetricBase.GetValue"/> to retrieve the current <see cref="GaugeValue"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var activeUsers = factory.Gauge("svc.users.active", "Active Users")
    ///     .WithUnit("count")
    ///     .WithDescription("Number of currently active users in the system")
    ///     .Build();
    ///
    /// activeUsers.SetValue(123);
    /// var snapshot = (GaugeValue)activeUsers.GetValue();
    /// Console.WriteLine($"Active Users = {snapshot.Value}");
    /// </code>
    /// </example>
    public override IGauge Build()
        => new GaugeMetric(Id, Name, MaterializeTags(), Unit, Description);
}
