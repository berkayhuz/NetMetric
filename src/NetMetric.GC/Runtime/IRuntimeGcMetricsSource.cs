// <copyright file="IRuntimeGcMetricsSource.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.GC.Runtime;

/// <summary>
/// An interface that provides access to runtime garbage collection (GC) metrics and counters for the running process.
/// Collectors communicate with this interface to retrieve GC-related data, without needing to know the details of the listener implementation.
/// </summary>
public interface IRuntimeGcMetricsSource : IDisposable
{
    /// <summary>
    /// Captures the percentage of time spent in garbage collection for the last few GCs.
    /// </summary>
    /// <returns>A <see cref="double"/> array containing the time-in-GC percentage for each GC pause.</returns>
    /// <remarks>
    /// This method provides a snapshot of the most recent GC pauses, expressed as a percentage of total time spent in garbage collection.
    /// </remarks>
    double[] SnapshotTimeInGcPercent();

    /// <summary>
    /// Retrieves the current heap size in bytes.
    /// </summary>
    /// <returns>A nullable <see cref="double"/> representing the current heap size in bytes, or <c>null</c> if unavailable.</returns>
    /// <remarks>
    /// This method provides the current size of the managed heap in bytes. It may return <c>null</c> if the heap size cannot be retrieved.
    /// </remarks>
    double? CurrentHeapBytes();

    /// <summary>
    /// Retrieves the current GC collection counts for generations 0, 1, and 2.
    /// </summary>
    /// <returns>A tuple containing the GC collection counts for each generation (Gen0, Gen1, and Gen2). Each value is nullable.</returns>
    /// <remarks>
    /// This method provides the number of GC collections that have occurred for each of the GC generations. The tuple values may be <c>null</c> if the collection counts are unavailable.
    /// </remarks>
    (double? gen0, double? gen1, double? gen2) CurrentGenCounts();
}
