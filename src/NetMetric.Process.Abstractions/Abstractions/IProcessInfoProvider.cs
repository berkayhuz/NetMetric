// <copyright file="IProcessInfoProvider.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Process.Abstractions;

/// <summary>
/// Provides information about the current process, including CPU usage, process start time, and uptime.
/// </summary>
public interface IProcessInfoProvider
{
    /// <summary>
    /// Gets the current <see cref="System.Diagnostics.Process"/> instance.
    /// </summary>
    System.Diagnostics.Process Current { get; }

    /// <summary>
    /// Gets the last recorded CPU usage and timestamp.
    /// </summary>
    /// <returns>A tuple containing the total CPU usage and timestamp of the last record.</returns>
    (TimeSpan totalCpu, long timestamp) GetLast();

    /// <summary>
    /// Sets the last recorded CPU usage and timestamp.
    /// </summary>
    /// <param name="totalCpu">The total CPU usage.</param>
    /// <param name="timestamp">The timestamp of the CPU usage record.</param>
    void SetLast(TimeSpan totalCpu, long timestamp);

    /// <summary>
    /// Gets the number of processors available on the machine.
    /// </summary>
    int ProcessorCount { get; }

    /// <summary>
    /// Gets the start time of the process in UTC.
    /// </summary>
    DateTime StartTimeUtc { get; }

    /// <summary>
    /// Gets the uptime of the process in UTC.
    /// </summary>
    /// <returns>The uptime of the process.</returns>
    TimeSpan UptimeUtc();
}
