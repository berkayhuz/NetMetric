// <copyright file="ProcessOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Process.Configuration;

/// <summary>
/// Options to configure the collection of process metrics, such as CPU usage, memory, threads, and uptime.
/// </summary>
public sealed class ProcessOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to collect CPU usage metrics.
    /// </summary>
    public bool EnableCpu { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to collect memory usage metrics.
    /// </summary>
    public bool EnableMemory { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to collect thread count metrics.
    /// </summary>
    public bool EnableThreads { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to collect uptime metrics.
    /// </summary>
    public bool EnableUptime { get; init; } = true;

    /// <summary>
    /// Gets or sets the CPU smoothing window for EWMA (Exponential Moving Average) in milliseconds.
    /// A value of 0 disables smoothing.
    /// </summary>
    public int CpuSmoothingWindowMs { get; init; }

    /// <summary>
    /// Gets or sets the metric name prefix.
    /// This is used to prefix metric names, for example "process.cpu.percent".
    /// </summary>
    public string MetricPrefix { get; init; } = "process";

    /// <summary>
    /// Gets or sets a value indicating whether to enable automatic tags for metrics.
    /// </summary>
    public bool EnableDefaultTags { get; init; } = true;

    /// <summary>
    /// Gets or sets the process name override. If set, this overrides the actual process name in the tags.
    /// </summary>
    public string? ProcessNameOverride { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether to include the machine name in the tags.
    /// </summary>
    public bool IncludeMachineNameTag { get; init; } = true;
}
