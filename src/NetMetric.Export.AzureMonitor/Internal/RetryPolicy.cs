// <copyright file="RetryPolicy.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Security.Cryptography;

namespace NetMetric.Export.AzureMonitor.Internal;

/// <summary>
/// Provides an application-level retry utility with bounded exponential backoff and
/// full-jitter randomization for transient failures in asynchronous workflows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Algorithm</b>: This policy uses an exponential backoff strategy capped by <c>maxDelay</c>
/// and randomized with <i>full jitter</i> to reduce thundering-herd effects. The effective delay for
/// attempt <c>n</c> (1-based) is uniformly random in <c>[0, min(maxDelay, baseDelay * 2^n))</c>.
/// </para>
/// <para>
/// <b>Intended usage</b>: Wrap short, idempotent operations (e.g., sending a telemetry batch,
/// fetching a page, acquiring a transient resource). The policy retries only when
/// <c>isTransient</c> returns <see langword="true"/> for the thrown exception.
/// Cancellation is honored between attempts and during delay.
/// </para>
/// </remarks>

internal static class RetryPolicy
{
    /// <summary>
    /// Executes an asynchronous <paramref name="action"/> with bounded exponential backoff and full-jitter randomization,
    /// retrying when the operation fails due to a transient exception as determined by <paramref name="isTransient"/>.
    /// </summary>
    /// <param name="action">
    /// The asynchronous operation to execute. The operation receives the ambient <see cref="CancellationToken"/>.
    /// The action should be <b>idempotent</b> or otherwise safe to retry.
    /// </param>
    /// <param name="maxAttempts">
    /// The maximum number of retry attempts allowed on transient failures. Must be greater than or equal to <c>1</c>.
    /// Note that total executions of <paramref name="action"/> can be up to <c>maxAttempts</c>, not counting the initial try.
    /// </param>
    /// <param name="baseDelay">
    /// The base backoff duration (the starting delay for the first retry before jitter is applied).
    /// Typical values are in the range of 50–500 ms depending on the service’s expected latency.
    /// </param>
    /// <param name="maxDelay">
    /// The upper bound for the backoff window. The randomized delay for each attempt will not exceed this value.
    /// </param>
    /// <param name="isTransient">
    /// A predicate that identifies whether an exception is considered transient and thus eligible for a retry.
    /// Return <see langword="true"/> for retryable conditions (e.g., timeouts, 5xx responses, throttling), otherwise <see langword="false"/>.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that aborts waiting and further retry attempts. Cancellation is observed
    /// before executing <paramref name="action"/> and during the backoff delay between attempts.
    /// </param>
    /// <param name="onRetry">
    /// An optional callback invoked after a transient failure is caught and before the delay is applied.
    /// Receives the <b>1-based</b> attempt number and the exception that triggered the retry.
    /// Use this for logging/diagnostics (avoid throwing from this callback).
    /// </param>
    /// <returns>
    /// A task that completes when <paramref name="action"/> succeeds or the retry policy gives up
    /// due to non-transient error, exhaustion of <paramref name="maxAttempts"/>, or cancellation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="action"/> or <paramref name="isTransient"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is canceled prior to or during execution, including during a delay.
    /// </exception>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <b>Delay calculation</b>: For attempt <c>n</c> (1-based), compute
    /// <c>backoff = min(maxDelay, baseDelay * 2^n)</c>, then sample a uniform random delay in
    /// <c>[0, backoff)</c> using a cryptographically secure random number generator to avoid correlation
    /// across concurrent callers.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Error propagation</b>: If a non-transient exception is thrown, it propagates immediately
    /// without further retries. If all attempts are exhausted, the last caught exception propagates.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Metrics</b>: Pair this with your diagnostics component (e.g., <c>onRetry</c>) to record attempt count,
    /// failure reasons, and backoff durations for observability.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <example>
    /// The following example retries a telemetry batch send on transient HTTP 429/5xx responses:
    /// <code language="csharp"><![CDATA[
    /// await RetryPolicy.RunAsync(
    ///     action: async ct =>
    ///     {
    ///         using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
    ///         {
    ///             Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
    ///         };
    ///         using var resp = await httpClient.SendAsync(req, ct).ConfigureAwait(false);
    ///         resp.EnsureSuccessStatusCode();
    ///     },
    ///     maxAttempts: 5,
    ///     baseDelay: TimeSpan.FromMilliseconds(100),
    ///     maxDelay: TimeSpan.FromSeconds(2),
    ///     isTransient: ex =>
    ///     {
    ///         if (ex is HttpRequestException) return true;
    ///         if (ex is TaskCanceledException tce && !tce.CancellationToken.IsCancellationRequested) return true; // timeout
    ///         if (ex is HttpRequestException hre && hre.StatusCode is >= (HttpStatusCode)500) return true;
    ///         return false;
    ///     },
    ///     ct: cancellationToken,
    ///     onRetry: (attempt, ex) =>
    ///     {
    ///         logger.LogWarning(ex, "Send failed (attempt {Attempt}). Retrying with backoff.", attempt);
    ///     });
    /// ]]></code>
    /// </example>
    public static async Task RunAsync(
        Func<CancellationToken, Task> action,
        int maxAttempts,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        Func<Exception, bool> isTransient,
        CancellationToken ct,
        Action<int, Exception>? onRetry = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(isTransient);

        int attempt = 0;
        using var rnd = RandomNumberGenerator.Create();

        for (; ; )
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await action(ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && isTransient(ex))
            {
                attempt++;
                onRetry?.Invoke(attempt, ex);

                // Exponential backoff with cap.
                var backoff = TimeSpan.FromMilliseconds(
                    Math.Min(
                        maxDelay.TotalMilliseconds,
                        baseDelay.TotalMilliseconds * Math.Pow(2, attempt)));

                // Full-jitter: uniform random in [0, backoff).
                var randomBytes = new byte[4];
                rnd.GetBytes(randomBytes);
                var randomValue = BitConverter.ToUInt32(randomBytes, 0) / (double)uint.MaxValue;
                var delay = TimeSpan.FromMilliseconds(randomValue * backoff.TotalMilliseconds);

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }
}
