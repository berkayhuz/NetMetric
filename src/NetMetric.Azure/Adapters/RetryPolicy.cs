// <copyright file="RetryPolicy.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Azure.Adapters;

/// <summary>
/// Provides a concise retry helper that executes an asynchronous operation with a capped
/// number of attempts, exponential backoff, and cryptographically secure jitter.
/// </summary>
/// <remarks>
/// <para>
/// This policy is intentionally minimal and dependency-free. It is suitable for short, idempotent
/// operations against remote services where occasional transient faults are expected (e.g.,
/// throttling, temporary network glitches, or service busy errors).
/// </para>
/// <para>
/// The backoff schedule is fixed for simplicity: 100ms, 300ms, then 600ms (each
/// augmented with a random jitter in the range <c>[0, baseDelayMs)</c>). Jitter reduces
/// coordinated retries across clients and helps avoid thundering-herd effects.
/// </para>
/// <para>
/// If you require adaptive policies (dynamic jitter distributions, unbounded attempts, circuit
/// breaking, etc.), consider wrapping this helper or using a more fully-featured resilience
/// library elsewhere in your stack.
/// </para>
/// </remarks>
internal static class RetryPolicy
{
    /// <summary>
    /// Executes the specified asynchronous <paramref name="operation"/> and retries it when
    /// <paramref name="isTransient"/> deems an exception transient, honoring an optional
    /// <paramref name="overallTimeout"/> and the provided <paramref name="ct"/>.
    /// </summary>
    /// <typeparam name="T">The result type produced by the operation.</typeparam>
    /// <param name="operation">
    /// The asynchronous function to execute. It must accept a <see cref="CancellationToken"/>
    /// and should be <em>idempotent</em>, as it may be invoked multiple times.
    /// </param>
    /// <param name="isTransient">
    /// A predicate that returns <see langword="true"/> when the given <see cref="Exception"/>
    /// is considered transient and therefore eligible for a retry; otherwise <see langword="false"/>.
    /// </param>
    /// <param name="overallTimeout">
    /// The total wall-clock duration allowed for all attempts combined. When greater than
    /// <see cref="TimeSpan.Zero"/>, the method links an internal <see cref="CancellationTokenSource"/>
    /// that cancels after the specified timeout. Use <see cref="TimeSpan.Zero"/> to disable.
    /// </param>
    /// <param name="ct">A token to observe for external cancellation requests.</param>
    /// <returns>The successful result of type <typeparamref name="T"/>.</returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description>Attempts: at most <c>3</c> (1 initial + up to 2 retries).</description></item>
    ///   <item><description>Delays (base): <c>100 ms</c>, <c>300 ms</c>, <c>600 ms</c>.</description></item>
    ///   <item><description>Jitter: for each delay, an extra random value in <c>[0, baseDelayMs)</c>
    ///   is added using <see cref="RandomNumberGenerator.GetInt32(int, int)"/>.</description></item>
    ///   <item><description>Non-transient exceptions are propagated immediately.</description></item>
    ///   <item><description>Cancellation or timeout results in <see cref="OperationCanceledException"/>.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> or <paramref name="isTransient"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown if the overall timeout elapses or <paramref name="ct"/> is canceled before a successful attempt.
    /// </exception>
    /// <example>
    /// The following example retries an Azure SDK call when HTTP 429/503/500 occur:
    /// <code language="csharp">
    /// using Azure;
    /// using Azure.Core;
    /// 
    /// static bool IsTransient(Exception ex) =>
    ///     ex is RequestFailedException rfe &amp;&amp; (rfe.Status == 429 || rfe.Status == 503 || rfe.Status == 500);
    /// 
    /// var result = await RetryPolicy.ExecuteAsync(
    ///     async t => await client.DoWorkAsync(t), // your async idempotent operation
    ///     IsTransient,
    ///     overallTimeout: TimeSpan.FromSeconds(10),
    ///     ct: CancellationToken.None);
    /// </code>
    /// </example>
    /// <example>
    /// To propagate caller cancellation while enforcing a hard upper bound for all attempts:
    /// <code language="csharp">
    /// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    /// var value = await RetryPolicy.ExecuteAsync(
    ///     t => FetchValueAsync(t),
    ///     isTransient: ex =&gt; ex is TimeoutException,
    ///     overallTimeout: TimeSpan.FromSeconds(3),
    ///     ct: cts.Token);
    /// </code>
    /// </example>
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<Exception, bool> isTransient,
        TimeSpan overallTimeout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(isTransient);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (overallTimeout > TimeSpan.Zero)
            cts.CancelAfter(overallTimeout);

        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                return await operation(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (isTransient(ex) && attempt < 3)
            {
                // Exponential base delay (ms)
                var baseDelayMs = attempt switch
                {
                    1 => 100,
                    2 => 300,
                    _ => 600
                };

                // Cryptographically secure jitter in [0, baseDelayMs)
                var jitterMs = RandomNumberGenerator.GetInt32(0, baseDelayMs);
                var delayMs = baseDelayMs + jitterMs;

                await Task.Delay(delayMs, cts.Token).ConfigureAwait(false);
                // Loop and retry
            }
        }
    }
}
