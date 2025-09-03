// <copyright file="RequestTimingExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NetMetric.Abstractions;

namespace NetMetric.Timer.AspNetCore.DependencyInjection;

/// <summary>
/// Provides extension methods for registering the request timing middleware
/// in the ASP.NET Core application pipeline.
/// These methods enable the measurement of HTTP request durations using NetMetric.
/// </summary>
public static class RequestTimingExtensions
{
    /// <summary>
    /// Adds the NetMetric request timing middleware to the ASP.NET Core application pipeline.
    /// This middleware measures the duration of HTTP server requests and records the data with 
    /// the specified or default metric identifier and name (e.g., <c>http.server.duration</c>).
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> instance to which the middleware will be added.</param>
    /// <param name="metricId">The custom metric identifier for the request duration. The default value is <c>http.server.duration</c>.</param>
    /// <param name="metricName">The human-readable name for the metric. The default value is <c>HTTP Server Duration</c>.</param>
    /// <returns>The updated <see cref="IApplicationBuilder"/> to support method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="metricId"/> or <paramref name="metricName"/> is null or whitespace.</exception>
    /// <remarks>
    /// This middleware captures the time taken to process HTTP requests in the ASP.NET Core application
    /// and records the results using the provided <see cref="ITimerSink"/> instance. By default, the 
    /// metric is identified by <c>http.server.duration</c>, but both the metric ID and name can be customized.
    /// </remarks>
    public static IApplicationBuilder UseNetMetricRequestTiming(
        this IApplicationBuilder app,
        string metricId = "http.server.duration",
        string metricName = "HTTP Server Duration")
    {
        // Ensure the application builder is not null.
        ArgumentNullException.ThrowIfNull(app);

        // Validate that metricId and metricName are not null or whitespace.
        if (string.IsNullOrWhiteSpace(metricId))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(metricId));
        if (string.IsNullOrWhiteSpace(metricName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(metricName));

        // Retrieve the ITimerSink service from the application's dependency injection container.
        var sink = app.ApplicationServices.GetRequiredService<ITimerSink>();

        // Use the middleware to measure request durations.
        return app.UseMiddleware<RequestTimingMiddleware>(sink, metricId, metricName);
    }
}
