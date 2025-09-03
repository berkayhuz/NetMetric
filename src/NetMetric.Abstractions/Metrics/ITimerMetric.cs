// <copyright file="ITimerMetric.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Defines a metric instrument for measuring execution time.
/// <para>
/// A timer metric records the duration of code execution, typically expressed
/// in milliseconds. The statistical representation of recorded values
/// (e.g., percentiles such as p50, p90, p99) is left to the implementation.
/// </para>
/// </summary>
public interface ITimerMetric : IMetric
{
    /// <summary>
    /// Starts a new timing scope that records the elapsed time when disposed.
    /// </summary>
    /// <returns>
    /// An <see cref="ITimerScope"/> representing the timing operation.
    /// </returns>
    ITimerScope Start();

    /// <summary>
    /// Starts a new measurement scope (alias for <see cref="Start"/>).
    /// </summary>
    /// <returns>
    /// An <see cref="ITimerScope"/> representing the timing operation.
    /// </returns>
    ITimerScope StartMeasurement();

    /// <summary>
    /// Measures the execution time of the provided action.
    /// </summary>
    /// <param name="action">The action to execute and time.</param>
    void Measure(Action action);

    /// <summary>
    /// Measures the execution time of the provided function and returns its result.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute and time.</param>
    /// <returns>The return value of the executed function.</returns>
    T Measure<T>(Func<T> func);

    /// <summary>
    /// Records a pre-measured duration.
    /// </summary>
    /// <param name="elapsed">The elapsed time to record.</param>
    void Record(TimeSpan elapsed);

    /// <summary>
    /// Records a pre-measured duration in milliseconds.
    /// </summary>
    /// <param name="ms">The elapsed time in milliseconds.</param>
    void RecordMilliseconds(double ms);
}
