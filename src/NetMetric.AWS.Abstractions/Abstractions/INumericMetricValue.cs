// <copyright file="INumericMetricValue.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AWS.Abstractions.Abstractions;

/// <summary>
/// Represents a metric value that exposes a numeric <see cref="Value"/>.
/// Typically used for gauges, instantaneous measurements, or counters where
/// the primary representation is a <see cref="double"/>.
/// </summary>
/// <remarks>
/// <para>
/// Consumers of this interface should treat <see cref="Value"/> as a pure data
/// carrier (no side effects). Implementations are free to compute or cache the value,
/// but accessing the property should be inexpensive and thread-safe.
/// </para>
/// <para>
/// <b>Units and scale</b><br/>
/// Ensure the numeric value is reported with a clearly defined unit and scale.
/// When the surrounding system supports tags (for example, a <c>unit</c> or
/// <c>netmetric.unit</c> tag in CloudWatch exporters), prefer expressing:
/// </para>
/// <list type="bullet">
///   <item><description>Durations in seconds (<c>s</c>) or milliseconds (<c>ms</c>).</description></item>
///   <item><description>Percentages on a 0–100 scale (not 0–1).</description></item>
///   <item><description>Sizes using bytes (<c>bytes</c>, <c>KB</c>, <c>MB</c>, etc.).</description></item>
/// </list>
/// <para>
/// <b>Numeric domain</b><br/>
/// Implementations should avoid returning <see cref="double.NaN"/>, <see cref="double.PositiveInfinity"/>,
/// or <see cref="double.NegativeInfinity"/>. If a value cannot be determined, prefer returning <c>0</c>
/// or omitting the metric at a higher layer, depending on your application's semantics.
/// </para>
/// </remarks>
/// <example>
/// <para>
/// The following example shows a simple gauge that reports current CPU usage as a percentage:
/// </para>
/// <code language="csharp"><![CDATA[
/// using NetMetric.AWS.Abstractions.Abstractions;
///
/// public sealed class CpuUsageMetric : INumericMetricValue
/// {
///     private readonly ISystemProbe _probe;
///
///     public CpuUsageMetric(ISystemProbe probe) => _probe = probe;
///
///     // Value is expressed on a 0–100 scale (percentage).
///     public double Value => _probe.GetCpuUsagePercent();
/// }
///
/// // Usage in an exporter or aggregator:
/// var cpu = new CpuUsageMetric(systemProbe);
/// double current = cpu.Value; // e.g., 37.5
/// ]]></code>
/// <para>
/// The next example reports an instantaneous queue depth where the unit can be tagged
/// as <c>count</c> by the exporting layer:
/// </para>
/// <code language="csharp"><![CDATA[
/// public sealed class QueueDepthMetric : INumericMetricValue
/// {
///     private readonly IQueue _queue;
///     public QueueDepthMetric(IQueue queue) => _queue = queue;
///     public double Value => _queue.Count; // integer count represented as double
/// }
/// ]]></code>
/// </example>
public interface INumericMetricValue
{
    /// <summary>
    /// Gets the numeric value of the metric.
    /// </summary>
    /// <value>
    /// A <see cref="double"/> representing the measured or observed quantity for this metric.
    /// The value should be finite (not NaN/Infinity) and expressed using a clear and consistent scale.
    /// </value>
    double Value { get; }
}
