// <copyright file="ServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetMetric.Abstractions;
using NetMetric.Redis.Abstractions;
using NetMetric.Redis.Client;
using NetMetric.Redis.Modules;
using NetMetric.Redis.Options;

namespace NetMetric.Redis.DependencyInjection;

/// <summary>
/// Extension methods for registering Redis services in the <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// These extensions integrate Redis support into a .NET application by wiring up:
/// </para>
/// <list type="bullet">
///   <item><description>Configuration binding and validation for <see cref="RedisOptions"/>.</description></item>
///   <item><description>A production-ready<see cref = "IRedisClient" /> implementation(StackExchange.Redis).</description></item>
///   <item><description>The <see cref="IModule"/> implementation that publishes Redis metrics (<see cref="RedisModule"/>).</description></item>
/// </list>
/// <para>
/// The registration pattern follows standard dependency injection (DI) practices and is safe to call multiple times; services
/// are added using <c>TryAdd*</c> semantics where applicable.
/// </para>
/// <para>
/// <strong>Thread Safety:</strong> The registrations themselves are thread-safe when invoked during application startup.
/// The created singletons (<see cref="IRedisClient"/> and <see cref="RedisModule"/>) are intended to be shared across the application.
/// </para>
/// <para>
/// <strong>Environment Requirements:</strong> A reachable Redis server must be available at the configured endpoint. If
/// the connection cannot be established at startup, the client registration may throw at build time due to the synchronous
/// wait on the connection routine.
/// </para>
/// <example>
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using NetMetric.Redis.Options;
///
/// var services = new ServiceCollection()
///     .AddLogging()
///     .AddNetMetricRedis(opts =>
///     {
///         opts.ConnectionString = "redis:6379";
///         opts.ConnectTimeoutMs = 5_000;
///         opts.CommandTimeoutMs = 3_000;
///         opts.AllowAdmin = false; // set true only if you need admin commands
///     });
///
/// var provider = services.BuildServiceProvider();
///
/// // Resolve the Redis metrics module (typically discovered by your monitoring host)
/// var modules = provider.GetServices<IModule>();
/// ]]></code>
/// </example>
/// <seealso cref="RedisOptions"/>
/// <seealso cref="IRedisClient"/>
/// <seealso cref="RedisModule"/>
/// </remarks>
public static class NetMetricRedisServiceCollectionExtensions
{
    /// <summary>
    /// Adds Redis-related services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to. Must not be <see langword="null"/>.</param>
    /// <param name="configure">
    /// An optional delegate to configure <see cref="RedisOptions"/>. If provided, it will be applied via
    /// <see cref="OptionsServiceCollectionExtensions.PostConfigure{TOptions}(IServiceCollection, Action{TOptions})"/>.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that additional calls can be chained.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method registers:
    /// </para>
    /// <list type="number">
    ///   <item><description><see cref="RedisOptions"/> in the Options framework (with optional <paramref name="configure"/> callback).</description></item>
    ///   <item><description><see cref="IValidateOptions{TOptions}"/> for <see cref="RedisOptions"/> via <see cref="RedisOptionsValidation"/>.</description></item>
    ///   <item><description>
    ///   A singleton <see cref="IRedisClient"/> that connects using <see cref="StackExchangeRedisClient.ConnectAsync(string, int, int, bool)"/>.
    ///   The connection is established synchronously during registration to ensure early failure if Redis is unreachable.
    ///   </description></item>
    ///   <item><description>
    ///   A singleton <see cref="IModule"/> implementation (<see cref="RedisModule"/>) that collects and exposes Redis metrics.
    ///   </description></item>
    /// </list>
    /// <para>
    /// <strong>Blocking Behavior:</strong> The call to <see cref="StackExchangeRedisClient.ConnectAsync(string, int, int, bool)"/>
    /// is awaited synchronously using <see cref="System.Runtime.CompilerServices.TaskAwaiter.GetResult"/>. If the Redis server
    /// is not available or the connection string is invalid, an exception will be thrown during service registration.
    /// </para>
    /// <para>
    /// <strong>Configuration Validation:</strong> If <see cref="RedisOptions"/> are invalid, an options validation exception
    /// will be raised when the options are accessed. Ensure required fields such as
    /// <see cref="RedisOptions.ConnectionString"/> are correctly set.
    /// </para>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// builder.Services.AddNetMetricRedis(options =>
    /// {
    ///     options.ConnectionString = "localhost:6379";
    ///     options.AllowAdmin = true; // enables commands that require admin privileges
    ///     options.ConnectTimeoutMs = 3_000;
    ///     options.CommandTimeoutMs = 5_000;
    /// });
    ///
    /// // Later, you can resolve IRedisClient or rely on RedisModule being discovered by your metrics host.
    /// ]]></code>
    /// </example>
    /// </remarks>
    /// <exception cref="OptionsValidationException">
    /// Thrown when <see cref="RedisOptions"/> fail validation (e.g., missing or malformed configuration) at the time they are accessed.
    /// </exception>
    /// <exception cref="System.Exception">
    /// Propagates any exception thrown by <see cref="StackExchangeRedisClient.ConnectAsync(string, int, int, bool)"/> if the connection cannot be established.
    /// This may include connectivity errors, authentication failures, or timeouts.
    /// </exception>
    public static IServiceCollection AddNetMetricRedis(this IServiceCollection services, Action<RedisOptions>? configure = null)
    {
        // Add Redis options to the DI container and apply any user-supplied configuration
        services.AddOptions<RedisOptions>();

        if (configure is not null)
        {
            services.PostConfigure(configure);
        }

        // Add validation for Redis options
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RedisOptions>, RedisOptionsValidation>());

        // Add Redis client (StackExchangeRedisClient)
        services.TryAddSingleton<IRedisClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<RedisOptions>>().Value;

            return StackExchangeRedisClient.ConnectAsync(
                o.ConnectionString,
                o.ConnectTimeoutMs,
                o.CommandTimeoutMs,
                allowAdmin: o.AllowAdmin).GetAwaiter().GetResult();
        });

        // Add the Redis metrics module to the DI container
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModule, RedisModule>());

        return services;
    }
}
