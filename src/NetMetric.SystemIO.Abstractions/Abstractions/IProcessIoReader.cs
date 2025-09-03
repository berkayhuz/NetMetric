// <copyright file="IProcessIoReader.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SystemIO.Abstractions;

/// <summary>
/// Defines an abstraction for reading I/O counters of the current process.
/// </summary>
/// <remarks>
/// Implementations may rely on platform-specific APIs such as Windows performance counters
/// or Linux /proc filesystem to gather process-level I/O statistics.
/// </remarks>
public interface IProcessIoReader
{
    /// <summary>
    /// Attempts to read the current process I/O counters, which include cumulative read and write bytes.
    /// </summary>
    /// <returns>
    /// An <see cref="IoSnapshot"/> containing the read and write bytes, or <c>null</c> if the data is unavailable.
    /// </returns>
    IoSnapshot? TryReadCurrent();
}
