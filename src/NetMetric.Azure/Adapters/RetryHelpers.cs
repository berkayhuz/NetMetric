// <copyright file="RetryHelpers.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Azure.Adapters;

/// <summary>
/// Provides lightweight retry helper methods for Azure-related operations.
/// </summary>
/// <remarks>
/// <para>
/// This helper targets simple scenarios where a single retry with a short, randomized
/// backoff is sufficient to mitigate transient failures (for example, brief network
/// glitches or momentary service throttling).
/// </para>
/// <para>
/// If you need multi-attempt retries with exponential backoff and jitter, prefer
/// <see cref="RetryPolicy"/> which offers a more complete strategy.
/// </para>
/// <para>
/// Thread Safety: <see cref="RetryHelpers"/> is stateless and entirely thread-safe.
/// </para>
/// </remarks>
internal static class RetryHelpers
{
    /// <summary>
    /// Executes the given asynchronous operation with a single retry when a transient exception is detected.
    /// </summary>
    /// <typeparam name="T">The type of the result produced by <paramref name="op"/>.</typeparam>
    /// <param name="op">
    /// The asynchronous operation to execute. The delegate receives a <see cref="CancellationToken"/>
    /// that must be observed by the operation and any nested I/O it performs.
    /// </param>
    /// <param name="isTransient">
    /// A delegate that determines whether a caught <see cref="Exception"/> should be considered transient
    /// (and therefore eligible for a single retry). The delegate must be non-<see langword="null"/>.
    /// </param>
    /// <param name="timeout">
    /// The maximum allowed duration for the overall execution, including the retry and any backoff delay.
    /// If <see cref="TimeSpan.Zero"/>, no explicit overall timeout is applied (only <paramref name="ct"/> is honored).
    /// </param>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns>
    /// A task that completes with the result of the operation, either from the first attempt or the retry.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Behavior:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Attempts the operation once.</description></item>
    ///   <item><description>
    ///   If it throws and <paramref name="isTransient"/> returns <see langword="true"/>, waits for a
    ///   cryptographically secure randomized backoff in the range <c>[100, 300]</c> milliseconds and retries once.
    ///   </description></item>
    ///   <item><description>Non-transient exceptions are propagated without retry.</description></item>
    ///   <item><description>
    ///   Timeout is enforced by linking <paramref name="ct"/> with an internal CTS that honors <paramref name="timeout"/>.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Use this helper when a single retry is known to be sufficient and you want to avoid the heavier weight of a full retry policy.
    /// For more complex cases (multiple attempts, exponential backoff, structured logging), see <see cref="RetryPolicy"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Retry a Service Bus admin call once on transient errors.
    /// static bool IsTransient(Exception ex)
    ///     => ex is Azure.RequestFailedException rfe && (rfe.Status == 429 || rfe.Status == 500 || rfe.Status == 503);
    ///
    /// long count = await RetryHelpers.ExecWithLightRetryAsync<long>(
    ///     async token =>
    ///     {
    ///         // Your SDK call here (must observe 'token')
    ///         var props = await admin.GetQueueRuntimePropertiesAsync("orders", token);
    ///         return props.Value.TotalMessageCount;
    ///     },
    ///     IsTransient,
    ///     timeout: TimeSpan.FromSeconds(5),
    ///     ct: cancellationToken);
    /// ]]></code>
    /// </example>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Cosmos diagnostics sampling with a single retry on 429/500/503:
    /// static bool IsCosmosTransient(Exception ex)
    ///     => ex is Microsoft.Azure.Cosmos.CosmosException ce &&
    ///        ((int)ce.StatusCode == 429 || (int)ce.StatusCode == 500 || (int)ce.StatusCode == 503);
    ///
    /// var (ru, latencyMs) = await RetryHelpers.ExecWithLightRetryAsync(
    ///     async token => await cosmosDiagnostics.SampleRuAndLatencyAsync(endpoint, database, container, token),
    ///     IsCosmosTransient,
    ///     timeout: TimeSpan.FromSeconds(10),
    ///     ct: cancellationToken);
    /// ]]></code>
    /// </example>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="op"/> or <paramref name="isTransient"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown if the operation is canceled via <paramref name="ct"/> or the <paramref name="timeout"/> elapses.
    /// </exception>
    /// <seealso cref="RetryPolicy"/>
    /// <seealso cref="CosmosDiagnosticsAdapter"/>
    public static async Task<T> ExecWithLightRetryAsync<T>(
        Func<CancellationToken, Task<T>> op,
        Func<Exception, bool> isTransient,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(isTransient);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero) cts.CancelAfter(timeout);

        try
        {
            return await op(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (isTransient(ex))
        {
            // Use cryptographically secure jitter in [100, 300] ms to reduce coordinated retries.
            int jitter = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100, 301);
            await Task.Delay(TimeSpan.FromMilliseconds(jitter), cts.Token).ConfigureAwait(false);

            return await op(cts.Token).ConfigureAwait(false);
        }
    }
}
