// <copyright file="RequestTimingMiddleware.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetMetric.Abstractions;
using NetMetric.Timer.Core;

namespace NetMetric.Timer.AspNetCore;

/// <summary>
/// Middleware that measures the duration of HTTP requests and records the timing data
/// in the provided <see cref="ITimerSink"/>. It tracks the request and response metadata,
/// including route, HTTP method, status code, and fault information.
/// </summary>
public sealed class RequestTimingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITimerSink _sink;
    private readonly string _metricId;
    private readonly string _metricName;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestTimingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="sink">The <see cref="ITimerSink"/> instance used to record metrics.</param>
    /// <param name="metricId">The identifier for the metric being recorded (default: <c>http.server.duration</c>).</param>
    /// <param name="metricName">The human-readable name for the metric (default: <c>HTTP Server Duration</c>).</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="next"/> or <paramref name="sink"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="metricId"/> or <paramref name="metricName"/> is null or whitespace.</exception>
    /// <remarks>
    /// This constructor initializes the middleware with a timer sink and optional custom metric identifiers.
    /// It ensures that both the next middleware and the sink are non-null, and that metric identifiers are valid.
    /// </remarks>
    public RequestTimingMiddleware(
        RequestDelegate next,
        ITimerSink sink,
        string metricId = "http.server.duration",
        string metricName = "HTTP Server Duration")
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(sink);

        _next = next;
        _sink = sink;

        if (string.IsNullOrWhiteSpace(metricId))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(metricId));
        if (string.IsNullOrWhiteSpace(metricName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(metricName));

        _metricId = metricId;
        _metricName = metricName;
    }

    /// <summary>
    /// Invokes the middleware, measures the request duration, and records it with appropriate tags.
    /// The metric is tagged with route, method, status code, and fault information.
    /// </summary>
    /// <param name="ctx">The current <see cref="HttpContext"/> for the request being processed.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="ctx"/> is null.</exception>
    /// <remarks>
    /// This method starts measuring the duration of the request by calling <see cref="TimeMeasure.Start"/>
    /// and tags the metrics with the relevant information. It records the request's route, HTTP method, 
    /// and status code, and also captures any fault that occurs during request processing.
    /// </remarks>
    public async Task InvokeAsync(HttpContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // Retrieve the route template for the current endpoint, falling back to the request path.
        var endpoint = ctx.GetEndpoint() as RouteEndpoint;
        var routeTemplate = endpoint?.RoutePattern.RawText
                            ?? (ctx.Request.Path.HasValue ? ctx.Request.Path.Value! : "/");

        // Prepare tags to track the request's route and HTTP method.
        var tags = new Dictionary<string, string>(capacity: 5)
        {
            ["route"] = routeTemplate,
            ["method"] = ctx.Request.Method,
        };

        // Add a callback to capture the HTTP response status code once the response starts.
        ctx.Response.OnStarting(() =>
        {
            tags["status"] = ctx.Response.StatusCode.ToString(CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        });

        // Start timing the request duration.
        using var _ = TimeMeasure.Start(_sink, _metricId, _metricName, tags);
        try
        {
            // Continue processing the request.
            await _next(ctx).ConfigureAwait(false);
        }
        catch
        {
            // Mark the request as faulted if an exception occurs during processing.
            tags["faulted"] = "true";
            throw;
        }
    }
}
