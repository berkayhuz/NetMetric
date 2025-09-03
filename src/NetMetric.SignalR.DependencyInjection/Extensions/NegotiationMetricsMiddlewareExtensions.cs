// <copyright file="NegotiationMetricsMiddlewareExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Builder;

namespace NetMetric.SignalR.DependencyInjection;

/// <summary>
/// Provides extension methods to register the SignalR negotiation metrics middleware
/// in the ASP.NET Core request pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose</b><br/>
/// SignalR clients initiate their connection with an HTTP POST to <c>/hubPath/negotiate</c>.
/// The negotiation middleware measures the latency of these requests, attempts to detect
/// the selected transport (e.g., WebSockets), and records errors that occur during negotiation.
/// </para>
/// <para>
/// <b>Placement</b><br/>
/// This middleware must be added <em>before</em> mapping SignalR endpoints
/// (i.e., prior to <c>app.MapHub&lt;THub&gt;(...)</c>) so that the <c>/negotiate</c>
/// requests flow through it.
/// </para>
/// <para>
/// <b>Thread Safety</b><br/>
/// The extension is stateless; each request is handled independently by
/// <see cref="NegotiationMetricsMiddleware"/>.
/// </para>
/// </remarks>
public static class NegotiationMetricsMiddlewareExtensions
{
    /// <summary>
    /// Adds middleware that captures metrics for SignalR <c>/negotiate</c> endpoints.
    /// Must be called before SignalR hubs are mapped via <c>MapHub</c>.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <returns>
    /// The same <see cref="IApplicationBuilder"/> instance so that multiple calls can be chained.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The underlying <see cref="NegotiationMetricsMiddleware"/> records:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Negotiation duration (latency)</description></item>
    ///   <item><description>Chosen transport (when detectable)</description></item>
    ///   <item><description>Negotiation errors (scope = <c>negotiate</c>)</description></item>
    /// </list>
    /// <para>
    /// It also stores a connection start timestamp in <c>HttpContext.Items["__nm_conn_started"]</c>
    /// for later components (e.g., hub filters) to compute connection lifetimes.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var builder = WebApplication.CreateBuilder(args);
    /// // ... DI registrations including ISignalRMetrics ...
    /// var app = builder.Build();
    ///
    /// // Add negotiation metrics BEFORE mapping hubs
    /// app.UseNetMetricSignalRNegotiation();
    ///
    /// // Map hubs after the middleware
    /// app.MapHub<ChatHub>("/hubs/chat");
    ///
    /// app.Run();
    /// ]]></code>
    /// </example>
    public static IApplicationBuilder UseNetMetricSignalRNegotiation(this IApplicationBuilder app)
        => app.UseMiddleware<NegotiationMetricsMiddleware>();
}
