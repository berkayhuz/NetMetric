// <copyright file="TimedProxy.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Timer.Core;

namespace NetMetric.Timer.Proxy;

/// <summary>
/// A dynamic proxy that measures the execution time of method calls and records the timing data
/// via an <see cref="ITimerSink"/>. This proxy can be used to wrap a service and monitor the performance
/// of its methods.
/// </summary>
/// <typeparam name="T">The type of the proxied service.</typeparam>
public sealed class TimedProxy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> : DispatchProxy
    where T : class
{
    private T? _target;
    private ITimerSink? _sink;
    private string _prefix = "svc";

    /// <summary>
    /// Initializes the proxy with the target service, the <see cref="ITimerSink"/> for recording timings,
    /// and an optional prefix for the metric ID.
    /// </summary>
    /// <param name="target">The service to be proxied.</param>
    /// <param name="sink">The <see cref="ITimerSink"/> used to record method timings.</param>
    /// <param name="prefix">An optional prefix to be used in the metric ID.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="target"/> or <paramref name="sink"/> is null.</exception>
    internal void Init(T target, ITimerSink sink, string prefix)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sink);

        _target = target;
        _sink = sink;
        _prefix = string.IsNullOrWhiteSpace(prefix) ? "svc" : prefix;
    }

    /// <summary>
    /// Intercepts method calls, measures their duration, and forwards the call to the underlying target service.
    /// </summary>
    /// <param name="targetMethod">The method being invoked on the proxied service.</param>
    /// <param name="args">The arguments passed to the method.</param>
    /// <returns>The return value of the invoked method (which may be a <see cref="Task"/>).</returns>
    /// <exception cref="InvalidOperationException">Thrown if the proxy has not been properly initialized.</exception>
    /// <remarks>
    /// This method creates a timing scope for the method execution, records the elapsed time, and handles both
    /// synchronous and asynchronous methods (including <see cref="Task"/> and <see cref="Task{T}"/>).
    /// </remarks>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (_target is null || _sink is null || targetMethod is null)
            throw new InvalidOperationException("Proxy not initialized");

        var id = $"{_prefix}.{typeof(T).Name}.{targetMethod.Name}";
        var name = targetMethod.Name;
        var tags = new Dictionary<string, string>
        {
            ["type"] = typeof(T).Name,
            ["method"] = targetMethod.Name
        };

        // Start timing but DO NOT use using/try-finally yet — we may need to keep it alive for async.
        var scope = TimeMeasure.Start(_sink, id, name, tags);

        try
        {
            var result = targetMethod.Invoke(_target, args);

            if (result is Task task)
            {
                // AOT-safe: no reflection, no wrapping. Record when the original task completes (success/fault/cancel).
                task.ContinueWith(
                    static _ => { }, // no-op body; we’ll dispose below via closure-free continuation
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                // Use a second continuation to dispose without capturing state (avoid closures/allocs).
                task.GetAwaiter().OnCompleted(scope.Dispose);

                // Return the original task (Task or Task<T>) unmodified.
                return result;
            }

            // Synchronous path: record now.
            scope.Dispose();
            return result;
        }
        catch
        {
            // Ensure we record even on exceptions thrown before any Task is produced.
            scope.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Awaits a non-generic task and keeps the timing scope alive until completion.
    /// </summary>
    /// <param name="task">The task to await.</param>
    private static async Task AwaitAndRecord(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        await task.ConfigureAwait(false);
    }

    /// <summary>
    /// Awaits a generic task (<see cref="Task{T}"/>) via reflection and keeps the timing scope alive.
    /// </summary>
    /// <param name="task">The task to await.</param>
    /// <returns>The result of the awaited task.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="task"/> is null.</exception>
    /// <remarks>
    /// This method uses reflection to invoke the appropriate method for awaiting a generic task.
    /// </remarks>
    [RequiresDynamicCode("Calls MethodInfo.MakeGenericMethod(Type) for Task<T> awaiting.")]
    private static object AwaitAndRecordGeneric(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var tRes = task.GetType().GetGenericArguments()[0];
        var m = typeof(TimedProxy<T>)
            .GetMethod(nameof(AwaitAndRecordGenericCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(tRes);

        return m.Invoke(null, new object[] { task })!;
    }

    private static async Task<TRes> AwaitAndRecordGenericCore<TRes>(Task<TRes> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return await task.ConfigureAwait(false);
    }
}
