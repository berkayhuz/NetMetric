// <copyright file="IGaugeBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a fluent builder contract for configuring and creating gauge metrics.
/// <para>
/// A gauge represents a numeric value that can arbitrarily go up and down over time,
/// such as current memory usage, queue depth, CPU utilization, or active connection count.
/// Unlike counters, gauges are not required to be monotonic.
/// </para>
/// </summary>
/// <remarks>
/// This builder follows the fluent API pattern, allowing consumers to specify
/// metadata (such as name, description, and unit) before building the final
/// <see cref="IGauge"/> instance.
/// </remarks>
public interface IGaugeBuilder : IInstrumentBuilder<IGauge>
{
}
