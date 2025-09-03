// <copyright file="AuthorizationTimingFilter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Mvc.Filters;

namespace NetMetric.AspNetCore.Filters;

/// <summary>
/// An <see cref="IAsyncAuthorizationFilter"/> that measures the execution time
/// of the authorization stage and records it into the configured <see cref="MvcMetricSet"/>.
/// </summary>
/// <remarks>
/// <para>
/// This filter does not make authorization decisions itself; it only observes the time taken
/// while authorization policies and attributes are evaluated by ASP.NET Core.
/// </para>
/// <para>
/// Timing is collected only when <see cref="AspNetCoreMetricOptions.EnableAuthorizationTiming"/> is enabled.  
/// Optional sampling is applied via <see cref="AspNetCoreMetricOptions.SamplingRate"/> to reduce overhead.
/// </para>
/// <para><strong>Thread Safety:</strong> The filter maintains no shared mutable state and is safe under concurrent use.</para>
/// </remarks>
/// <example>
/// Register globally:
/// <code>
/// services.AddControllers(options =>
/// {
///     options.Filters.Add&lt;AuthorizationTimingFilter&gt;();
/// });
/// </code>
/// </example>
/// <seealso cref="MvcMetricSet"/>
/// <seealso cref="AspNetCoreMetricOptions"/>
/// <seealso cref="MvcStageNames"/>
public sealed class AuthorizationTimingFilter : IAsyncAuthorizationFilter
{
    private readonly MvcMetricSet _metrics;
    private readonly AspNetCoreMetricOptions _opt;
    private readonly Func<double> _rnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationTimingFilter"/> class.
    /// </summary>
    /// <param name="metrics">The <see cref="MvcMetricSet"/> used to store authorization timing metrics.</param>
    /// <param name="opt">The configuration options controlling sampling and enablement.</param>
    public AuthorizationTimingFilter(MvcMetricSet metrics, AspNetCoreMetricOptions opt)
    {
        _metrics = metrics;
        _opt = opt;
        _rnd = Random.Shared.NextDouble;
    }

    /// <summary>
    /// Called asynchronously during the authorization stage of the MVC filter pipeline,
    /// measuring its duration in milliseconds.
    /// </summary>
    /// <param name="context">
    /// The <see cref="AuthorizationFilterContext"/> that provides access to
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> and route data.
    /// </param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// Skips timing if <see cref="AspNetCoreMetricOptions.EnableAuthorizationTiming"/> is disabled,
    /// or the randomized sampling check fails.
    /// </description></item>
    /// <item><description>
    /// Otherwise, resolves the normalized route, HTTP method, scheme, and protocol flavor (via
    /// <see cref="HttpProtocolHelper.GetFlavor(Microsoft.AspNetCore.Http.HttpContext)"/>).
    /// </description></item>
    /// <item><description>
    /// Uses <see cref="Stopwatch"/> to measure elapsed ticks and converts them to milliseconds via <c>TimeUtil.TicksToMs</c>.
    /// </description></item>
    /// <item><description>
    /// Records the observation under <c>MvcStageNames.Authorization</c> using
    /// <see cref="MvcMetricSet.GetOrCreate(string, string, string, string, string)"/> followed by <c>Observe(...)</c>.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_opt.EnableAuthorizationTiming || (_opt.SamplingRate < 1.0 && _rnd() > _opt.SamplingRate))
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return;
        }

        var http = context.HttpContext;
        var route = RequestRouteResolver.ResolveNormalizedRoute(http, _opt.OtherRouteLabel);
        var method = http.Request.Method;
        var scheme = http.Request.Scheme;
        var flavor = HttpProtocolHelper.GetFlavor(http);

        var start = Stopwatch.GetTimestamp();

        await Task.CompletedTask.ConfigureAwait(false);

        var elapsedMs = (Stopwatch.GetTimestamp() - start) * TimeUtil.TicksToMs;

        _metrics.GetOrCreate(route, method, MvcStageNames.Authorization, scheme, flavor)
                .Observe(elapsedMs);
    }
}
