// <copyright file="HttpNetworkTimingHandler.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Network.Http;

/// <summary>
/// A custom <see cref="DelegatingHandler"/> that measures network timing for HTTP requests and responses.
/// It records timing metrics such as Time To First Byte (TTFB), server timing headers, and transfer times.
/// </summary>
public sealed class HttpNetworkTimingHandler : DelegatingHandler
{
    private readonly ITimerSink _sink;
    private readonly NetworkTimingOptions _opt;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpNetworkTimingHandler"/> class.
    /// </summary>
    /// <param name="sink">The timer sink used to record network timing metrics.</param>
    /// <param name="options">Optional configuration for network timing behavior.</param>
    /// <exception cref="ArgumentNullException">Thrown if the sink is null.</exception>
    public HttpNetworkTimingHandler(ITimerSink sink, NetworkTimingOptions? options = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _opt = options ?? new NetworkTimingOptions();
    }

    /// <summary>
    /// Sends an HTTP request and records various timing metrics for the request and response.
    /// It measures time to first byte (TTFB), server timing headers, and total transfer time.
    /// </summary>
    /// <param name="request">The HTTP request to send.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>The HTTP response message.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var start = Stopwatch.GetTimestamp();

        var method = request.Method.Method;
        var host = request.RequestUri?.Host ?? "";
        var path = request.RequestUri?.AbsolutePath ?? "/";

        // Include the query string if the option is enabled
        if (_opt.IncludeQueryString && request.RequestUri is not null)
        {
            path = request.RequestUri.PathAndQuery;
        }

        // Truncate path if it exceeds the specified max length
        if (_opt.MaxPathLength is int max && path.Length > max)
        {
            path = path[..max];
        }

        // Tags to be used in metric recording
        var tags = new Dictionary<string, string>(4)
        {
            ["method"] = method,
            ["host"] = host,
            ["path"] = path
        };

        HttpResponseMessage resp;

        long headersTs;

        try
        {
            // Send the HTTP request and record the response headers timestamp
            resp = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            headersTs = Stopwatch.GetTimestamp();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Record error metric if request fails
            _sink.Record($"{_opt.MetricIdPrefix}.errors", $"{_opt.MetricNamePrefix} Errors", 1, tags);

            throw;
        }

        // Record HTTP response status
        tags["status"] = ((int)resp.StatusCode).ToString();

        // Calculate Time To First Byte (TTFB) in milliseconds
        var ttfbMs = (headersTs - start) * TimeUtil.TicksToMs;

        _sink.Record($"{_opt.MetricIdPrefix}.ttfb", $"{_opt.MetricNamePrefix} TTFB", ttfbMs, tags);

        // Parse and record Server-Timing headers if available
        if (_opt.ParseServerTiming && resp.Headers.TryGetValues("Server-Timing", out var stVals))
        {
            foreach (var v in stVals)
            {
                foreach (var item in ServerTimingParser.Parse(v))
                {
                    if (item.DurationMs is double ms)
                    {
                        _sink.Record($"{_opt.MetricIdPrefix}.server.timing.{item.Name}", $"{_opt.MetricNamePrefix} ServerTiming {item.Name}", ms, tags);
                    }
                }
            }
        }

        // If no content is present, record total timing and return the response
        if (resp.Content is null)
        {
            _sink.Record($"{_opt.MetricIdPrefix}.total", $"{_opt.MetricNamePrefix} Total", ttfbMs, tags);

            return resp;
        }

        // Wrap the original content stream to track the transfer time
        var originalContent = resp.Content;
        var stream = await originalContent.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var timed = new TimedReadStream(
            inner: stream,
            sink: _sink,
            total: ($"{_opt.MetricIdPrefix}.total", $"{_opt.MetricNamePrefix} Total"),
            transfer: ($"{_opt.MetricIdPrefix}.transfer", $"{_opt.MetricNamePrefix} Transfer"),
            tags: tags,
            startTicks: start,
            headersTicks: headersTs,
            tagBytes: _opt.TagResponseBytes);

        var newContent = new StreamContent(timed);

        // Copy headers from the original content to the new content
        foreach (var h in originalContent.Headers)
        {
            newContent.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        resp.Content = newContent;

        return resp;
    }
}
