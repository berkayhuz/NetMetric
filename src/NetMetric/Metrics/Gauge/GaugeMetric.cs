// <copyright file="GaugeMetric.cs" company="NetMetric"
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.Gauge;

/// <summary>
/// A <see cref="GaugeMetric"/> represents a metric that stores the latest observed value
/// at a given point in time.  
/// </summary>
/// <remarks>
/// <para>
/// Gauges differ from counters in that their value may go <b>up or down</b> over time.  
/// They are typically used to represent instantaneous measurements such as:
/// <list type="bullet">
///   <item><description>Number of active users.</description></item>
///   <item><description>CPU usage percentage.</description></item>
///   <item><description>Queue depth or buffer fill level.</description></item>
/// </list>
/// </para>
/// <para>
/// The gauge is <b>thread-safe</b>. Setting and reading values use <see cref="Interlocked"/>
/// operations to ensure consistency under concurrent access.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example: active users gauge
/// var activeUsers = factory.Gauge("svc.users.active", "Active Users")
///     .WithUnit("count")
///     .WithDescription("Number of currently active users")
///     .Build();
///
/// activeUsers.SetValue(42);
///
/// var snapshot = (GaugeValue)activeUsers.GetValue();
/// Console.WriteLine($"Active users = {snapshot.Value}");
/// </code>
/// </example>
public sealed class GaugeMetric : MetricBase, IGauge
{
    private double _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="GaugeMetric"/> class.
    /// </summary>
    /// <param name="id">Stable unique identifier for the gauge (e.g., <c>"system.cpu.usage"</c>).</param>
    /// <param name="name">Human-readable name of the gauge (e.g., <c>"CPU Usage"</c>).</param>
    /// <param name="tags">Optional metric dimension tags (merged with global/resource tags if configured).</param>
    /// <param name="unit">Optional unit of measurement (e.g., <c>"bytes"</c>, <c>"%"</c>).</param>
    /// <param name="description">Optional description of the metric’s purpose.</param>
    public GaugeMetric(
        string id,
        string name,
        IReadOnlyDictionary<string, string>? tags = null,
        string? unit = null,
        string? description = null)
        : base(id, name, InstrumentKind.Gauge, tags, unit, description) { }

    /// <summary>
    /// Sets the current value of the gauge, overwriting any previously stored value.
    /// </summary>
    /// <param name="value">The new value to set. Must be a finite number.</param>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="value"/> is NaN or Infinity.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Gauges are point-in-time indicators. Calling <see cref="SetValue(double)"/> replaces
    /// the prior value without accumulation.
    /// </para>
    /// <para>
    /// Internally uses <see cref="Interlocked.Exchange(ref double, double)"/> to guarantee atomicity.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// cpuGauge.SetValue(73.4); // Set CPU usage to 73.4%
    /// </code>
    /// </example>
    public void SetValue(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("Invalid value.", nameof(value));

        Interlocked.Exchange(ref _value, value);
    }

    /// <summary>
    /// Retrieves the current gauge value in a thread-safe manner.
    /// </summary>
    /// <returns>
    /// A <see cref="GaugeValue"/> containing the last recorded gauge value.  
    /// Returns <c>0</c> if no value has been set yet.
    /// </returns>
    /// <remarks>
    /// Uses <see cref="Interlocked.CompareExchange(ref double, double, double)"/> to perform
    /// an atomic read of the stored value.
    /// </remarks>
    /// <example>
    /// <code>
    /// var snapshot = (GaugeValue)cpuGauge.GetValue();
    /// Console.WriteLine($"CPU Usage = {snapshot.Value}%");
    /// </code>
    /// </example>
    public override object? GetValue()
        => new GaugeValue(Interlocked.CompareExchange(ref _value, 0d, 0d));
}
