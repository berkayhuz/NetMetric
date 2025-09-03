// <copyright file="TimingHandler.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Network.Http;

/// <summary>
/// A custom <see cref="DelegatingHandler"/> that measures the duration of HTTP requests and records the timing metrics.
/// </summary>
public sealed class TimingHandler : DelegatingHandler
{
    private readonly ITimerSink _sink;
    private readonly string _id;
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimingHandler"/> class.
    /// </summary>
    /// <param name="sink">The timer sink used to record timing metrics.</param>
    /// <param name="id">The metric ID to record the timing data (default is "http.client.duration").</param>
    /// <param name="name">The name of the metric to record the timing data (default is "HTTP Client Duration").</param>
    /// <exception cref="ArgumentNullException">Thrown if the sink is null.</exception>
    public TimingHandler(ITimerSink sink, string id = "http.client.duration", string name = "HTTP Client Duration")
    {
        (_sink, _id, _name) = (sink ?? throw new ArgumentNullException(nameof(sink)), id, name);
    }

    /// <summary>
    /// Sends an HTTP request, records the duration of the request, and logs it using the provided timer sink.
    /// It also records the HTTP method, host, path, and response status.
    /// </summary>
    /// <param name="request">The HTTP request to send.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>The HTTP response message.</returns>
    /// <exception cref="Exception">Throws the exception encountered during the HTTP request processing.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Start measuring the request duration
        var start = Stopwatch.GetTimestamp();

        var tags = new Dictionary<string, string>(3)
        {
            ["method"] = request.Method.Method,
            ["host"] = request.RequestUri?.Host ?? "",
            ["path"] = request.RequestUri?.AbsolutePath ?? "",
        };

        try
        {
            // Send the HTTP request and wait for the response
            var resp = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // Record the response status and calculate the total duration
            tags["status"] = ((int)resp.StatusCode).ToString();

            var totalMs = (Stopwatch.GetTimestamp() - start) * TimeUtil.TicksToMs;

            // Record the timing data
            _sink.Record(_id, _name, totalMs, tags);

            return resp;
        }
        catch
        {
            // Record error metric if the request fails
            _sink.Record($"{_id}.errors", $"{_name} Errors", 1, tags);

            throw;
        }
    }
}
