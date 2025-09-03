// <copyright file="IProcessMetricProbe.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Abstractions;

namespace NetMetric.Process.Abstractions;

/// <summary>
/// Represents a metric probe that collects process-related metrics asynchronously.
/// </summary>
public interface IProcessMetricProbe
{
    /// <summary>
    /// Collects metrics asynchronously and passes them to the specified <see cref="IMetricCollector"/>.
    /// </summary>
    /// <param name="metrics">The collector to store the collected metrics.</param>
    /// <param name="ct">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CollectAsync(IMetricCollector metrics, CancellationToken ct);
}
