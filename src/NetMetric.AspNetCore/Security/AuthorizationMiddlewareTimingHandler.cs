// <copyright file="AuthorizationMiddlewareTimingHandler.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace NetMetric.AspNetCore.Security;

/// <summary>
/// An <see cref="IAuthorizationMiddlewareResultHandler"/> decorator that measures
/// the execution time of authorization decision handling and records it as a metric.
/// </summary>
/// <remarks>
/// <para>
/// This handler wraps the default <see cref="IAuthorizationMiddlewareResultHandler"/>
/// to instrument the time spent evaluating authorization policies and producing
/// the final decision result.
/// </para>
/// <para>
/// Timing is collected only when
/// <see cref="AspNetCoreMetricOptions.EnableAuthorizationDecisionTiming"/> is enabled.
/// </para>
/// <para><strong>Thread Safety:</strong> The handler holds no shared mutable state and is safe for
/// concurrent use; all state is scoped to the current request.</para>
/// </remarks>
/// <example>
/// To register this handler:
/// <code>
/// services.AddSingleton&lt;IAuthorizationMiddlewareResultHandler, AuthorizationMiddlewareTimingHandler&gt;();
/// </code>
/// </example>
/// <seealso cref="IAuthorizationMiddlewareResultHandler"/>
/// <seealso cref="AuthorizationPolicy"/>
/// <seealso cref="PolicyAuthorizationResult"/>
/// <seealso cref="NetMetric.AspNetCore.Internal.MvcMetricSet"/>
/// <seealso cref="NetMetric.AspNetCore.Internal.MvcStageNames"/>
/// <seealso cref="NetMetric.AspNetCore.Options.AspNetCoreMetricOptions"/>
public sealed class AuthorizationMiddlewareTimingHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly IAuthorizationMiddlewareResultHandler _inner;
    private readonly AspNetCoreMetricOptions _opt;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationMiddlewareTimingHandler"/> class.
    /// </summary>
    /// <param name="inner">The inner <see cref="IAuthorizationMiddlewareResultHandler"/> to wrap.</param>
    /// <param name="opt">The configuration options controlling metric collection.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="inner"/> or <paramref name="opt"/> is <see langword="null"/>.
    /// </exception>
    public AuthorizationMiddlewareTimingHandler(
        IAuthorizationMiddlewareResultHandler inner,
        AspNetCoreMetricOptions opt)
    {
        _inner = inner;
        _opt = opt;
    }

    /// <summary>
    /// Handles the result of an authorization policy evaluation,
    /// measuring how long the decision stage takes.
    /// </summary>
    /// <param name="next">The next <see cref="RequestDelegate"/> in the pipeline.</param>
    /// <param name="context">The current <see cref="HttpContext"/>.</param>
    /// <param name="policy">The <see cref="AuthorizationPolicy"/> being evaluated.</param>
    /// <param name="authorizeResult">The <see cref="PolicyAuthorizationResult"/> produced by policy evaluation.</param>
    /// <returns>A task representing the asynchronous handling operation.</returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// Skips timing if <see cref="AspNetCoreMetricOptions.EnableAuthorizationDecisionTiming"/> is disabled.
    /// </description></item>
    /// <item><description>
    /// Resolves route, HTTP method, scheme, and protocol flavor for tagging the observation.
    /// </description></item>
    /// <item><description>
    /// Uses <c>Stopwatch.GetTimestamp()</c> to measure elapsed ticks and converts to milliseconds via <c>TimeUtil.TicksToMs</c>,
    /// then records under <see cref="NetMetric.AspNetCore.Internal.MvcStageNames.AuthzDecision"/>.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_opt.EnableAuthorizationDecisionTiming)
        {
            await _inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
            return;
        }

        var route = RequestRouteResolver.ResolveNormalizedRoute(context, _opt.OtherRouteLabel);
        var method = context.Request.Method;
        var scheme = context.Request.Scheme;
        var flavor = HttpProtocolHelper.GetFlavor(context);
        var metrics = context.RequestServices.GetService(typeof(MvcMetricSet)) as MvcMetricSet;

        var start = Stopwatch.GetTimestamp();

        await _inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);

        var elapsedMs = (Stopwatch.GetTimestamp() - start) * TimeUtil.TicksToMs;

        metrics?.GetOrCreate(route, method, MvcStageNames.AuthzDecision, scheme, flavor)
               .Observe(elapsedMs);
    }
}
