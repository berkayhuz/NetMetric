// <copyright file="RequestMetricsMiddleware.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Http;

namespace NetMetric.AspNetCore.Middleware;

/// <summary>
/// ASP.NET Core middleware that records request/response metrics such as duration,
/// request size, response size, and total request count.
/// </summary>
/// <remarks>
/// <para>
/// The middleware wraps the HTTP pipeline, measuring latency and sizes for each request.
/// It supports sampling via <see cref="AspNetCoreMetricOptions.SamplingRate"/> to limit overhead
/// in high-throughput scenarios.
/// </para>
/// <para>
/// Response size is tracked by wrapping <see cref="HttpResponse.Body"/> with
/// <see cref="CountingResponseStream"/> to count written bytes. Metrics are created and cached
/// via <see cref="RequestMetricSet"/> with tags such as <c>route</c>, <c>method</c>, <c>scheme</c>,
/// and <c>http.flavor</c>; the total requests counter additionally includes the <c>code</c> tag.
/// </para>
/// <para><strong>Thread Safety:</strong> The middleware holds no shared mutable state beyond injected
/// dependencies and is safe to invoke concurrently across requests.</para>
/// </remarks>
/// <example>
/// To register the middleware in the pipeline:
/// <code>
/// app.UseMiddleware&lt;RequestMetricsMiddleware&gt;();
/// </code>
/// </example>
/// <seealso cref="RequestMetricSet"/>
/// <seealso cref="CountingResponseStream"/>
/// <seealso cref="TagKeys"/>
/// <seealso cref="MetricNames"/>
/// <seealso cref="AspNetCoreMetricOptions"/>
public sealed class RequestMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestMetricSet _metrics;
    private readonly AspNetCoreMetricOptions _opt;
    private readonly Func<double> _rnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestMetricsMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next <see cref="RequestDelegate"/> in the pipeline.</param>
    /// <param name="metrics">The <see cref="RequestMetricSet"/> responsible for creating metrics.</param>
    /// <param name="options">The configuration options controlling sampling, bounds, and route handling.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="next"/>, <paramref name="metrics"/>, or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public RequestMetricsMiddleware(
        RequestDelegate next,
        RequestMetricSet metrics,
        AspNetCoreMetricOptions options)
    {
        _next = next;
        _metrics = metrics;
        _opt = options;
        _rnd = Random.Shared.NextDouble;
    }

    /// <summary>
    /// Invokes the middleware for the given HTTP context,
    /// measuring latency, request size, response size, and incrementing total request count.
    /// </summary>
    /// <param name="context">The current <see cref="HttpContext"/>.</param>
    /// <returns>A task representing the asynchronous middleware execution.</returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// Applies randomized sampling if <see cref="AspNetCoreMetricOptions.SamplingRate"/> is less than 1.0; when a sample
    /// is skipped, the request is forwarded without any metric recording.
    /// </description></item>
    /// <item><description>
    /// Wraps the response stream with <see cref="CountingResponseStream"/> to capture bytes written. The inner stream
    /// ownership is not changed; it is restored in a <c>finally</c> block.
    /// </description></item>
    /// <item><description>
    /// Uses <see cref="Stopwatch.GetTimestamp"/> to measure elapsed ticks and converts them to milliseconds
    /// using <c>TimeUtil.TicksToMs</c>.
    /// </description></item>
    /// <item><description>
    /// Observes request size when <c>Content-Length</c> is available (negative or missing values are ignored),
    /// observes response size based on counted bytes, and increments the total requests counter with the response
    /// status <c>code</c> tag.
    /// </description></item>
    /// </list>
    /// <para>
    /// <strong>Note:</strong> If response buffering or server features (e.g., compression) modify the response body after
    /// writing, <see cref="CountingResponseStream"/> reflects bytes written at this layer and may differ from the final
    /// on-the-wire size.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    public async Task Invoke(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_opt.SamplingRate < 1.0 && _rnd() > _opt.SamplingRate)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var route = RequestRouteResolver.ResolveNormalizedRoute(context, _opt.OtherRouteLabel);
        var method = context.Request.Method;
        var scheme = context.Request.Scheme;
        var flavor = HttpProtocolHelper.GetFlavor(context);

        var originalBody = context.Response.Body;
        try
        {
            var counting = new CountingResponseStream(originalBody);
            context.Response.Body = counting;

            var start = Stopwatch.GetTimestamp();
            await _next(context).ConfigureAwait(false);
            var elapsedMs = (Stopwatch.GetTimestamp() - start) * TimeUtil.TicksToMs;

            var code = context.Response.StatusCode.ToString();
            var (dur, reqSize, resSize, total) = _metrics.GetOrCreate(route, method, code, scheme, flavor);

            dur.Observe(elapsedMs);

            var reqLen = context.Request.ContentLength;
            if (reqLen.HasValue && reqLen.Value >= 0)
                reqSize.Observe(reqLen.Value);

            resSize.Observe(counting.BytesWritten);

            total.Increment(1);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
