// <copyright file="ServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMetric.Kestrel.Extensions;

/// <summary>
/// Provides extension methods for registering NetMetric Kestrel server instrumentation
/// into an application's dependency injection (DI) container.
/// </summary>
/// <remarks>
/// <para>
/// The registration wires up a singleton <see cref="KestrelMetricSet"/> configured with
/// TLS handshake histogram buckets and a hosted service (<see cref="KestrelMetricsHostedService"/>)
/// that enables the event listener for Kestrel and TLS sources during the application lifetime.
/// </para>
/// <para>
/// Thread-safety: This API is typically invoked at startup time while the service
/// collection is being composed. Resulting singletons are safe for concurrent use at runtime.
/// </para>
/// <example>
/// The following example shows how to add Kestrel metrics with custom TLS handshake buckets:
/// <code language="csharp"><![CDATA[
/// using NetMetric.Kestrel.Extensions;
///
/// var builder = WebApplication.CreateBuilder(args);
///
/// builder.Services.AddNetMetricCore(); // your core NetMetric setup
/// builder.Services.AddNetMetricKestrel(opt =>
/// {
///     opt.TlsHandshakeBucketsMs = ImmutableArray.Create(0.5, 1, 2, 5, 10, 20, 50, 100, 250, 500, 1000);
/// });
///
/// var app = builder.Build();
/// app.Run();
/// ]]></code>
/// </example>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the NetMetric Kestrel server metrics pipeline.
    /// </summary>
    /// <param name="services">The service collection to add metrics to. Must not be <see langword="null"/>.</param>
    /// <param name="configure">
    /// Optional callback to customize <see cref="KestrelMetricOptions"/> such as TLS handshake
    /// histogram bucket boundaries.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance to allow fluent chaining.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>Creates a <see cref="KestrelMetricOptions"/> instance and applies <paramref name="configure"/> if provided.</description>
    ///   </item>
    ///   <item>
    ///     <description>Builds a singleton <see cref="KestrelMetricSet"/> using the application <see cref="IMetricFactory"/> and sanitized TLS buckets.</description>
    ///   </item>
    ///   <item>
    ///     <description>Registers <see cref="KestrelMetricsHostedService"/> as an <see cref="IHostedService"/> to activate the event listener lifecycle.</description>
    ///   </item>
    /// </list>
    /// <para>
    /// TLS bucket values are validated and normalized (finite, non-negative, distinct, ascending)
    /// before being applied to the handshake duration histogram.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddNetMetricKestrel(
        this IServiceCollection services,
        Action<KestrelMetricOptions>? configure = null)
    {
        var opt = new KestrelMetricOptions();
        configure?.Invoke(opt);

        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IMetricFactory>();
            var buckets = SanitizeBuckets(opt.TlsHandshakeBucketsMs); // ImmutableArray -> double[]
            return new KestrelMetricSet(factory, buckets);
        });

        services.AddSingleton<IHostedService, KestrelMetricsHostedService>();
        return services;
    }

    /// <summary>
    /// Cleans and validates the TLS handshake histogram bucket boundaries.
    /// </summary>
    /// <param name="input">The candidate bucket list (milliseconds) from options.</param>
    /// <returns>
    /// A materialized <see cref="double"/> array suitable for hot-path histogram use.
    /// If <paramref name="input"/> is empty or all values are filtered out, returns
    /// <see cref="KestrelMetricOptions.DefaultTlsHandshakeBucketsMs"/> instead.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Filtering rules:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Non-finite (NaN/∞) values are removed.</description></item>
    ///   <item><description>Negative values are removed.</description></item>
    ///   <item><description>Duplicates are removed.</description></item>
    ///   <item><description>Remaining values are sorted ascending.</description></item>
    /// </list>
    /// <para>
    /// Providing sane, ordered buckets helps ensure meaningful histogram aggregation and avoids
    /// pathological cardinality or runtime validation exceptions in metric backends.
    /// </para>
    /// </remarks>
    private static double[] SanitizeBuckets(ImmutableArray<double> input)
    {
        if (input.IsDefaultOrEmpty)
            return KestrelMetricOptions.DefaultTlsHandshakeBucketsMs.ToArray();

        var cleaned = input
            .Where(d => !double.IsNaN(d) && double.IsFinite(d) && d >= 0)
            .Distinct()
            .OrderBy(d => d)
            .ToArray();

        return cleaned.Length > 0 ? cleaned : KestrelMetricOptions.DefaultTlsHandshakeBucketsMs.ToArray();
    }
}
