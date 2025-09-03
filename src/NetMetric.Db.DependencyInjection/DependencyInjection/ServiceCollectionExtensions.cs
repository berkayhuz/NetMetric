// <copyright file="ServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMetric.Db.Abstractions;
using NetMetric.Db.Modules;

namespace NetMetric.Db.DependencyInjection;

/// <summary>
/// Provides extension methods for registering NetMetric database instrumentation
/// and metrics module into an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// These extensions integrate NetMetric database metrics into an application’s dependency
/// injection container, making them available throughout the application lifecycle.
/// </para>
/// <para><strong>Registered services:</strong></para>
/// <list type="bullet">
///   <item><description><see cref="IDbInstrumentation"/> — the database instrumentation adapter.</description></item>
///   <item><description><see cref="DbMetricsModule"/> — the module that manages instruments and collectors.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using NetMetric.Db.DependencyInjection;
///
/// var services = new ServiceCollection();
///
/// // Add default NetMetric database services
/// services.AddNetMetricDb();
///
/// // Optionally configure metrics behavior
/// services.AddNetMetricDb(opts =>
/// {
///     opts.DefaultTags = new Dictionary<string, string>
///     {
///         ["service.name"] = "orders-api",
///         ["db.system"]   = "postgres"
///     };
///     opts.PoolSamplePeriodMs = 5000; // sample every 5s
/// });
///
/// var provider = services.BuildServiceProvider();
///
/// // Resolve and use DbMetricsModule
/// var module = provider.GetRequiredService<DbMetricsModule>();
///
/// using (module.StartQuery())
/// {
///     // Execute database command...
/// }
/// ]]></code>
/// </example>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds NetMetric database instrumentation and metrics collection services
    /// to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">
    /// An optional delegate to configure <see cref="DbMetricsOptions"/> after the defaults are applied.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that multiple calls can be chained.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method registers the following components as singletons:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see cref="IDbInstrumentation"/> — enables recording of connection and query activity.</description></item>
    ///   <item><description><see cref="DbMetricsModule"/> — manages active connections, pool metrics, query durations, and errors.</description></item>
    /// </list>
    /// <para>
    /// Consumers can supply an optional <paramref name="configure"/> action to override default
    /// settings such as <see cref="DbMetricsOptions.DefaultTags"/> or <see cref="DbMetricsOptions.PoolSamplePeriodMs"/>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddNetMetricDb(
        this IServiceCollection services,
        Action<DbMetricsOptions>? configure = null)
    {
        services.AddOptions<DbMetricsOptions>();
        if (configure is not null) services.PostConfigure(configure);

        services.TryAddSingleton<IDbInstrumentation, DbInstrumentation>();
        services.TryAddSingleton<DbMetricsModule>();
        return services;
    }
}
