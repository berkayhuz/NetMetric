// <copyright file="ExceptionTimingFilter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Mvc.Filters;

namespace NetMetric.AspNetCore.Filters;

/// <summary>
/// An <see cref="IAsyncExceptionFilter"/> that measures execution time when an exception occurs
/// in the MVC pipeline, and records the observation into the configured <see cref="MvcMetricSet"/>.
/// </summary>
/// <remarks>
/// <para>
/// The filter runs only when an unhandled exception is raised during action execution. If custom
/// exception handlers are present, their handling time is included in the measured scope. When no
/// additional handling is applied, the observed duration will typically be minimal.
/// </para>
/// <para>
/// Timing is collected only if <see cref="AspNetCoreMetricOptions.EnableExceptionTiming"/> is enabled.  
/// Optional sampling is supported via <see cref="AspNetCoreMetricOptions.SamplingRate"/> to reduce overhead.
/// </para>
/// <para><strong>Thread Safety:</strong> The filter maintains no shared mutable state and is safe under concurrent use.</para>
/// </remarks>
/// <example>
/// Apply globally:
/// <code>
/// services.AddControllers(options =>
/// {
///     options.Filters.Add&lt;ExceptionTimingFilter&gt;();
/// });
/// </code>
/// </example>
/// <seealso cref="MvcMetricSet"/>
/// <seealso cref="AspNetCoreMetricOptions"/>
/// <seealso cref="MvcStageNames"/>
public sealed class ExceptionTimingFilter : IAsyncExceptionFilter
{
    private readonly MvcMetricSet _metrics;
    private readonly AspNetCoreMetricOptions _opt;
    private readonly Func<double> _rnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExceptionTimingFilter"/> class.
    /// </summary>
    /// <param name="metrics">The <see cref="MvcMetricSet"/> used to store exception timing metrics.</param>
    /// <param name="opt">The configuration options controlling sampling and enablement.</param>
    public ExceptionTimingFilter(MvcMetricSet metrics, AspNetCoreMetricOptions opt)
    {
        _metrics = metrics;
        _opt = opt;
        _rnd = Random.Shared.NextDouble;
    }

    /// <summary>
    /// Called asynchronously when an exception occurs in the MVC filter/action pipeline.
    /// Measures the duration of the exception handling phase and records it as a metric.
    /// </summary>
    /// <param name="context">
    /// The <see cref="ExceptionContext"/> containing the current <see cref="Microsoft.AspNetCore.Http.HttpContext"/>
    /// and exception details.
    /// </param>
    /// <returns>A <see cref="Task"/> representing the asynchronous filter operation.</returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// Skips timing if <see cref="AspNetCoreMetricOptions.EnableExceptionTiming"/> is disabled,
    /// or the randomized sampling check fails.
    /// </description></item>
    /// <item><description>
    /// Resolves normalized route, HTTP method, scheme, and protocol flavor from the current request.
    /// </description></item>
    /// <item><description>
    /// Uses <see cref="Stopwatch"/> to measure elapsed ticks and converts them to milliseconds via <c>TimeUtil.TicksToMs</c>.
    /// </description></item>
    /// <item><description>
    /// Records the observation under <c>MvcStageNames.Exception</c>.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    public Task OnExceptionAsync(ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_opt.EnableExceptionTiming || (_opt.SamplingRate < 1.0 && _rnd() > _opt.SamplingRate))
        {
            return Task.CompletedTask;
        }

        var http = context.HttpContext;
        var route = RequestRouteResolver.ResolveNormalizedRoute(http, _opt.OtherRouteLabel);
        var method = http.Request.Method;
        var scheme = http.Request.Scheme;
        var flavor = HttpProtocolHelper.GetFlavor(http);

        var start = Stopwatch.GetTimestamp();

        var elapsedMs = (Stopwatch.GetTimestamp() - start) * TimeUtil.TicksToMs;

        _metrics.GetOrCreate(route, method, MvcStageNames.Exception, scheme, flavor)
                .Observe(elapsedMs);

        return Task.CompletedTask;
    }
}
