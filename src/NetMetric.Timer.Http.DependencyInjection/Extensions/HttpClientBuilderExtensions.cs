// <copyright file="HttpClientBuilderExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using NetMetric.Abstractions;
using NetMetric.Timer.Http;

namespace NetMetric.Timer.DependencyInjection;

/// <summary>
/// Provides extension methods for <see cref="IHttpClientBuilder"/> to integrate 
/// NetMetric timing functionality into HTTP client pipelines.
/// The class is placed within the <c>Microsoft.Extensions.DependencyInjection</c> namespace 
/// to enhance discoverability in IntelliSense.
/// </summary>
public static class HttpClientBuilderExtensions
{
    /// <summary>
    /// Adds a <see cref="TimingHandler"/> to the HTTP client pipeline with the default metric ID 
    /// (<c>http.client.duration</c>) and name (<c>HTTP Client Duration</c>).
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/> instance to which the handler will be added.</param>
    /// <returns>The updated <see cref="IHttpClientBuilder"/> to allow method chaining.</returns>
    /// <remarks>
    /// This extension method simplifies the process of adding a default timing handler to your HTTP client,
    /// enabling automatic measurement of HTTP client durations without any custom configuration.
    /// </remarks>
    public static IHttpClientBuilder AddNetMetricSimpleTimerHandler(this IHttpClientBuilder builder)
        => builder.AddNetMetricSimpleTimerHandler(id: null, name: null);

    /// <summary>
    /// Adds a <see cref="TimingHandler"/> to the HTTP client pipeline with a custom metric ID and name.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/> instance to which the handler will be added.</param>
    /// <param name="id">The custom metric ID. If null, defaults to <c>http.client.duration</c>.</param>
    /// <param name="name">The custom metric name. If null, defaults to <c>HTTP Client Duration</c>.</param>
    /// <returns>The updated <see cref="IHttpClientBuilder"/> to allow method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the <paramref name="builder"/> parameter is null.</exception>
    /// <remarks>
    /// This extension method allows you to configure the timing handler with custom metric identifiers 
    /// to suit your specific requirements. It is particularly useful when you need to customize the 
    /// metric ID or name for reporting or tracking purposes.
    /// </remarks>
    public static IHttpClientBuilder AddNetMetricSimpleTimerHandler(this IHttpClientBuilder builder, string? id, string? name)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Register the TimingHandler as a transient service
        builder.Services.AddTransient(sp =>
            new TimingHandler(
                sp.GetRequiredService<ITimerSink>(),
                id ?? "http.client.duration",
                name ?? "HTTP Client Duration"));

        // Add the TimingHandler to the HTTP message handler pipeline
        builder.AddHttpMessageHandler<TimingHandler>();

        return builder;
    }
}
