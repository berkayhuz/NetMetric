// <copyright file="ICounterBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a fluent builder contract for configuring and creating counter metrics.
/// <para>
/// A counter metric is a monotonically increasing numeric value that is typically used
/// to track counts of events, requests, errors, or other occurrences over time.
/// Counters are reset only when the process restarts.
/// </para>
/// </summary>
/// <remarks>
/// This builder follows the fluent API pattern, allowing consumers to specify
/// metadata (such as name, description, and unit) before building the final
/// <see cref="ICounterMetric"/> instance.
/// </remarks>
public interface ICounterBuilder : IInstrumentBuilder<ICounterMetric>
{
}
