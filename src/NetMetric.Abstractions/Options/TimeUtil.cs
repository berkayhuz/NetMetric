// <copyright file="TimeUtil.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Provides utility methods and constants for working with time conversions
/// in metric calculations.
/// </summary>
public static class TimeUtil
{
    /// <summary>
    /// Gets the conversion factor from stopwatch ticks to milliseconds.
    /// <para>
    /// This value is computed as <c>1000.0 / Stopwatch.Frequency</c>,
    /// where <see cref="Stopwatch.Frequency"/> represents the number of ticks
    /// per second for the high-resolution performance counter.
    /// </para>
    /// <para>
    /// Multiplying a tick delta (as returned by <see cref="Stopwatch.GetTimestamp"/>)
    /// by this factor yields the elapsed time in milliseconds.
    /// </para>
    /// </summary>
    public static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
}
