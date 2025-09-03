// <copyright file="IAsyncMetricExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Optional contract for exporters that require asynchronous export.
/// </summary>
public interface IAsyncMetricExporter : IMetricExporter
{
    /// <summary>Asynchronous export.</summary>
    Task ExportAsync(IEnumerable<IMetric> metrics);
}
