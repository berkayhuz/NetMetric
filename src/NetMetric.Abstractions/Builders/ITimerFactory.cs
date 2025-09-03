// <copyright file="ITimerFactory.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a factory contract for creating timer metrics.
/// <para>
/// A timer metric measures the duration of operations or code blocks,
/// typically storing the recorded values in an underlying histogram or summary
/// to enable distribution analysis such as averages, percentiles, and quantiles.
/// </para>
/// <remarks>
/// This interface exists separately from <see cref="IMetricFactory"/> to avoid
/// breaking changes while providing timer-specific creation options.
/// </remarks>
/// </summary>
public interface ITimerFactory
{
    /// <summary>
    /// Creates a new <see cref="ITimerMetric"/> instance with the specified configuration.
    /// </summary>
    /// <param name="id">
    /// A unique identifier for the timer metric (used internally for registry lookups).
    /// </param>
    /// <param name="name">
    /// A human-readable display name for the metric (e.g., <c>"http_request_duration"</c>).
    /// </param>
    /// <param name="tags">
    /// An optional set of static key-value tags to associate with the metric,
    /// such as <c>{"method":"GET","endpoint":"/api"}</c>. May be <see langword="null"/>.
    /// </param>
    /// <param name="capacity">
    /// The initial capacity of the underlying histogram used to store recorded durations.
    /// Defaults to <c>2048</c>.
    /// </param>
    /// <returns>
    /// A fully configured <see cref="ITimerMetric"/> instance ready for use.
    /// </returns>
    ITimerMetric CreateTimer(
        string id,
        string name,
        IReadOnlyDictionary<string, string>? tags = null,
        int capacity = 2048);
}
