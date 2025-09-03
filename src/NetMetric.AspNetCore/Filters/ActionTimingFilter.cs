// <copyright file="ActionTimingFilter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace NetMetric.AspNetCore.Filters;

/// <summary>
/// An <see cref="IAsyncActionFilter"/> that measures execution time of MVC controller actions
/// and records the observation into the configured <see cref="MvcMetricSet"/>.
/// </summary>
/// <remarks>
/// <para>
/// This filter runs after model binding has completed. If you need separate metrics for model binding
/// or validation, consider introducing dedicated wrappers (e.g., a timing model binder or
/// <see cref="IObjectModelValidator"/> decorator) at a later stage.
/// </para>
/// <para>
/// Optional sampling is honored via <see cref="AspNetCoreMetricOptions.SamplingRate"/> to minimize overhead
/// in high-throughput scenarios. When <see cref="AspNetCoreMetricOptions.EnableActionTiming"/> is disabled,
/// the filter short-circuits without recording any metrics.
/// </para>
/// <para><strong>Thread Safety:</strong> The filter maintains no shared mutable state and is safe under concurrent use.</para>
/// </remarks>
/// <example>
/// Apply this filter globally:
/// <code>
/// services.AddControllers(options =>
/// {
///     options.Filters.Add&lt;ActionTimingFilter&gt;();
/// });
/// </code>
/// </example>
/// <seealso cref="MvcMetricSet"/>
/// <seealso cref="AspNetCoreMetricOptions"/>
/// <seealso cref="MvcStageNames"/>
public sealed class ActionTimingFilter : IAsyncActionFilter
{
    private readonly MvcMetricSet _metrics;
    private readonly AspNetCoreMetricOptions _opt;
    private readonly Func<double> _rnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionTimingFilter"/> class.
    /// </summary>
    /// <param name="metrics">
    /// The <see cref="MvcMetricSet"/> responsible for storing action-level timing metrics.
    /// </param>
    /// <param name="opt">
    /// The <see cref="AspNetCoreMetricOptions"/> containing configuration for sampling rate
    /// and whether action timing is enabled.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="metrics"/> or <paramref name="opt"/> is <see langword="null"/>.
    /// </exception>
    public ActionTimingFilter(MvcMetricSet metrics, AspNetCoreMetricOptions opt)
    {
        _metrics = metrics;
        _opt = opt;
        _rnd = Random.Shared.NextDouble;
    }

    /// <summary>
    /// Executes the action while measuring its execution time in milliseconds.
    /// </summary>
    /// <param name="context">
    /// The current <see cref="ActionExecutingContext"/> providing access to
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> and action arguments.
    /// </param>
    /// <param name="next">
    /// The delegate that, when invoked, executes the action and subsequent filters.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous filter execution.
    /// </returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// If <see cref="AspNetCoreMetricOptions.EnableActionTiming"/> is <see langword="false"/>,
    /// or the randomized sampling check fails, the method forwards execution without recording.
    /// </description></item>
    /// <item><description>
    /// Otherwise, it resolves a normalized route, HTTP method, scheme, and protocol flavor.
    /// </description></item>
    /// <item><description>
    /// Uses <see cref="Stopwatch"/> to calculate elapsed time in ticks, then converts to milliseconds via <c>TimeUtil.TicksToMs</c>.
    /// </description></item>
    /// <item><description>
    /// Observes the measured value under <c>MvcStageNames.Action</c> through <see cref="MvcMetricSet.GetOrCreate(string, string, string, string, string)"/>.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="next"/> is <see langword="null"/>.
    /// </exception>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!_opt.EnableActionTiming || (_opt.SamplingRate < 1.0 && _rnd() > _opt.SamplingRate))
        {
            await next().ConfigureAwait(false);
            return;
        }

        var http = context.HttpContext;
        var route = RequestRouteResolver.ResolveNormalizedRoute(http, _opt.OtherRouteLabel);
        var method = http.Request.Method;
        var scheme = http.Request.Scheme;
        var flavor = HttpProtocolHelper.GetFlavor(http);

        var start = Stopwatch.GetTimestamp();

        await next().ConfigureAwait(false);

        var elapsedMs = (Stopwatch.GetTimestamp() - start) * TimeUtil.TicksToMs;

        _metrics.GetOrCreate(route, method, MvcStageNames.Action, scheme, flavor)
                .Observe(elapsedMs);
    }
}
