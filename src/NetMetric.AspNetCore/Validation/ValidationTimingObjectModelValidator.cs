// <copyright file="ValidationTimingObjectModelValidator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace NetMetric.AspNetCore.Validation;

/// <summary>
/// An <see cref="IObjectModelValidator"/> decorator that measures the execution time
/// of model validation and records the observation into <see cref="MvcMetricSet"/>.
/// </summary>
/// <remarks>
/// <para>
/// This validator wraps the default ASP.NET Core <see cref="IObjectModelValidator"/> to instrument
/// the duration of the validation phase in the MVC pipeline.
/// </para>
/// <para>
/// Timing is collected only when enabled via <see cref="AspNetCoreMetricOptions"/> flags
/// (for example, <see cref="AspNetCoreMetricOptions.EnableActionTiming"/> or
/// <see cref="AspNetCoreMetricOptions.EnableModelBindingTiming"/>). If timing is disabled, the inner
/// validator is invoked without additional overhead.
/// </para>
/// <para><strong>Thread Safety:</strong> The decorator is stateless and safe for concurrent use;
/// all per-request data is obtained from the current <see cref="HttpContext"/>.</para>
/// </remarks>
/// <example>
/// Register this validator as the application's object model validator:
/// <code>
/// builder.Services.AddSingleton&lt;IObjectModelValidator, ValidationTimingObjectModelValidator&gt;();
/// </code>
/// </example>
/// <seealso cref="IObjectModelValidator"/>
/// <seealso cref="MvcMetricSet"/>
/// <seealso cref="MvcStageNames"/>
/// <seealso cref="AspNetCoreMetricOptions"/>
public sealed class ValidationTimingObjectModelValidator : IObjectModelValidator
{
    private readonly IObjectModelValidator _inner;
    private readonly AspNetCoreMetricOptions _opt;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationTimingObjectModelValidator"/> class.
    /// </summary>
    /// <param name="inner">The inner <see cref="IObjectModelValidator"/> to wrap.</param>
    /// <param name="opt">The configuration options controlling metric collection.</param>
    /// <param name="accessor">The <see cref="IHttpContextAccessor"/> used to access the current <see cref="HttpContext"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is <see langword="null"/>.</exception>
    public ValidationTimingObjectModelValidator(
        IObjectModelValidator inner,
        AspNetCoreMetricOptions opt,
        IHttpContextAccessor accessor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _opt = opt ?? throw new ArgumentNullException(nameof(opt));
        _httpContextAccessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    }

    /// <summary>
    /// Validates the given model and measures the elapsed time of the validation process.
    /// </summary>
    /// <param name="actionContext">The <see cref="ActionContext"/> representing the current request.</param>
    /// <param name="validationState">The optional <see cref="ValidationStateDictionary"/>.</param>
    /// <param name="prefix">The model binding prefix.</param>
    /// <param name="model">The model instance to validate.</param>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// Skips timing if timing is disabled in <see cref="AspNetCoreMetricOptions"/>.
    /// </description></item>
    /// <item><description>
    /// Resolves normalized route, HTTP method, scheme, and protocol flavor from <see cref="HttpContext"/> for tagging.
    /// </description></item>
    /// <item><description>
    /// Uses <see cref="Stopwatch.GetTimestamp"/> to measure elapsed ticks, converts to milliseconds via <c>TimeUtil.TicksToMs</c>,
    /// and records the observation under <see cref="MvcStageNames.Validation"/>.
    /// </description></item>
    /// </list>
    /// If the current <see cref="HttpContext"/> is unavailable, validation proceeds without recording metrics.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actionContext"/> is <see langword="null"/>.</exception>
    public void Validate(
        ActionContext actionContext,
        ValidationStateDictionary? validationState,
        string prefix,
        object? model)
    {
        ArgumentNullException.ThrowIfNull(actionContext);

        // NOTE: Consider introducing a dedicated EnableValidationTiming flag in options
        // if validation timing should be toggled independently.
        if (!_opt.EnableActionTiming && !_opt.EnableModelBindingTiming) // remove this check to always measure validation
        {
            _inner.Validate(actionContext, validationState, prefix, model);
            return;
        }

        var http = _httpContextAccessor.HttpContext;
        if (http is null)
        {
            _inner.Validate(actionContext, validationState, prefix, model);
            return;
        }

        var route = RequestRouteResolver.ResolveNormalizedRoute(http, _opt.OtherRouteLabel);
        var method = http.Request.Method;
        var scheme = http.Request.Scheme;
        var flavor = HttpProtocolHelper.GetFlavor(http);
        var metrics = http.RequestServices.GetService(typeof(MvcMetricSet)) as MvcMetricSet;

        var start = Stopwatch.GetTimestamp();
        _inner.Validate(actionContext, validationState, prefix, model);
        var elapsedMs = (Stopwatch.GetTimestamp() - start) * TimeUtil.TicksToMs;

        metrics?.GetOrCreate(route, method, MvcStageNames.Validation, scheme, flavor)
               .Observe(elapsedMs);
    }
}
