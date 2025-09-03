// <copyright file="NetMetricHttpClientServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMetric.HttpClient.Extensions;

/// <summary>
/// Extension methods to integrate NetMetric HTTP client instrumentation into
/// an application's dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// These extensions register the <see cref="HttpClientMetricSet"/> singleton,
/// wire up a diagnostic observer to consume <c>System.Net.*</c> <see cref="System.Diagnostics.DiagnosticSource"/>
/// events, and provide a convenient way to attach a metrics handler to named <see cref="System.Net.Http.HttpClient"/> instances.
/// </para>
/// <para>
/// Typical usage:
/// <code>
/// var services = new ServiceCollection();
/// services.AddNetMetricHttpClient(opts =>
/// {
///     // Optional: customize histogram buckets
///     opts.LatencyBucketsMs = new[] { 1d, 2d, 5d, 10d, 20d, 50d, 100d, 200d, 500d, 1000d };
///     opts.SizeBuckets      = new[] { 128d, 512d, 1024d, 4096d, 16384d, 65536d, 262144d };
/// });
///
/// // Attach timing/size metrics to a named HttpClient
/// services.AddHttpClientWithMetrics("my-api")
///         .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://api.example.com"));
/// </code>
/// </para>
/// <para>
/// Thread safety: registrations performed by these methods are safe for concurrent calls
/// during application startup. The resulting singletons (<see cref="HttpClientMetricSet"/> and
/// <see cref="HttpClientDiagnosticObserver"/>) are designed to be used concurrently at runtime.
/// </para>
/// </remarks>
public static class NetMetricHttpClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds NetMetric HTTP client metrics integration to the service collection.
    /// </summary>
    /// <param name="services">The target <see cref="IServiceCollection"/> to register components into.</param>
    /// <param name="configure">
    /// Optional configuration callback for <see cref="NetMetricHttpClientOptions"/> such as
    /// latency and size histogram buckets.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance to allow fluent chaining.
    /// </returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Registers <see cref="HttpClientMetricSet"/> as a singleton created from the application's
    /// <see cref="IMetricFactory"/> and the provided <see cref="NetMetricHttpClientOptions"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Registers <see cref="HttpClientDiagnosticObserver"/> to listen to <c>System.Net.*</c>
    /// <see cref="System.Diagnostics.DiagnosticListener"/> events (HTTP, DNS, sockets, TLS) and emit phase metrics.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Registers <see cref="DiagnosticObserverHostedService"/> as an <see cref="IHostedService"/> so the observer
    /// is owned and disposed by the host lifecycle.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddNetMetricHttpClient();
    ///
    /// // Later, add a named client and use it via IHttpClientFactory
    /// builder.Services.AddHttpClientWithMetrics("github")
    ///        .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://api.github.com"));
    /// </code>
    /// </example>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddNetMetricHttpClient(
        this IServiceCollection services,
        Action<NetMetricHttpClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var opts = new NetMetricHttpClientOptions();
        configure?.Invoke(opts);

        // HttpClientMetricSet singleton
        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IMetricFactory>();
            return new HttpClientMetricSet(factory, opts.LatencyBucketsMs.ToArray(), opts.SizeBuckets.ToArray());
        });

        // Diagnostic observer
        services.AddSingleton<HttpClientDiagnosticObserver>();

        // Start/own observer via host lifetime
        services.AddSingleton<IHostedService, DiagnosticObserverHostedService>();

        return services;
    }

    /// <summary>
    /// Registers a named <see cref="System.Net.Http.HttpClient"/> and attaches the NetMetric timing handler.
    /// </summary>
    /// <param name="services">The target <see cref="IServiceCollection"/>.</param>
    /// <param name="name">The logical name of the <see cref="System.Net.Http.HttpClient"/> to register.</param>
    /// <returns>
    /// An <see cref="IHttpClientBuilder"/> that can be used for further client configuration
    /// (e.g., base address, default headers, additional handlers).
    /// </returns>
    /// <remarks>
    /// <para>
    /// The injected <see cref="NetMetricTimingHandler"/> records request/response timings and sizes
    /// to the shared <see cref="HttpClientMetricSet"/>. You can call this method multiple times to
    /// register different named clients, each benefiting from the same instrumentation.
    /// </para>
    /// <para>
    /// This method composes with the standard <see cref="IHttpClientFactory"/> pipeline. Any user-defined
    /// primary handler or additional delegating handlers will execute in the usual order around
    /// <see cref="NetMetricTimingHandler"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// services.AddNetMetricHttpClient();
    /// services.AddHttpClientWithMetrics("weather")
    ///         .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));
    /// </code>
    /// </example>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public static IHttpClientBuilder AddHttpClientWithMetrics(
        this IServiceCollection services,
        string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return services.AddHttpClient(name)
            .AddHttpMessageHandler(sp =>
            {
                var metrics = sp.GetRequiredService<HttpClientMetricSet>();
                return new NetMetricTimingHandler(metrics);
            });
    }
}
