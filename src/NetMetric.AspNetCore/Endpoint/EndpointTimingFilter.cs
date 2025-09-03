// <copyright file="EndpointTimingFilter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NetMetric.AspNetCore.Endpoint;

/// <summary>
/// An <see cref="IEndpointFilter"/> that measures the execution time of a Minimal API endpoint
/// and records the observation into the configured <c>MvcMetricSet</c>.
/// </summary>
/// <remarks>
/// <para>
/// The filter is enabled only when <see cref="AspNetCoreMetricOptions.EnableActionTiming"/> is <see langword="true"/>.
/// It normalizes the request route, captures the HTTP method, scheme, and protocol flavor,
/// then observes the elapsed time (in milliseconds) for the downstream endpoint delegate.
/// </para>
/// <para>
/// The measurement is performed using <see cref="Stopwatch"/> and converted to milliseconds via <c>TimeUtil.TicksToMs</c>.
/// The recorded value is tagged as <c>MvcStageNames.Action</c>, allowing you to correlate Minimal API handler latency
/// with MVC-controller action timings in dashboards.
/// </para>
/// <para><strong>Thread Safety:</strong> The filter itself holds no shared state and is safe to use concurrently per request.</para>
/// </remarks>
/// <example>
/// To register this filter for a specific endpoint:
/// <code>
/// app.MapGet("/health", () => Results.Ok())
///    .AddEndpointFilter&lt;EndpointTimingFilter&gt;();
/// </code>
/// Or to add it globally (applies to all mapped endpoints that support endpoint filters):
/// <code>
/// app.UseEndpoints(endpoints =>
/// {
///     endpoints.MapGet("/health", () => Results.Ok())
///              .AddEndpointFilter&lt;EndpointTimingFilter&gt;();
/// });
/// </code>
/// </example>
/// <seealso cref="MvcMetricSet"/>
/// <seealso cref="AspNetCoreMetricOptions"/>
/// <seealso cref="MvcStageNames"/>
public sealed class EndpointTimingFilter : IEndpointFilter
{
    /// <summary>
    /// Invokes the next endpoint filter/delegate in the pipeline while measuring its execution time.
    /// </summary>
    /// <param name="context">
    /// The current <see cref="EndpointFilterInvocationContext"/> providing access to <see cref="HttpContext"/>
    /// and endpoint arguments.
    /// </param>
    /// <param name="next">
    /// The next <see cref="EndpointFilterDelegate"/> in the pipeline to be invoked.
    /// </param>
    /// <returns>
    /// A task that completes with the downstream result object produced by <paramref name="next"/>.
    /// </returns>
    /// <remarks>
    /// When timing is disabled via <see cref="AspNetCoreMetricOptions.EnableActionTiming"/>,
    /// the method short-circuits and forwards the call without recording any metrics.
    /// Otherwise, it:
    /// <list type="number">
    /// <item><description>Resolves a normalized route label (fallback to <see cref="AspNetCoreMetricOptions.OtherRouteLabel"/> if needed).</description></item>
    /// <item><description>Captures HTTP method, scheme, and protocol flavor (via <see cref="HttpProtocolHelper.GetFlavor(HttpContext)"/>).</description></item>
    /// <item><description>Measures elapsed time using <see cref="Stopwatch"/> and converts ticks to milliseconds via <c>TimeUtil.TicksToMs</c>.</description></item>
    /// <item><description>Records the observation with stage name <c>MvcStageNames.Action</c> using <see cref="MvcMetricSet.GetOrCreate(string, string, string, string, string)"/> followed by <c>Observe(...)</c>.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="next"/> is <see langword="null"/>.
    /// </exception>
    public async ValueTask<object?> InvokeAsync(
         EndpointFilterInvocationContext context,
         EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var opt = http.RequestServices.GetRequiredService<AspNetCoreMetricOptions>();

        if (!opt.EnableActionTiming)
        {
            return await next(context).ConfigureAwait(false);
        }

        var route = RequestRouteResolver.ResolveNormalizedRoute(http, opt.OtherRouteLabel);
        var method = http.Request.Method;
        var scheme = http.Request.Scheme;
        var flavor = HttpProtocolHelper.GetFlavor(http);
        var metrics = http.RequestServices.GetRequiredService<MvcMetricSet>();

        var start = Stopwatch.GetTimestamp();
        var result = await next(context).ConfigureAwait(false);
        var elapsedMs = (Stopwatch.GetTimestamp() - start) * TimeUtil.TicksToMs;

        metrics.GetOrCreate(route, method, MvcStageNames.Action, scheme, flavor).Observe(elapsedMs);
        return result;
    }
}
