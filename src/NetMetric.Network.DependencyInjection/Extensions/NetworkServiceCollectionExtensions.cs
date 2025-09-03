// <copyright file="NetworkServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetMetric.Abstractions;
using NetMetric.Network.Configuration;
using NetMetric.Network.Http;

namespace NetMetric.Network.DependencyInjection;

/// <summary>
/// Provides extension methods for adding network-related services, including network timing handlers, to the <see cref="IServiceCollection"/>.
/// </summary>
public static class NetworkServiceCollectionExtensions
{
    /// <summary>
    /// Adds network timing components to the dependency injection container, including network timing options and handlers.
    /// After calling this method, you can add the <see cref="HttpNetworkTimingHandler"/> through an <see cref="IHttpClientBuilder"/>.
    /// </summary>
    /// <param name="services">The service collection to which the network timing components are added.</param>
    /// <param name="configure">An optional action to configure the <see cref="NetworkTimingOptions"/>.</param>
    /// <returns>The service collection with the network timing components added.</returns>
    public static IServiceCollection AddNetMetricHttpTiming(this IServiceCollection services, Action<NetworkTimingOptions>? configure = null)
    {
        // Configure options if provided
        if (configure is not null)
        {
            services.AddOptions<NetworkTimingOptions>().Configure(configure);
        }
        else
        {
            services.AddOptions<NetworkTimingOptions>();
        }

        // Add HttpNetworkTimingHandler: It requires ITimerSink and NetworkTimingOptions to be injected
        services.TryAddTransient<HttpNetworkTimingHandler>(sp =>
        {
            var sink = sp.GetRequiredService<ITimerSink>(); // Comes from Core package
            var opts = sp.GetRequiredService<IOptions<NetworkTimingOptions>>().Value;

            return new HttpNetworkTimingHandler(sink, opts);
        });

        // Add simple TimingHandler (with id/name parameters for factory)
        services.TryAddTransient<TimingHandler>(sp => new TimingHandler(sp.GetRequiredService<ITimerSink>()));

        return services;
    }

    /// <summary>
    /// Adds an advanced <see cref="HttpNetworkTimingHandler"/> to the <see cref="IHttpClientBuilder"/>.
    /// </summary>
    /// <param name="builder">The HTTP client builder to which the handler is added.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> with the timing handler added.</returns>
    public static IHttpClientBuilder AddNetMetricHttpTiming(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddNetMetricHttpTiming();
        builder.AddHttpMessageHandler<HttpNetworkTimingHandler>();

        return builder;
    }

    /// <summary>
    /// Adds an advanced <see cref="HttpNetworkTimingHandler"/> to the <see cref="IHttpClientBuilder"/> with custom options.
    /// </summary>
    /// <param name="builder">The HTTP client builder to which the handler is added.</param>
    /// <param name="configure">An action to configure the <see cref="NetworkTimingOptions"/>.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> with the configured timing handler added.</returns>
    public static IHttpClientBuilder AddNetMetricHttpTiming(this IHttpClientBuilder builder, Action<NetworkTimingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddNetMetricHttpTiming(configure);
        builder.AddHttpMessageHandler<HttpNetworkTimingHandler>();

        return builder;
    }

    /// <summary>
    /// Adds a simple <see cref="TimingHandler"/> to the <see cref="IHttpClientBuilder"/>. Optionally, custom <paramref name="id"/> and <paramref name="name"/> can be specified.
    /// </summary>
    /// <param name="builder">The HTTP client builder to which the handler is added.</param>
    /// <param name="id">The optional metric ID for the timing handler.</param>
    /// <param name="name">The optional display name for the timing handler.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> with the simple timing handler added.</returns>
    public static IHttpClientBuilder AddNetMetricSimpleTiming(this IHttpClientBuilder builder, string? id = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Inject a custom instance of TimingHandler with id/name parameters
        builder.Services.AddTransient(sp => new TimingHandler(sp.GetRequiredService<ITimerSink>(), id ?? "http.client.duration", name ?? "HTTP Client Duration"));

        builder.AddHttpMessageHandler<TimingHandler>();

        return builder;
    }
}
