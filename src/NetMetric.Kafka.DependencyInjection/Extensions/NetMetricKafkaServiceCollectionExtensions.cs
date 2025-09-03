// <copyright file="NetMetricKafkaServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetMetric.Abstractions;
using NetMetric.Kafka.Abstractions;
using NetMetric.Kafka.Configurations;
using NetMetric.Kafka.Modules;

namespace NetMetric.Kafka.DependencyInjection;

/// <summary>
/// Extension methods for registering NetMetric Kafka integration with the
/// <see cref="IServiceCollection"/> dependency injection (DI) container.
/// </summary>
/// <remarks>
/// <para>
/// This type wires the Kafka metrics module (<see cref="KafkaModule"/>) into your application by:
/// </para>
/// <list type="bullet">
///   <item><description>Adding and optionally configuring <see cref="KafkaModuleOptions"/>.</description></item>
///   <item><description>Discovering any registered <see cref="IKafkaStatsSource"/> instances
///   (e.g., Confluent client statistics sources) to serve as inputs.</description></item>
///   <item><description>Resolving the core <see cref="IMetricFactory"/> to emit metrics.</description></item>
///   <item><description>Optionally attaching an <see cref="IKafkaLagProbe"/> for consumer lag metrics
///   if one is available in the container.</description></item>
/// </list>
/// <para>
/// You typically call <see cref="AddNetMetricKafka(IServiceCollection, Action{KafkaModuleOptions}?)"/>
/// from your application startup to enable Kafka metrics collection.
/// </para>
/// <para><b>Requirements</b></para>
/// <list type="bullet">
///   <item><description>An <see cref="IMetricFactory"/> must be registered before the module is created.</description></item>
///   <item><description>At least one <see cref="IKafkaStatsSource"/> should be registered to provide client statistics;</description></item>
///   <item><description><see cref="IKafkaLagProbe"/> is optional and only required if consumer lag metrics are desired.</description></item>
/// </list>
/// </remarks>
/// <threadsafety>
/// This type only adds registrations to the DI container and does not maintain shared mutable state.
/// It is safe to call during application startup and is not intended for use after the container is built.
/// </threadsafety>
/// <example>
/// <para><b>Minimal setup</b></para>
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using NetMetric.Abstractions;
/// using NetMetric.Kafka.Abstractions;
/// using NetMetric.Kafka.Configurations;
///
/// var services = new ServiceCollection();
///
/// // Register a metric factory (implementation-specific).
/// services.AddSingleton<IMetricFactory, MyMetricFactory>();
///
/// // Register one or more Kafka stats sources (e.g., wrapping Confluent client statistics).
/// services.AddSingleton<IKafkaStatsSource, MyConfluentKafkaStatsSource>();
///
/// // Add the NetMetric Kafka module with default options.
/// services.AddNetMetricKafka();
///
/// var provider = services.BuildServiceProvider();
/// ]]></code>
/// </example>
/// <example>
/// <para><b>Configuring options and lag probe</b></para>
/// <code language="csharp"><![CDATA[
/// var services = new ServiceCollection();
///
/// services.AddSingleton<IMetricFactory, MyMetricFactory>();
/// services.AddSingleton<IKafkaStatsSource, MyConfluentKafkaStatsSource>();
///
/// // Optional: register a lag probe to enable consumer lag metrics.
/// services.AddSingleton<IKafkaLagProbe, AdminClientLagProbe>();
///
/// // Configure module options (e.g., base tags, enabling collectors, etc.).
/// services.AddNetMetricKafka(options =>
/// {
///     options.BaseTags["cluster"] = "prod";
///     options.BaseTags["region"]  = "eu-west-1";
///     options.EnableClientStats   = true;
///     options.EnableLagMetrics    = true; // requires IKafkaLagProbe
/// });
/// ]]></code>
/// </example>
/// <seealso cref="KafkaModule"/>
/// <seealso cref="KafkaModuleOptions"/>
/// <seealso cref="IKafkaStatsSource"/>
/// <seealso cref="IKafkaLagProbe"/>
/// <seealso cref="IMetricFactory"/>
public static class NetMetricKafkaServiceCollectionExtensions
{
    /// <summary>
    /// Registers NetMetric Kafka services and the <see cref="KafkaModule"/> with the DI container.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configure">
    /// An optional delegate to configure <see cref="KafkaModuleOptions"/> during registration.
    /// If omitted, default options are used.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance to support fluent registration.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method performs the following steps:
    /// </para>
    /// <list type="number">
    ///   <item><description>Registers <see cref="KafkaModuleOptions"/> with the options system.</description></item>
    ///   <item><description>Applies the optional <paramref name="configure"/> action.</description></item>
    ///   <item><description>Registers a singleton <see cref="IModule"/> that composes:
    ///     <list type="bullet">
    ///       <item><description>All available <see cref="IKafkaStatsSource"/> instances (or none if not registered).</description></item>
    ///       <item><description>The required <see cref="IMetricFactory"/> for emitting metrics.</description></item>
    ///       <item><description>The configured <see cref="KafkaModuleOptions"/> via <see cref="IOptions{TOptions}"/>.</description></item>
    ///       <item><description>An optional <see cref="IKafkaLagProbe"/> for lag metrics (if registered).</description></item>
    ///     </list>
    ///   </description></item>
    /// </list>
    /// <para>
    /// If no <see cref="IKafkaStatsSource"/> instances are registered, the module will be created but will not emit
    /// client statistics until sources are added. Lag metrics are enabled only when an <see cref="IKafkaLagProbe"/> is present
    /// and the corresponding option is enabled.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddNetMetricKafka(
        this IServiceCollection services,
        Action<KafkaModuleOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register Kafka module options
        services.AddOptions<KafkaModuleOptions>();

        // Apply optional configuration if provided
        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Register the Kafka module with DI, including its dependencies
        services.TryAddSingleton<IModule>(sp =>
        {
            var sources = sp.GetService<IEnumerable<IKafkaStatsSource>>() ?? Array.Empty<IKafkaStatsSource>();
            var factory = sp.GetRequiredService<IMetricFactory>();
            var opts = sp.GetRequiredService<IOptions<KafkaModuleOptions>>().Value;
            var lagProbe = sp.GetService<IKafkaLagProbe>();

            return new KafkaModule(sources, factory, opts, lagProbe);
        });

        return services;
    }
}
