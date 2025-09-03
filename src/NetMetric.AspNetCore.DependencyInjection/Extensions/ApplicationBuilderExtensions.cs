// <copyright file="ApplicationBuilderExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Builder;

namespace NetMetric.AspNetCore.Extensions;

/// <summary>
/// Provides extension methods for <see cref="IApplicationBuilder"/> to enable
/// NetMetric ASP.NET Core middleware.
/// </summary>
/// <remarks>
/// <para>
/// This extension integrates <see cref="RequestMetricsMiddleware"/> into the ASP.NET Core pipeline,
/// which records request duration, request/response sizes, and request totals.
/// </para>
/// <para>
/// <strong>Ordering:</strong> For comprehensive coverage (including endpoints/filters), call this
/// extension <em>before</em> routing/endpoint execution (e.g., before <c>app.MapControllers()</c>).
/// </para>
/// <para>
/// <strong>Dependencies:</strong> Ensure required services (e.g., <c>RequestMetricSet</c>,
/// <c>AspNetCoreMetricOptions</c>) are registered in DI.
/// </para>
/// </remarks>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the <see cref="RequestMetricsMiddleware"/> to the request processing pipeline,
    /// enabling HTTP server metrics instrumentation.
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> instance.</param>
    /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// // builder.Services.AddNetMetricAspNetCore(); // registers metrics + options (example)
    /// var app = builder.Build();
    ///
    /// // Enable NetMetric ASP.NET Core middleware
    /// app.UseNetMetricAspNetCore();
    ///
    /// app.MapControllers();
    /// app.Run();
    /// </code>
    /// </example>
    /// <remarks>
    /// This method is idempotent with respect to typical middleware ordering; calling it multiple
    /// times will add multiple instances, which is not recommended.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is <see langword="null"/>.</exception>
    public static IApplicationBuilder UseNetMetricAspNetCore(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestMetricsMiddleware>();
    }
}
