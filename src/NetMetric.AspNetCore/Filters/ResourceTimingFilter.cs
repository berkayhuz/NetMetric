// <copyright file="ResourceTimingFilter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Mvc.Filters;

namespace NetMetric.AspNetCore.Filters;

/// <summary>
/// An <see cref="IAsyncResourceFilter"/> that measures the execution time of the resource stage
/// in the ASP.NET Core MVC request pipeline, and records the observation into the configured
/// <see cref="MvcMetricSet"/>.
/// </summary>
/// <remarks>
/// <para>
/// The resource filter stage wraps the entire execution of the action and its filters,
/// except for authorization. It is commonly used for caching, global resource initialization,
/// or scoping request lifetime services.
/// </para>
/// <para>
/// Timing is collected only if <see cref="AspNetCoreMetricOptions.EnableResourceTiming"/> is enabled.
/// Sampling support is provided through <see cref="AspNetCoreMetricOptions.SamplingRate"/> to reduce overhead
/// on high-throughput applications.
/// </para>
/// <para><strong>Thread Safety:</strong> This filter maintains no shared mutable state and is safe under concurrent use.</para>
/// </remarks>
/// <example>
/// Register globally:
/// <code>
/// services.AddControllers(options =>
/// {
///     options.Filters.Add&lt;ResourceTimingFilter&gt;();
/// });
/// </code>
/// </example>
/// <seealso cref="MvcMetricSet"/>
/// <seealso cref="AspNetCoreMetricOptions"/>
/// <seealso cref="MvcStageNames"/>
public sealed class ResourceTimingFilter : IAsyncResourceFilter
{
    private readonly MvcMetricSet _metrics;
    private readonly AspNetCoreMetricOptions _opt;
    private readonly Func<double> _rnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceTimingFilter"/> class.
    /// </summary>
    /// <param name="metrics">The <see cref="MvcMetricSet"/> used to store resource-stage timing metrics.</param>
    /// <param name="opt">The configuration options controlling sampling and enablement.</param>
    public ResourceTimingFilter(MvcMetricSet metrics, AspNetCoreMetricOptions opt)
    {
        _metrics = metrics;
        _opt = opt;
        _rnd = Random.Shared.NextDouble;
    }

    /// <summary>
    /// Executes the resource filter stage, measuring elapsed time in milliseconds.
    /// </summary>
    /// <param name="context">
    /// The <see cref="ResourceExecutingContext"/> providing access to
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> and resource filter data.
    /// </param>
    /// <param name="next">
    /// The delegate that continues the resource execution pipeline and invokes subsequent filters/actions.
    /// </param>
    /// <returns>A <see cref="Task"/> that completes when resource execution is finished.</returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// Skips timing if <see cref="AspNetCoreMetricOptions.EnableResourceTiming"/> is <see langword="false"/>,
    /// or the randomized sampling check fails.
    /// </description></item>
    /// <item><description>
    /// Otherwise, resolves normalized route, HTTP method, scheme, and protocol flavor
    /// (via <see cref="HttpProtocolHelper.GetFlavor(Microsoft.AspNetCore.Http.HttpContext)"/>).
    /// </description></item>
    /// <item><description>
    /// Uses <see cref="Stopwatch"/> to calculate elapsed ticks and converts them to milliseconds via <c>TimeUtil.TicksToMs</c>.
    /// </description></item>
    /// <item><description>
    /// Records the observation in the <c>MvcStageNames.Resource</c> stage of the metric set using
    /// <see cref="MvcMetricSet.GetOrCreate(string, string, string, string, string)"/> followed by <c>Observe(...)</c>.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="next"/> is <see langword="null"/>.
    /// </exception>
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!_opt.EnableResourceTiming || (_opt.SamplingRate < 1.0 && _rnd() > _opt.SamplingRate))
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

        _ = await next().ConfigureAwait(false);

        var elapsedMs = (Stopwatch.GetTimestamp() - start) * TimeUtil.TicksToMs;

        _metrics
            .GetOrCreate(route, method, MvcStageNames.Resource, scheme, flavor)
            .Observe(elapsedMs);
    }
}
