// <copyright file="IMetricExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Single exporter surface: asynchronous.
/// </summary>
public interface IMetricExporter
{
    /// <summary>
    /// Exports the given set of metrics asynchronously.
    /// </summary>
    /// <param name="metrics">The collection of metrics to export.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    [RequiresUnreferencedCode(
        "Some exporters (e.g., Prometheus) use reflection. " +
        "When trimming is enabled, public properties and methods of metric implementations " +
        "must be preserved (via DynamicDependency, DynamicallyAccessedMembers, or a linker descriptor).")]
    Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default);
}
