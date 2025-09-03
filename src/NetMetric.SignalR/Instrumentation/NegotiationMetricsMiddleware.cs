// <copyright file="NegotiationMetricsMiddleware.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Http;
using ISignalRMetrics = NetMetric.SignalR.Abstractions.ISignalRMetrics;

namespace NetMetric.SignalR.Instrumentation;

/// <summary>
/// ASP.NET Core middleware that intercepts SignalR <c>/negotiate</c> requests
/// and records negotiation metrics (duration, chosen transport, and errors)
/// via an <see cref="ISignalRMetrics"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose</b><br/>
/// SignalR clients begin their connection lifecycle with an HTTP POST to <c>/hubPath/negotiate</c>.
/// This middleware measures the duration of those requests, attempts to detect the chosen transport
/// (e.g., WebSockets), and records errors encountered during the negotiation process.
/// </para>
/// <para>
/// <b>Integration</b><br/>
/// Register this middleware early in the ASP.NET Core pipeline, before the SignalR endpoints:
/// <code language="csharp"><![CDATA[
/// app.UseMiddleware<NegotiationMetricsMiddleware>();
/// app.MapHub<ChatHub>("/hubs/chat");
/// ]]></code>
/// </para>
/// <para>
/// <b>Metrics produced</b>
/// <list type="bullet">
///   <item><description><c>Negotiated</c> (duration, transport, fallback)</description></item>
///   <item><description><c>ObserveError</c> with scope <c>negotiate</c></description></item>
/// </list>
/// </para>
/// <para>
/// <b>Thread safety</b><br/>
/// The middleware is stateless except for the injected <see cref="RequestDelegate"/>. Each request
/// is handled independently; the provided <see cref="ISignalRMetrics"/> implementation must be
/// thread-safe.
/// </para>
/// </remarks>
public sealed class NegotiationMetricsMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="NegotiationMetricsMiddleware"/> class.
    /// </summary>
    /// <param name="next">
    /// The next component in the HTTP request pipeline. Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="next"/> is <see langword="null"/>.
    /// </exception>
    public NegotiationMetricsMiddleware(RequestDelegate next)
        => _next = next ?? throw new ArgumentNullException(nameof(next));

    /// <summary>
    /// Processes an incoming HTTP request. If the path ends with <c>/negotiate</c>,
    /// records negotiation metrics (duration and errors). Otherwise, forwards the
    /// request without instrumentation.
    /// </summary>
    /// <param name="context">The current <see cref="HttpContext"/>. Must not be <see langword="null"/>.</param>
    /// <param name="metrics">The metrics collector. Must not be <see langword="null"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="metrics"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown if the request is aborted or the token is canceled; the exception is recorded via
    /// <see cref="ISignalRMetrics.ObserveError(string, string, string)"/> and then rethrown.
    /// </exception>
    /// <remarks>
    /// On successful negotiation, this method records <see cref="ISignalRMetrics.Negotiated"/>.
    /// On errors, it records <see cref="ISignalRMetrics.ObserveError"/> with scope <c>negotiate</c>.
    /// </remarks>
    /// <example>
    /// The following example shows how to register the middleware and a hub:
    /// <code language="csharp"><![CDATA[
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddSignalR();
    /// builder.Services.AddSingleton<ISignalRMetrics, DefaultSignalRMetrics>(); // Or your concrete metric set
    ///
    /// var app = builder.Build();
    /// app.UseMiddleware<NegotiationMetricsMiddleware>();  // Place before MapHub
    /// app.MapHub<ChatHub>("/hubs/chat");
    /// app.Run();
    /// ]]></code>
    /// </example>
    public async Task InvokeAsync(HttpContext context, ISignalRMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metrics);

        var path = context.Request.Path.Value;

        // Handle negotiate endpoint
        if (path is not null &&
            path.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase))
        {
            var hub = ExtractHubName(path);
            var sw = Stopwatch.StartNew();

            try
            {
                await _next(context).ConfigureAwait(false);
                sw.Stop();

                // Naïve detection: check if WebSocket extensions were negotiated
                var chosen = context.Response.Headers["sec-websocket-extensions"].Count > 0
                    ? "ws"
                    : null;

                metrics.Negotiated(hub, chosen, sw.Elapsed, fallback: null);
            }
            catch (OperationCanceledException)
            {
                metrics.ObserveError(hub, scope: "negotiate",
                    exceptionType: nameof(OperationCanceledException));
                throw;
            }
            catch (Exception ex)
            {
                metrics.ObserveError(hub, scope: "negotiate",
                    exceptionType: ex.GetType().Name);
                throw;
            }

            return;
        }

        // Attach a timestamp for connection lifetime measurement in later middleware
        if (!context.Items.ContainsKey("__nm_conn_started"))
            context.Items["__nm_conn_started"] = DateTimeOffset.UtcNow;

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts the hub name from a SignalR negotiate path.
    /// </summary>
    /// <param name="path">
    /// The request path (e.g., <c>/hubs/chat/negotiate</c>).
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The hub name (e.g., <c>"chat"</c> for <c>/hubs/chat/negotiate</c>).
    /// Returns <c>"unknown"</c> if the path cannot be parsed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var hub = NegotiationMetricsMiddleware
    ///     .GetHubNameForTest("/hubs/notifications/negotiate"); // "notifications"
    /// ]]></code>
    /// </example>
    private static string ExtractHubName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segs.Length >= 2 ? segs[^2] : "unknown";
    }

    // NOTE: If you want to unit test hub-name extraction explicitly, you can add
    // an internal method and expose it to tests via InternalsVisibleTo. The example
    // above demonstrates intended behavior.
}
