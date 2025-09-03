// <copyright file="TimingHandler.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Abstractions;
using NetMetric.Timer.Core;

namespace NetMetric.Timer.Http;

/// <summary>
/// A <see cref="DelegatingHandler"/> implementation that measures the duration of HTTP client requests 
/// and records the timing data using the provided <see cref="ITimerSink"/> instance.
/// This handler can be added to an <see cref="HttpClient"/> to automatically monitor the duration 
/// of outgoing HTTP requests.
/// </summary>
public sealed class TimingHandler : DelegatingHandler
{
    private readonly ITimerSink _sink;
    private readonly string _id;
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimingHandler"/> class with a custom metric ID 
    /// and name for tracking HTTP client durations.
    /// </summary>
    /// <param name="sink">The <see cref="ITimerSink"/> instance used to record the duration of HTTP requests.</param>
    /// <param name="id">The unique identifier for the metric.</param>
    /// <param name="name">The display name of the metric.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when the <paramref name="sink"/> parameter is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the <paramref name="id"/> or <paramref name="name"/> parameters are null, empty, or consist only of white-space characters.
    /// </exception>
    /// <remarks>
    /// This constructor allows you to provide custom metric identifiers to suit specific application requirements.
    /// </remarks>
    public TimingHandler(ITimerSink sink, string id, string name)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(name));

        _sink = sink;
        _id = id;
        _name = name;
    }

    /// <inheritdoc />
    /// <summary>
    /// Asynchronously measures the duration of the outgoing HTTP request and forwards it down the handler pipeline.
    /// The duration is recorded by the <see cref="ITimerSink"/> instance provided to the handler.
    /// </summary>
    /// <param name="request">The outgoing HTTP request message.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="Task{HttpResponseMessage}"/> representing the HTTP response message.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when the <paramref name="request"/> parameter is null.
    /// </exception>
    /// <remarks>
    /// This method measures the duration of the HTTP request, capturing key request properties such as the HTTP method,
    /// host, and path. The measurement is recorded as a timed metric, which can be exported or logged through the 
    /// provided <see cref="ITimerSink"/>.
    /// </remarks>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Validate the request parameter to prevent null reference exceptions.
        ArgumentNullException.ThrowIfNull(request);

        // Collect low-cardinality tags to describe the request context.
        var tags = new Dictionary<string, string>(capacity: 4)
        {
            ["method"] = request.Method.Method,
            ["host"] = request.RequestUri?.Host ?? string.Empty,
            ["path"] = request.RequestUri?.AbsolutePath ?? string.Empty,
        };

        // Start the timer measurement and record the request duration.
        using var _ = TimeMeasure.Start(_sink, _id, _name, tags);

        // Forward the HTTP request through the handler pipeline.
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
