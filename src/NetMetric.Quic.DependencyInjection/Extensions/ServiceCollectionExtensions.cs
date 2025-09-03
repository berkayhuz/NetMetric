// <copyright file="ServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NetMetric.Quic.Extensions;

/// <summary>
/// Provides extension methods to register QUIC metrics support in an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// These helpers wire up the QUIC metrics pipeline that is based on .NET
/// <c>EventCounters</c> emitted by MsQuic/<c>System.Net.Quic</c>.
/// Registration includes:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// Binding and validating <see cref="QuicOptions"/> via <see cref="OptionsBuilder{TOptions}"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// Creating a singleton <see cref="QuicMetricSet"/> using the application's
/// <see cref="IMetricFactory"/> to expose counters and gauges.
/// </description>
/// </item>
/// <item>
/// <description>
/// Adding a singleton <see cref="IHostedService"/> (<see cref="QuicMetricsHostedService"/>)
/// that starts a best-effort <c>EventListener</c> to translate EventCounters into metrics
/// for the lifetime of the process.
/// </description>
/// </item>
/// </list>
/// <para>
/// These methods are idempotent and safe to call once during application startup.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using NetMetric.Quic.Extensions;
/// using Microsoft.Extensions.DependencyInjection;
/// 
/// var services = new ServiceCollection();
/// 
/// // Minimal registration with defaults:
/// services.AddNetMetricQuic();
/// 
/// // Or with custom options:
/// services.AddNetMetricQuic(opt =>
/// {
///     opt.SamplingIntervalSec = 2;     // Poll EventCounters every 2 seconds
///     opt.EnableFallback = true;       // Publish unknown counters as multi-gauges
///     opt.MaxFallbackSeries = 300;     // Cardinality guard for fallback series
/// });
/// </code>
/// </example>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers QUIC metrics with default <see cref="QuicOptions"/>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that additional calls can be chained.
    /// </returns>
    /// <remarks>
    /// This overload is equivalent to calling
    /// <see cref="AddNetMetricQuic(IServiceCollection, Action{QuicOptions})"/> with a no-op configurator.
    /// </remarks>
    public static IServiceCollection AddNetMetricQuic(this IServiceCollection services)
        => services.AddNetMetricQuic(_ => { });

    /// <summary>
    /// Registers QUIC metrics with custom configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">A delegate to configure <see cref="QuicOptions"/>.</param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that additional calls can be chained.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method:
    /// </para>
    /// <list type="number">
    /// <item><description>Binds <see cref="QuicOptions"/> and applies <paramref name="configure"/>.</description></item>
    /// <item><description>
    /// Creates a singleton <see cref="QuicMetricSet"/> by resolving <see cref="IMetricFactory"/>
    /// and the configured <see cref="QuicOptions"/>.
    /// </description></item>
    /// <item><description>
    /// Registers <see cref="QuicMetricsHostedService"/> as a singleton <see cref="IHostedService"/> to
    /// start/stop the QUIC <c>EventListener</c>.
    /// </description></item>
    /// </list>
    /// <para>
    /// It is expected that your application registers an <see cref="IMetricFactory"/> implementation
    /// beforehand (for example, via your observability/metrics provider).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddNetMetricQuic(options =>
    /// {
    ///     options.SamplingIntervalSec = 1;
    ///     options.EnableFallback = true;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddNetMetricQuic(this IServiceCollection services, Action<QuicOptions> configure)
    {
        // Bind and configure options
        services.AddOptions<QuicOptions>().Configure(configure);

        // Create the metric set once, using the application's IMetricFactory and configured options
        services.TryAddSingleton(sp =>
        {
            var f = sp.GetRequiredService<IMetricFactory>();
            var opt = sp.GetRequiredService<IOptions<QuicOptions>>().Value;
            return new QuicMetricSet(f, opt);
        });

        // Host a background listener that translates EventCounters into metrics
        services.TryAddSingleton<IHostedService, QuicMetricsHostedService>();
        return services;
    }
}
