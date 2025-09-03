// <copyright file="TimingModelBinderProvider.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NetMetric.AspNetCore.Mvc;

/// <summary>
/// A custom <see cref="IModelBinderProvider"/> that wraps the default model binder
/// with timing instrumentation to measure model binding duration.
/// </summary>
/// <remarks>
/// <para>
/// This provider intercepts the creation of a model binder and wraps it with a
/// <see cref="TimingModelBinder"/> that records elapsed time into the
/// <see cref="MvcMetricSet"/> when <see cref="AspNetCoreMetricOptions.EnableModelBindingTiming"/>
/// is enabled.
/// </para>
/// <para>
/// Reentrancy is prevented via two guards:
/// </para>
/// <list type="bullet">
///   <item><description>Skips when the requested <see cref="BindingInfo.BinderType"/> is already <see cref="TimingModelBinder"/>.</description></item>
///   <item><description>Skips when a per-request guard flag is present in <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.</description></item>
/// </list>
/// </remarks>
/// <example>
/// Register this provider:
/// <code>
/// services.AddControllers(options =>
/// {
///     options.ModelBinderProviders.Insert(0, new TimingModelBinderProvider());
/// });
/// </code>
/// </example>
/// <seealso cref="IModelBinderProvider"/>
/// <seealso cref="IModelBinder"/>
/// <seealso cref="MvcMetricSet"/>
/// <seealso cref="MvcStageNames"/>
/// <seealso cref="AspNetCoreMetricOptions"/>
public sealed class TimingModelBinderProvider : IModelBinderProvider
{
    private const string GuardKey = "__NetMetric.TimingBinder.Guard__";

    /// <inheritdoc />
    /// <summary>
    /// Returns a timing-enabled binder when appropriate, otherwise defers to subsequent providers.
    /// </summary>
    /// <param name="context">The <see cref="ModelBinderProviderContext"/> describing the binding request.</param>
    /// <returns>
    /// A timing-enabled <see cref="IModelBinder"/> when successful; otherwise <see langword="null"/>
    /// to allow the provider chain to continue.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method uses a per-request guard stored in <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>
    /// to avoid infinite recursion during binder resolution.
    /// </para>
    /// <para>
    /// When an inner binder cannot be created (returns <see langword="null"/>), this provider yields <see langword="null"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Guard: avoid recursive fallbacks
        if (context.BindingInfo?.BinderType == typeof(TimingModelBinder))
        {
            return null;
        }

        // Guard 2: avoid re-queuing in the same request
        var http = context.Services.GetService(typeof(Microsoft.AspNetCore.Http.IHttpContextAccessor))
                  as Microsoft.AspNetCore.Http.IHttpContextAccessor;
        if (http?.HttpContext?.Items.ContainsKey(GuardKey) == true)
        {
            return null;
        }

        http?.HttpContext?.Items.Add(GuardKey, true);
        try
        {
            // Delegate to subsequent providers for the actual binder
            var inner = context.CreateBinder(context.Metadata);

            if (inner is null)
            {
                return null;
            }

            return new TimingModelBinder(inner);
        }
        finally
        {
            http?.HttpContext?.Items.Remove(GuardKey);
        }
    }

    /// <summary>
    /// An <see cref="IModelBinder"/> wrapper that measures the duration
    /// of model binding and records it into metrics.
    /// </summary>
    /// <remarks>
    /// When <see cref="AspNetCoreMetricOptions.EnableModelBindingTiming"/> is enabled, this binder:
    /// <list type="number">
    ///   <item><description>Resolves normalized route, method, scheme, and protocol flavor.</description></item>
    ///   <item><description>Measures elapsed time using <c>Stopwatch.GetTimestamp()</c> and converts ticks to ms via <c>TimeUtil.TicksToMs</c>.</description></item>
    ///   <item><description>Records the observation under <see cref="MvcStageNames.ModelBinding"/> in <see cref="MvcMetricSet"/>.</description></item>
    /// </list>
    /// Otherwise, it delegates directly to the inner binder without recording metrics.
    /// </remarks>
    private sealed class TimingModelBinder : IModelBinder
    {
        private readonly IModelBinder _inner;

        /// <summary>
        /// Initializes a new instance of the <see cref="TimingModelBinder"/> class.
        /// </summary>
        /// <param name="inner">The inner model binder to wrap.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is <see langword="null"/>.</exception>
        public TimingModelBinder(IModelBinder inner) => _inner = inner;

        /// <inheritdoc />
        /// <summary>
        /// Binds the model asynchronously, optionally recording model-binding duration as a metric.
        /// </summary>
        /// <param name="bindingContext">The <see cref="ModelBindingContext"/> for the current binding operation.</param>
        /// <returns>A <see cref="Task"/> that completes when the binding operation finishes.</returns>
        /// <remarks>
        /// Throws <see cref="ArgumentException"/> if <see cref="ModelBindingContext.HttpContext"/> is <see langword="null"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="bindingContext"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="bindingContext"/>.<see cref="ModelBindingContext.HttpContext"/> is <see langword="null"/>.
        /// </exception>
        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            ArgumentNullException.ThrowIfNull(bindingContext);

            var http = bindingContext.HttpContext
                      ?? throw new ArgumentException("HttpContext is null.", nameof(bindingContext));

            var opt = http.RequestServices.GetService(typeof(AspNetCoreMetricOptions))
                     as AspNetCoreMetricOptions;

            if (opt is { EnableModelBindingTiming: true })
            {
                var route = RequestRouteResolver.ResolveNormalizedRoute(http, opt.OtherRouteLabel);
                var method = http.Request.Method;
                var scheme = http.Request.Scheme;
                var flavor = HttpProtocolHelper.GetFlavor(http);
                var metrics = http.RequestServices.GetService(typeof(MvcMetricSet)) as MvcMetricSet;

                var start = Stopwatch.GetTimestamp();
                await _inner.BindModelAsync(bindingContext).ConfigureAwait(false);
                var elapsedMs = (Stopwatch.GetTimestamp() - start) * TimeUtil.TicksToMs;

                metrics?.GetOrCreate(route, method, MvcStageNames.ModelBinding, scheme, flavor)
                       .Observe(elapsedMs);
                return;
            }

            await _inner.BindModelAsync(bindingContext).ConfigureAwait(false);
        }
    }
}
