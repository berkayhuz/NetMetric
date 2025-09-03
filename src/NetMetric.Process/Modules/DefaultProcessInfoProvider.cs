// <copyright file="DefaultProcessInfoProvider.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Process.Abstractions;

namespace NetMetric.Process.Modules;

/// <summary>
/// Provides information about the current process, including CPU usage, process start time, and uptime.
/// This class implements <see cref="IProcessInfoProvider"/> and provides methods for retrieving process metrics.
/// </summary>
internal sealed class DefaultProcessInfoProvider : IProcessInfoProvider, IDisposable
{
    private readonly object _lock = new();
    private (TimeSpan cpu, long ts) _last;

    // Single instance, no handle leaks
    private readonly System.Diagnostics.Process _current =
        System.Diagnostics.Process.GetCurrentProcess();

    /// <summary>
    /// Gets the current process.
    /// </summary>
    public System.Diagnostics.Process Current => _current;

    /// <summary>
    /// Gets the number of processors available on the machine.
    /// </summary>
    public int ProcessorCount => Environment.ProcessorCount;

    /// <summary>
    /// Gets the last recorded CPU usage and timestamp.
    /// </summary>
    /// <returns>A tuple containing the total CPU usage and timestamp.</returns>
    public (TimeSpan totalCpu, long timestamp) GetLast()
    {
        lock (_lock)
        {
            return _last;
        }
    }

    /// <summary>
    /// Sets the last recorded CPU usage and timestamp.
    /// </summary>
    /// <param name="totalCpu">The total CPU usage.</param>
    /// <param name="timestamp">The timestamp when the CPU usage was recorded.</param>
    public void SetLast(TimeSpan totalCpu, long timestamp)
    {
        lock (_lock)
        {
            _last = (totalCpu, timestamp);
        }
    }

    /// <summary>
    /// Gets the start time of the process in UTC.
    /// </summary>
    public DateTime StartTimeUtc => _current.StartTime.ToUniversalTime();

    /// <summary>
    /// Gets the uptime of the process in UTC.
    /// </summary>
    /// <returns>The uptime of the process.</returns>
    public TimeSpan UptimeUtc() => DateTime.UtcNow - StartTimeUtc;

    /// <summary>
    /// Disposes the current process to release resources.
    /// </summary>
    public void Dispose()
    {
        try
        {
            _current?.Dispose();
        }
        catch
        {
            throw;
        }
    }
}
