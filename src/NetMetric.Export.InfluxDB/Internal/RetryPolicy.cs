// <copyright file="RetryPolicy.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.InfluxDB.Internal;

/// <summary>
/// Provides retry logic with exponential backoff for asynchronous HTTP operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retryable conditions.</b> The policy treats the following outcomes as transient and retries:
/// </para>
/// <list type="bullet">
///   <item><description>HTTP 5xx status codes.</description></item>
///   <item><description><see cref="System.Net.HttpStatusCode.TooManyRequests"/> (HTTP 429).</description></item>
///   <item>
///     <description>
///       Exceptions thrown by the delegate: <see cref="System.Net.Http.HttpRequestException"/> or
///       <see cref="System.Threading.Tasks.TaskCanceledException"/> (including timeouts).
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>Backoff strategy.</b> After each failed attempt, the delay is calculated as
/// <c>baseDelay * 2^attempt</c>, where the first retry waits exactly the base delay.
/// The number of retries is limited by the <c>maxRetries</c> argument passed to
/// <see cref="SendWithRetryAsync"/>.
/// </para>
/// <para>
/// <b>Non-retriable results.</b> Any non-transient result (for example, 2xx success or most 4xx errors
/// except 429) is returned immediately without additional delay.
/// </para>
/// <para>
/// <b>Resource management.</b> For retryable HTTP responses, the response is disposed before the next attempt
/// to avoid leaking sockets or buffers.
/// </para>
/// <para>
/// <b>Thread-safety.</b> The class is stateless and safe to call concurrently.
/// </para>
/// </remarks>
internal static class RetryPolicy
{
    /// <summary>
    /// Executes the specified asynchronous HTTP operation with retries and exponential backoff.
    /// </summary>
    /// <param name="send">
    /// A delegate that performs the HTTP request. The delegate receives a
    /// <see cref="System.Threading.CancellationToken"/> and returns a
    /// <see cref="System.Net.Http.HttpResponseMessage"/> wrapped in a <see cref="System.Threading.Tasks.Task"/>.
    /// </param>
    /// <param name="maxRetries">
    /// The maximum number of retries after the initial attempt. A value of <c>0</c> disables retries
    /// (only the initial attempt is made). The total number of attempts is <c>maxRetries + 1</c>.
    /// </param>
    /// <param name="baseDelay">
    /// The base delay used to compute the exponential backoff. The delay before retry <c>n</c> is
    /// <c>baseDelay * 2^n</c>, where <c>n</c> starts at <c>0</c>.
    /// </param>
    /// <param name="ct">
    /// A token that propagates notification that operations should be canceled. Cancellation is honored
    /// both while invoking <paramref name="send"/> and while awaiting the backoff delay.
    /// </param>
    /// <returns>
    /// A task that completes with the successful <see cref="System.Net.Http.HttpResponseMessage"/> returned by
    /// <paramref name="send"/> or with the final failure if retries are exhausted.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown if <paramref name="send"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown if the operation is canceled via <paramref name="ct"/>.
    /// </exception>
    /// <exception cref="System.Net.Http.HttpRequestException">
    /// Thrown if all attempts fail with transient errors or exceptions.
    /// </exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// using System;
    /// using System.Net.Http;
    /// using System.Threading;
    /// using System.Threading.Tasks;
    ///
    /// // Example: Retrying an HTTP POST with exponential backoff
    /// static async Task<HttpResponseMessage> PostWithRetryAsync(HttpClient client, Uri uri, HttpContent content, CancellationToken ct)
    /// {
    ///     return await RetryPolicy.SendWithRetryAsync(
    ///         async token =>
    ///         {
    ///             using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
    ///             // Timeouts at the HttpClient level typically surface as TaskCanceledException.
    ///             return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
    ///                                 .ConfigureAwait(false);
    ///         },
    ///         maxRetries: 3,                 // Up to 3 retries (4 total attempts)
    ///         baseDelay: TimeSpan.FromMilliseconds(250),
    ///         ct: ct).ConfigureAwait(false);
    /// }
    /// ]]></code>
    /// </example>
    /// <seealso cref="System.Net.Http.HttpClient"/>
    /// <seealso cref="System.Net.Http.HttpRequestException"/>
    /// <seealso cref="System.Threading.Tasks.TaskCanceledException"/>
    public static async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        int maxRetries,
        TimeSpan baseDelay,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(send);

        Exception? last = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var resp = await send(ct).ConfigureAwait(false);

                if ((int)resp.StatusCode < 500 && resp.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
                {
                    // Success (2xx) or non-retriable 4xx (except 429) → return immediately.
                    return resp;
                }

                // 429 or 5xx → retry.
                last = new HttpRequestException($"Transient HTTP {(int)resp.StatusCode}.");
                resp.Dispose();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
            }

            // Exponential backoff: baseDelay * 2^attempt
            var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        throw last ?? new HttpRequestException("Unknown transient error.");
    }
}
