// <copyright file="NetMetricGrpcServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;

namespace NetMetric.Grpc.Extensions;

/// <summary>
/// Provides extension methods for registering NetMetric gRPC server metrics
/// into the dependency injection (DI) container.
/// </summary>
/// <remarks>
/// This extension wires up the <see cref="GrpcServerMetricSet"/> and
/// <see cref="NetMetricServerInterceptor"/> so that gRPC server calls can be
/// automatically instrumented with metrics such as:
/// <list type="bullet">
/// <item><description>Call duration histograms</description></item>
/// <item><description>Call counters</description></item>
/// <item><description>Message size histograms</description></item>
/// <item><description>Message counters</description></item>
/// <item><description>Error counters</description></item>
/// </list>
/// </remarks>
public static class NetMetricGrpcServiceCollectionExtensions
{
    /// <summary>
    /// Adds NetMetric gRPC server metrics integration by registering the metric set
    /// and interceptor into the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register services with.</param>
    /// <param name="configure">
    /// An optional delegate to configure <see cref="NetMetricGrpcServerOptions"/>
    /// such as histogram bucket boundaries for latency and message size.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <example>
    /// Example usage in a gRPC server project:
    /// <code language="csharp">
    /// var builder = WebApplication.CreateBuilder(args);
    ///
    /// // Register NetMetric gRPC metrics
    /// builder.Services.AddNetMetricCore(); // registers core NetMetric services
    /// builder.Services.AddNetMetricGrpcServer(options =>
    /// {
    ///     options.LatencyBucketsMs = new[] { 1d, 5d, 10d, 50d, 100d };
    ///     options.SizeBuckets = new[] { 64d, 256d, 1024d, 4096d };
    /// });
    ///
    /// var app = builder.Build();
    /// app.MapGrpcService&lt;UserService&gt;()
    ///    .Intercept&lt;NetMetricServerInterceptor&gt;();
    ///
    /// app.Run();
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// Registers the following services:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="GrpcServerMetricSet"/> as a singleton</description></item>
    /// <item><description><see cref="NetMetricServerInterceptor"/> as a singleton</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddNetMetricGrpcServer(
        this IServiceCollection services,
        Action<NetMetricGrpcServerOptions>? configure = null)
    {
        var opts = new NetMetricGrpcServerOptions();
        configure?.Invoke(opts);

        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IMetricFactory>();
            return new GrpcServerMetricSet(
                factory,
                opts.LatencyBucketsMs.ToArray(),
                opts.SizeBuckets.ToArray());
        });

        services.AddSingleton<NetMetricServerInterceptor>();
        return services;
    }
}
