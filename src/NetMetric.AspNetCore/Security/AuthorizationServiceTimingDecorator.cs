// <copyright file="AuthorizationServiceTimingDecorator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NetMetric.AspNetCore.Security;

/// <summary>
/// An <see cref="IAuthorizationService"/> decorator that measures
/// the execution time of authorization decisions and records them as metrics.
/// </summary>
/// <remarks>
/// <para>
/// This decorator wraps the default <see cref="IAuthorizationService"/> implementation
/// and records elapsed time for authorization requirement evaluation.
/// Timing is collected only if <see cref="AspNetCoreMetricOptions.EnableAuthorizationDecisionTiming"/>
/// is enabled and the sampling check passes (see <see cref="AspNetCoreMetricOptions.SamplingRate"/>).
/// </para>
/// <para>
/// Route, HTTP method, scheme, and protocol flavor are resolved from
/// <see cref="IHttpContextAccessor"/> for tagging metrics.
/// </para>
/// <para><strong>Thread Safety:</strong> The decorator keeps no shared mutable state and is safe to use
/// concurrently across requests; all state is scoped to the current <see cref="HttpContext"/>.</para>
/// </remarks>
/// <example>
/// Register this decorator (e.g., using Scrutor):
/// <code>
/// services.Decorate&lt;IAuthorizationService, AuthorizationServiceTimingDecorator&gt;();
/// </code>
/// </example>
/// <seealso cref="IAuthorizationService"/>
/// <seealso cref="NetMetric.AspNetCore.Internal.MvcMetricSet"/>
/// <seealso cref="NetMetric.AspNetCore.Internal.MvcStageNames"/>
/// <seealso cref="NetMetric.AspNetCore.Options.AspNetCoreMetricOptions"/>
public sealed class AuthorizationServiceTimingDecorator : IAuthorizationService
{
    private readonly IAuthorizationService _inner;
    private readonly AspNetCoreMetricOptions _opt;
    private readonly IServiceProvider _sp;
    private readonly Func<double> _rnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationServiceTimingDecorator"/> class.
    /// </summary>
    /// <param name="inner">The inner <see cref="IAuthorizationService"/> implementation to wrap.</param>
    /// <param name="opt">The configuration options controlling metric collection.</param>
    /// <param name="sp">The <see cref="IServiceProvider"/> used to resolve <see cref="IHttpContextAccessor"/>.</param>
    public AuthorizationServiceTimingDecorator(
        IAuthorizationService inner,
        AspNetCoreMetricOptions opt,
        IServiceProvider sp)
    {
        _inner = inner;
        _opt = opt;
        _sp = sp;
        _rnd = Random.Shared.NextDouble;
    }

    /// <summary>
    /// Authorizes the specified user for the given resource and requirements,
    /// measuring how long the decision stage takes.
    /// </summary>
    /// <param name="user">The <see cref="System.Security.Claims.ClaimsPrincipal"/> representing the current user.</param>
    /// <param name="resource">The resource being accessed. May be <see langword="null"/>.</param>
    /// <param name="requirements">The set of authorization requirements to evaluate.</param>
    /// <returns>
    /// An <see cref="AuthorizationResult"/> indicating whether authorization succeeded.
    /// </returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// Skips timing if <see cref="AspNetCoreMetricOptions.EnableAuthorizationDecisionTiming"/> is disabled
    /// or the randomized sampling check fails.
    /// </description></item>
    /// <item><description>
    /// Resolves route, method, scheme, and protocol flavor from <see cref="IHttpContextAccessor"/> if available.
    /// </description></item>
    /// <item><description>
    /// Uses <c>Stopwatch.GetTimestamp()</c> to measure elapsed ticks, converts to milliseconds via <c>TimeUtil.TicksToMs</c>,
    /// and records the observation under <c>MvcStageNames.AuthzDecision</c>.
    /// </description></item>
    /// </list>
    /// </remarks>
    public async Task<AuthorizationResult> AuthorizeAsync(
       System.Security.Claims.ClaimsPrincipal user,
       object? resource,
       IEnumerable<IAuthorizationRequirement> requirements)
    {
        if (!_opt.EnableAuthorizationDecisionTiming || (_opt.SamplingRate < 1.0 && _rnd() > _opt.SamplingRate))
            return await _inner.AuthorizeAsync(user, resource, requirements).ConfigureAwait(false);

        var http = _sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
        var route = http is null ? _opt.OtherRouteLabel : RequestRouteResolver.ResolveNormalizedRoute(http, _opt.OtherRouteLabel);
        var method = http?.Request.Method ?? "UNKNOWN";
        var scheme = http?.Request.Scheme ?? "UNKNOWN";
        var flavor = http is null ? "1.1" : HttpProtocolHelper.GetFlavor(http);

        var start = Stopwatch.GetTimestamp();
        var result = await _inner.AuthorizeAsync(user, resource, requirements).ConfigureAwait(false);
        var elapsedMs = (Stopwatch.GetTimestamp() - start) * TimeUtil.TicksToMs;

        var metrics = http?.RequestServices.GetService(typeof(MvcMetricSet)) as MvcMetricSet;
        metrics?.GetOrCreate(route, method, MvcStageNames.AuthzDecision, scheme, flavor)
               .Observe(elapsedMs);

        return result;
    }

    /// <summary>
    /// Authorizes the specified user for the given resource and policy name.
    /// This overload does not include timing instrumentation.
    /// </summary>
    /// <param name="user">The <see cref="System.Security.Claims.ClaimsPrincipal"/> representing the current user.</param>
    /// <param name="resource">The resource being accessed. May be <see langword="null"/>.</param>
    /// <param name="policyName">The name of the authorization policy to evaluate.</param>
    /// <returns>
    /// An <see cref="AuthorizationResult"/> indicating whether authorization succeeded.
    /// </returns>
    public Task<AuthorizationResult> AuthorizeAsync(
        System.Security.Claims.ClaimsPrincipal user,
        object? resource,
        string policyName)
        => _inner.AuthorizeAsync(user, resource, policyName);
}
