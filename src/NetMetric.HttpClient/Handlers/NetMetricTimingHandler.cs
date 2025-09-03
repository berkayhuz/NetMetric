// <copyright file="NetMetricTimingHandler.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.HttpClient.Handlers;

/// <summary>
/// A <see cref="DelegatingHandler"/> that records end-to-end HTTP request
/// timing and size metrics via <see cref="HttpClientMetricSet"/>.
/// </summary>
/// <remarks>
/// <para>
/// Measures the <c>total</c> request latency (from send until response headers are available),
/// increments per-status-code totals, and observes response size from the <c>Content-Length</c>
/// header when available.
/// </para>
/// <para>
/// When a response has content, the handler wraps the content stream with a <see cref="CountingStream"/>
/// to track the downloaded byte count (only when size is not already known) and to observe a separate
/// <c>download</c> phase duration while the caller reads the content downstream.
/// </para>
/// <para>
/// Timeout/cancellation errors (e.g., <see cref="OperationCanceledException"/> or
/// <see cref="TaskCanceledException"/>) increment the timeout counter after recording the
/// <c>total</c> phase. Any other exception increments a synthetic status-code bucket labeled
/// <c>"EXC"</c> so failures are visible in totals.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> Instances of this handler can be shared across requests as with any
/// typical <see cref="HttpMessageHandler"/>; all metric instruments are internally cached by
/// <see cref="HttpClientMetricSet"/>.
/// </para>
/// </remarks>
/// <example>
/// Register the handler and capture total latency, status totals, and download timing:
/// <code language="csharp"><![CDATA[
/// services.AddSingleton<HttpClientMetricSet>(sp => CreateMetrics(sp));
/// services.AddHttpClient("netmetric")
///         .AddHttpMessageHandler(sp => new NetMetricTimingHandler(sp.GetRequiredService<HttpClientMetricSet>()));
///
/// var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("netmetric");
/// using var resp = await client.GetAsync("https://example.com", HttpCompletionOption.ResponseHeadersRead, ct);
/// var body = await resp.Content.ReadAsStringAsync(ct); // download phase measured by CountingStream
/// ]]></code>
/// </example>
/// <seealso cref="HttpClientMetricSet"/>
/// <seealso cref="CountingStream"/>
public sealed class NetMetricTimingHandler : DelegatingHandler
{
    private readonly HttpClientMetricSet _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetMetricTimingHandler"/> class.
    /// </summary>
    /// <param name="metrics">Metric registry used to create and update HTTP metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metrics"/> is <see langword="null"/>.</exception>
    public NetMetricTimingHandler(HttpClientMetricSet metrics) => _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    /// <summary>
    /// Sends an HTTP request and records timing/size metrics for the operation.
    /// </summary>
    /// <param name="request">The outgoing <see cref="HttpRequestMessage"/>.</param>
    /// <param name="cancellationToken">A token to cancel the send operation.</param>
    /// <returns>
    /// A task that represents the asynchronous send operation and yields the
    /// <see cref="HttpResponseMessage"/> from the inner handler.
    /// </returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Records <c>total</c> phase duration immediately after the inner handler returns
    /// (i.e., headers received; use <see cref="HttpCompletionOption.ResponseHeadersRead"/> upstream to return earlier).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Increments per-host/method/scheme totals keyed by HTTP status code; on exceptions, increments <c>"EXC"</c>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// If <c>Content-Length</c> is present, observes size once. The stream is still wrapped to measure
    /// a <c>download</c> phase, but additional size observation is skipped to avoid double counting.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// On timeout/cancellation, increments the timeout counter after recording the <c>total</c> phase.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the operation is canceled via <paramref name="cancellationToken"/> or due to timeout.
    /// </exception>
    /// <exception cref="TaskCanceledException">
    /// Thrown when the request task is canceled (commonly indicates a timeout).
    /// </exception>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uri = request.RequestUri;
        var host = uri?.Host ?? "unknown";
        var scheme = uri?.Scheme ?? "http";
        var method = request.Method.Method;

        var start = Stopwatch.GetTimestamp();
        try
        {
            // With ResponseHeadersRead you can return earlier if configured upstream.
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var totalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _metrics.GetPhase(host, method, scheme, "total").Observe(totalMs);

            var code = ((int)response.StatusCode).ToString();
            _metrics.GetTotal(host, method, scheme, code).Increment();

            // Observe size from Content-Length once, if available.
            bool hasKnownLength = false;
            if (response.Content is not null && response.Content.Headers.ContentLength is long contentLength && contentLength >= 0)
            {
                hasKnownLength = true;
                _metrics.GetSize(host, method, scheme).Observe(contentLength);
            }

            // Wrap content to measure download phase; only observe size from the wrapper if not already known.
            if (response.Content is not null)
            {
                // Snapshot existing content headers before replacing the content.
                var originalHeaders = new List<KeyValuePair<string, IEnumerable<string>>>();
                foreach (var h in response.Content.Headers)
                    originalHeaders.Add(new(h.Key, h.Value));

                var inner = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var wrapped = new CountingStream(
                    inner,
                    onBytes: bytes =>
                    {
                        if (!hasKnownLength)
                            _metrics.GetSize(host, method, scheme).Observe(bytes);
                    },
                    onCompleteMs: ms =>
                    {
                        _metrics.GetPhase(host, method, scheme, "download").Observe(ms);
                    });

                var newContent = new StreamContent(wrapped);

                // Preserve original content headers on the replaced content.
                foreach (var h in originalHeaders)
                    newContent.Headers.TryAddWithoutValidation(h.Key, h.Value);

                response.Content = newContent;
            }

            return response;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            var totalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _metrics.GetPhase(host, method, scheme, "total").Observe(totalMs);
            _metrics.GetTimeouts(host, method, scheme).Increment();
            throw;
        }
        catch
        {
            var totalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _metrics.GetPhase(host, method, scheme, "total").Observe(totalMs);
            _metrics.GetTotal(host, method, scheme, "EXC").Increment(); // synthetic bucket for exception paths
            throw;
        }
    }
}
