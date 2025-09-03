// <copyright file="TimeMeasure.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Timer.Core;

/// <summary>
/// Provides helper methods for starting timing scopes or wrapping actions and functions with timing measurement.
/// </summary>
public static class TimeMeasure
{
    /// <summary>
    /// Starts a timing scope that records the elapsed time in milliseconds upon disposal.
    /// The time is recorded using the provided <see cref="ITimerSink"/>.
    /// </summary>
    /// <param name="sink">The <see cref="ITimerSink"/> used to record the timing data.</param>
    /// <param name="id">The unique identifier for the timing metric.</param>
    /// <param name="name">The display name for the timing metric.</param>
    /// <param name="tags">Optional tags associated with the timing metric.</param>
    /// <returns>A new <see cref="TimingScope"/> that will record the elapsed time when disposed.</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static TimingScope Start(ITimerSink sink, string id, string name, IReadOnlyDictionary<string, string>? tags = null)
        => new TimingScope(sink, id, name, tags);

    /// <summary>
    /// Measures the execution time of an action and records it using the provided <see cref="ITimerSink"/>.
    /// </summary>
    /// <param name="sink">The <see cref="ITimerSink"/> used to record the timing data.</param>
    /// <param name="id">The unique identifier for the timing metric.</param>
    /// <param name="name">The display name for the timing metric.</param>
    /// <param name="action">The action to be measured.</param>
    /// <param name="tags">Optional tags associated with the timing metric.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="sink"/> or <paramref name="action"/> is null.</exception>
    public static void Measure(this ITimerSink sink, string id, string name, Action action, IReadOnlyDictionary<string, string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(action);
        using var _ = Start(sink, id, name, tags);
        action();
    }

    /// <summary>
    /// Measures the execution time of a function and records it using the provided <see cref="ITimerSink"/>.
    /// Returns the result of the function.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the function.</typeparam>
    /// <param name="sink">The <see cref="ITimerSink"/> used to record the timing data.</param>
    /// <param name="id">The unique identifier for the timing metric.</param>
    /// <param name="name">The display name for the timing metric.</param>
    /// <param name="func">The function to be measured.</param>
    /// <param name="tags">Optional tags associated with the timing metric.</param>
    /// <returns>The result of the function.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="sink"/> or <paramref name="func"/> is null.</exception>
    public static T Measure<T>(this ITimerSink sink, string id, string name, Func<T> func, IReadOnlyDictionary<string, string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(func);
        using var _ = Start(sink, id, name, tags);
        return func();
    }

    /// <summary>
    /// Measures the execution time of an asynchronous action and records it using the provided <see cref="ITimerSink"/>.
    /// </summary>
    /// <param name="sink">The <see cref="ITimerSink"/> used to record the timing data.</param>
    /// <param name="id">The unique identifier for the timing metric.</param>
    /// <param name="name">The display name for the timing metric.</param>
    /// <param name="func">The asynchronous action to be measured.</param>
    /// <param name="tags">Optional tags associated with the timing metric.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="sink"/> or <paramref name="func"/> is null.</exception>
    public static async Task MeasureAsync(this ITimerSink sink, string id, string name, Func<CancellationToken, Task> func, IReadOnlyDictionary<string, string>? tags = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(func);
        using var _ = Start(sink, id, name, tags);
        await func(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Measures the execution time of an asynchronous function and records it using the provided <see cref="ITimerSink"/>.
    /// Returns the result of the asynchronous function.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the function.</typeparam>
    /// <param name="sink">The <see cref="ITimerSink"/> used to record the timing data.</param>
    /// <param name="id">The unique identifier for the timing metric.</param>
    /// <param name="name">The display name for the timing metric.</param>
    /// <param name="func">The asynchronous function to be measured.</param>
    /// <param name="tags">Optional tags associated with the timing metric.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, with the result of the function.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="sink"/> or <paramref name="func"/> is null.</exception>
    public static async Task<T> MeasureAsync<T>(this ITimerSink sink, string id, string name, Func<CancellationToken, Task<T>> func, IReadOnlyDictionary<string, string>? tags = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(func);
        using var _ = Start(sink, id, name, tags);
        return await func(ct).ConfigureAwait(false);
    }
}
