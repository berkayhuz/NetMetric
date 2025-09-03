// <copyright file="EfCoreServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NetMetric.Db.EntityFrameworkCore.DependencyInjection;

/// <summary>
/// Provides extension methods to integrate NetMetric EF Core interceptors
/// with <see cref="DbContextOptionsBuilder"/> and <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// These extensions wire NetMetric’s EF Core command/connection interceptors into your application,
/// enabling collection of query duration, error counts, and connection lifecycle metrics without
/// modifying repository code.
/// </para>
/// <para><strong>Thread Safety:</strong>
/// Interceptors themselves are stateless/safe for concurrent use; they emit metrics through
/// thread-safe instruments managed by the NetMetric modules.
/// </para>
/// </remarks>
/// <example>
/// <para><strong>Register interceptors and DbContext (recommended):</strong></para>
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using Microsoft.EntityFrameworkCore;
/// using NetMetric.Db.EntityFrameworkCore.DependencyInjection;
///
/// var services = new ServiceCollection();
///
/// // 1) Register NetMetric EF Core interceptors
/// services.AddNetMetricEfCore();
///
/// // 2) Register your DbContext and attach interceptors via the (sp, options) overload
/// services.AddDbContext<AppDbContext>((sp, options) =>
/// {
///     options.UseSqlServer("<connection-string>")
///            .UseNetMetric(sp); // resolves and attaches NetMetric interceptors
/// });
/// ]]></code>
/// </example>
public static class EfCoreServiceCollectionExtensions
{
    /// <summary>
    /// Configures EF Core to use NetMetric interceptors by resolving them from the
    /// provided <see cref="IServiceProvider"/> and adding them to the options builder.
    /// </summary>
    /// <param name="b">The <see cref="DbContextOptionsBuilder"/> being configured.</param>
    /// <param name="sp">The <see cref="IServiceProvider"/> used to resolve interceptors.</param>
    /// <returns>
    /// The same <see cref="DbContextOptionsBuilder"/> instance, allowing method chaining.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="b"/> or <paramref name="sp"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method adds both NetMetric EF Core interceptors:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see cref="NetMetricEfCoreCommandInterceptor"/></description></item>
    ///   <item><description><see cref="NetMetricEfCoreConnectionInterceptor"/></description></item>
    /// </list>
    /// <para>
    /// Use the <c>AddDbContext((sp, options) =&gt; ...)</c> overload so that you can pass the same
    /// <see cref="IServiceProvider"/> instance that contains the interceptor registrations.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// services.AddDbContext<AppDbContext>((sp, options) =>
    /// {
    ///     options.UseNpgsql("<connection-string>")
    ///            .UseNetMetric(sp); // attaches NetMetric interceptors
    /// });
    /// ]]></code>
    /// </example>
    public static DbContextOptionsBuilder UseNetMetric(
        this DbContextOptionsBuilder b, IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(sp);

        var cmd = sp.GetRequiredService<NetMetricEfCoreCommandInterceptor>();
        var con = sp.GetRequiredService<NetMetricEfCoreConnectionInterceptor>();

        return b.AddInterceptors(cmd, con);
    }

    /// <summary>
    /// Registers NetMetric EF Core interceptors in the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance, allowing method chaining.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method registers the following interceptors as singletons:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see cref="NetMetricEfCoreCommandInterceptor"/></description></item>
    ///   <item><description><see cref="NetMetricEfCoreConnectionInterceptor"/></description></item>
    /// </list>
    /// Register these before configuring your <c>DbContextOptionsBuilder</c> via <see cref="UseNetMetric(DbContextOptionsBuilder, IServiceProvider)"/>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var services = new ServiceCollection();
    /// services.AddNetMetricEfCore(); // registers interceptors
    ///
    /// services.AddDbContext<AppDbContext>((sp, options) =>
    ///     options.UseSqlite("Data Source=app.db")
    ///            .UseNetMetric(sp));
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricEfCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<NetMetricEfCoreCommandInterceptor>();
        services.AddSingleton<NetMetricEfCoreConnectionInterceptor>();

        return services;
    }
}
