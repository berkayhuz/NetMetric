// <copyright file="ISystemIoReader.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SystemIO.Abstractions;

/// <summary>
/// Defines an abstraction for reading system-wide I/O statistics from available devices.
/// </summary>
/// <remarks>
/// Implementations may read from OS-specific sources (e.g., Linux /proc/diskstats,
/// Windows performance counters) to gather cumulative I/O metrics.
/// </remarks>
public interface ISystemIoReader
{
    /// <summary>
    /// Attempts to read the cumulative I/O data for each device on the system.
    /// </summary>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    ///     <item><description>A <see cref="DateTime"/> representing the timestamp of the data.</description></item>
    ///     <item><description>A list of <see cref="DeviceIo"/> representing the read and write bytes for each device.</description></item>
    /// </list>
    /// Returns <c>null</c> if the data is unavailable.
    /// </returns>
    (DateTime tsUtc, IReadOnlyList<DeviceIo> devices)? TryReadDevices();
}
