// <copyright file="RedisOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Redis.Options;

/// <summary>
/// Represents configuration options for connecting to Redis and collecting Redis-related metrics.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="RedisOptions"/> class provides a strongly-typed configuration object that defines
/// how the Redis module should connect to Redis servers and which metrics to collect. 
/// </para>
/// <para>
/// These options can be bound from configuration sources (e.g., <c>appsettings.json</c>) using 
/// .NET configuration binding.
/// </para>
/// <para>Example:</para>
/// <code language="json">
/// {
///   "Redis": {
///     "ConnectionString": "localhost:6379",
///     "ConnectTimeoutMs": 2000,
///     "CommandTimeoutMs": 1500,
///     "AllowAdmin": true,
///     "EnableLatency": true,
///     "EnableSlowlog": false,
///     "ServiceName": "cart-service"
///   }
/// }
/// </code>
/// <para>Usage in C#:</para>
/// <code language="csharp">
/// var options = new RedisOptions
/// {
///     ConnectionString = "redis.mycompany.local:6379",
///     ConnectTimeoutMs = 2000,
///     CommandTimeoutMs = 1500,
///     AllowAdmin = false,
///     EnableLatency = true,
///     EnableSlowlog = true,
///     ServiceName = "checkout-service"
/// };
/// </code>
/// </remarks>
public sealed class RedisOptions
{
    /// <summary>
    /// Gets or sets the Redis connection string, specifying the Redis server address and port. 
    /// Default is <c>"localhost:6379"</c>.
    /// </summary>
    /// <remarks>
    /// This value can include authentication and additional Redis connection parameters. 
    /// For clustered Redis deployments, multiple endpoints may be specified.
    /// </remarks>
    public string ConnectionString { get; init; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the connection timeout in milliseconds.
    /// Default is <c>1000</c> ms.
    /// </summary>
    /// <remarks>
    /// This setting determines how long the client will wait for a Redis connection to be established 
    /// before throwing a timeout exception.
    /// </remarks>
    public int ConnectTimeoutMs { get; init; } = 1000;

    /// <summary>
    /// Gets or sets the command timeout in milliseconds.
    /// Default is <c>1000</c> ms.
    /// </summary>
    /// <remarks>
    /// This value defines the maximum time the client will wait for a Redis command to complete.
    /// </remarks>
    public int CommandTimeoutMs { get; init; } = 1000;

    /// <summary>
    /// Gets or sets a flag indicating whether administrative commands are allowed.
    /// Default is <c>true</c>.
    /// </summary>
    /// <remarks>
    /// Administrative commands include operations such as <c>INFO</c>, <c>CONFIG</c>, 
    /// and <c>SLOWLOG</c>. 
    /// If disabled, collectors that rely on administrative commands may not function properly.
    /// </remarks>
    public bool AllowAdmin { get; init; } = true;

    /// <summary>
    /// Gets or sets a flag indicating whether to enable latency metrics collection.
    /// Default is <c>true</c>.
    /// </summary>
    /// <remarks>
    /// When enabled, latency statistics are gathered using the Redis <c>LATENCY LATEST</c> command.
    /// </remarks>
    public bool EnableLatency { get; init; } = true;

    /// <summary>
    /// Gets or sets a flag indicating whether to enable slowlog metrics collection.
    /// Default is <c>false</c>.
    /// </summary>
    /// <remarks>
    /// When enabled, Redis slowlog entries are collected using the <c>SLOWLOG GET</c> command.
    /// </remarks>
    public bool EnableSlowlog { get; init; }

    /// <summary>
    /// Gets or sets the logical service name associated with the Redis instance.
    /// </summary>
    /// <remarks>
    /// This value is typically used for tagging metrics to differentiate multiple services 
    /// that use Redis within the same monitoring system.
    /// </remarks>
    public string? ServiceName { get; init; }
}
