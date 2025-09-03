// <copyright file="ServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace NetMetric.SignalR.DependencyInjection;

/// <summary>
/// Provides extension methods to register SignalR metrics instrumentation components into the application's
/// dependency injection (DI) container.
/// </summary>
/// <remarks>
/// <para>
/// <b>What gets registered</b><br/>
/// The NetMetric SignalR instrumentation includes:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="ISignalRMetrics"/> via <see cref="SignalRMetricSet"/> (the concrete metrics sink/aggregator)</description></item>
///   <item><description><see cref="IHubFilter"/> via <see cref="SignalRHubFilter"/> (global hub method/connection instrumentation)</description></item>
///   <item><description>
/// A decorator over <see cref="HubLifetimeManager{THub}"/> (see <c>InstrumentedHubLifetimeManager&lt;&gt;</c>) to record group metrics
/// </description></item>
/// </list>
/// <para>
/// <b>Negotiation metrics</b><br/>
/// Negotiation metrics middleware is not added here; you should add it separately in the HTTP pipeline
/// (see remarks on <see cref="AddNetMetricSignalR(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{SignalRMetricsOptions}?)"/>).
/// </para>
/// <para>
/// <b>Thread-safety</b><br/>
/// The registered services are singletons and are expected to be thread-safe. Instrumentation paths may be invoked
/// concurrently by multiple hubs and connections.
/// </para>
/// <para>
/// <b>AOT / trimming</b><br/>
/// The optional decoration of <see cref="HubLifetimeManager{THub}"/> uses reflection and dynamic activation
/// (see <see cref="DecorateExtensions.TryDecorate(IServiceCollection, Type, Type)"/>). When NativeAOT or aggressive trimming is enabled,
/// decoration is attempted only if <see cref="RuntimeFeature.IsDynamicCodeSupported"/> is <see langword="true"/>.
/// </para>
/// </remarks>
/// <seealso cref="ISignalRMetrics"/>
/// <seealso cref="SignalRMetricSet"/>
/// <seealso cref="SignalRHubFilter"/>
/// <seealso cref="NegotiationMetricsMiddleware"/>
/// <seealso cref="DecorateExtensions.TryDecorate(IServiceCollection, System.Type, System.Type)"/>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers NetMetric SignalR instrumentation components such as hub method tracking, connection lifecycle metrics,
    /// (separately added) negotiation metrics middleware, message size histograms, error classification, and group activity counters.
    /// </summary>
    /// <param name="services">The service collection to register services with. Must not be <see langword="null"/>.</param>
    /// <param name="configure">Optional callback to configure <see cref="SignalRMetricsOptions"/>. When omitted, default options are used.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Actions performed</b>
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     Registers a singleton instance of <see cref="SignalRMetricsOptions"/> configured via <paramref name="configure"/>.
    ///   </item>
    ///   <item>
    ///     Registers <see cref="ISignalRMetrics"/> as <see cref="SignalRMetricSet"/>, which depends on
    ///     <c>NetMetric.Abstractions.IMetricFactory</c> and <see cref="SignalRMetricsOptions"/>.
    ///   </item>
    ///   <item>
    ///     Adds <see cref="SignalRHubFilter"/> and applies it globally to all hubs using <see cref="HubOptions"/>.
    ///   </item>
    ///   <item>
    ///     Attempts to decorate <see cref="HubLifetimeManager{THub}"/> with <c>InstrumentedHubLifetimeManager&lt;&gt;</c>
    ///     to produce group metrics (only when <see cref="RuntimeFeature.IsDynamicCodeSupported"/> is <see langword="true"/>).
    ///   </item>
    /// </list>
    /// <para>
    /// <b>Negotiation metrics</b><br/>
    /// To record <c>/negotiate</c> timings and errors, add the middleware separately in the HTTP pipeline:
    /// </para>
    /// <code language="csharp"><![CDATA[
    /// app.UseMiddleware<NegotiationMetricsMiddleware>();
    /// ]]></code>
    /// <para>
    /// <b>Example</b>
    /// </para>
    /// <code language="csharp"><![CDATA[
    /// services.AddNetMetricSignalR(opt =>
    /// {
    ///     opt.NormalizeTransport = true;
    ///     opt.MethodSampleRate = 0.25; // sample 25% of method durations
    /// });
    ///
    /// // Later in pipeline:
    /// app.UseMiddleware<NegotiationMetricsMiddleware>();
    /// app.MapHub<ChatHub>("/hubs/chat");
    /// ]]></code>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    /// <seealso cref="SignalRMetricsOptions"/>
    /// <seealso cref="SignalRHubFilter"/>
    /// <seealso cref="InstrumentedHubLifetimeManager{THub}"/>
    /// <seealso cref="NegotiationMetricsMiddleware"/>
    public static IServiceCollection AddNetMetricSignalR(this IServiceCollection services, Action<SignalRMetricsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure and register options
        var opt = new SignalRMetricsOptions();
        configure?.Invoke(opt);
        services.AddSingleton(opt);

        // Metrics surface: SignalRMetricSet (requires IMetricFactory + options)
        services.AddSingleton<ISignalRMetrics, SignalRMetricSet>(sp =>
        {
            var factory = sp.GetRequiredService<NetMetric.Abstractions.IMetricFactory>();
            var options = sp.GetRequiredService<SignalRMetricsOptions>();
            return new SignalRMetricSet(factory, options);
        });

        // Hub method & connection instrumentation via a global hub filter
        services.AddSingleton<SignalRHubFilter>();
        services.AddSignalR(options =>
        {
            // Apply our filter to all hubs
            options.AddFilter<SignalRHubFilter>();
        });

        // Decorate HubLifetimeManager<T> with the instrumented group-aware wrapper when dynamic code is available
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
#pragma warning disable IL2026 // reflection used on purpose, guarded by IsDynamicCodeSupported
            services.TryDecorate(typeof(HubLifetimeManager<>), typeof(InstrumentedHubLifetimeManager<>));
#pragma warning restore IL2026
        }

        return services;
    }
}
