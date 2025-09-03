// <copyright file="RabbitMqModuleOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Net.Security;

namespace NetMetric.RabbitMQ.Configurations;

/// <summary>
/// Configures how the NetMetric RabbitMQ module connects to RabbitMQ and which collectors it enables.
/// </summary>
/// <remarks>
/// <para>
/// Use this options class to supply connection parameters (via a connection string, <see cref="Uri"/>, or discrete host
/// fields), configure resilience (heartbeats and automatic recovery), optionally enable TLS/SSL, and toggle metric
/// collectors on or off. All properties are <c>init</c>-only and are therefore safe for binding from configuration files
/// (e.g., <c>appsettings.json</c>) or environment variables.
/// </para>
/// <para>
/// <b>Supplying connection information</b><br/>
/// Provide <em>one</em> of the following:
/// <list type="bullet">
///   <item><description><see cref="ConnectionString"/> (e.g., <c>amqp://user:pass@host:5672/vhost</c> or <c>amqps://…</c>)</description></item>
///   <item><description><see cref="Uri"/> (AMQP/AMQPS)</description></item>
///   <item><description>Discrete fields: <see cref="HostName"/>, <see cref="Port"/>, <see cref="UserName"/>,
///   <see cref="Password"/>, <see cref="VirtualHost"/></description></item>
/// </list>
/// If multiple sources are set, the consuming code decides precedence. As a best practice, prefer a single, canonical
/// source (usually a connection string or <see cref="Uri"/>).
/// </para>
/// <para>
/// <b>TLS/SSL</b><br/>
/// Set <see cref="UseSsl"/> to <see langword="true"/> to negotiate TLS. Optionally set <see cref="SslServerName"/> to
/// control SNI/hostname validation. For development-only scenarios you may relax validation via
/// <see cref="SslAcceptAnyServerCert"/> or specific <see cref="SslAcceptablePolicyErrors"/> flags. Avoid using these
/// relaxations in production.
/// </para>
/// <para>
/// <b>Collectors</b><br/>
/// The flags under the “Collector flags” region control which built-in collectors are enabled:
/// <list type="bullet">
///   <item><description><see cref="EnableConnectionHealth"/> — reports connection up/down state.</description></item>
///   <item><description><see cref="EnableChannelCount"/> — reports channel counts and negotiated limits.</description></item>
///   <item><description><see cref="EnableQueueDepth"/> — reports message depth for queues in <see cref="QueueNames"/>.</description></item>
///   <item><description><see cref="EnablePublishConfirmLatency"/> — measures publisher confirm latency.</description></item>
///   <item><description><see cref="EnableConsumerRate"/> — estimates consumption rate for queues in <see cref="QueueNames"/>.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Thread-safety</b><br/>
/// This type contains only immutable (<c>init</c>-only) properties and is safe to share across threads after construction.
/// </para>
/// <example>
/// <para>
/// <b>Binding from configuration (ASP.NET Core)</b>
/// </para>
/// <code language="csharp"><![CDATA[
/// Program.cs
/// builder.Services.Configure<RabbitMqModuleOptions>(
///     builder.Configuration.GetSection("NetMetric:RabbitMQ"));
/// ]]></code>
/// <para>
/// <b>JSON configuration</b>
/// </para>
/// <code language="json"><![CDATA[
/// {
///   "NetMetric": {
///     "RabbitMQ": {
///       "ConnectionString": "amqps://appuser:secret@rabbit01:5671/prod",
///       "ClientProvidedName": "orders-api",
///       "RequestedHeartbeat": "00:00:30",
///       "NetworkRecoveryInterval": "00:00:10",
///       "AutomaticRecoveryEnabled": true,
///       "TopologyRecoveryEnabled": true,
///       "UseSsl": true,
///       "SslServerName": "rabbit01.internal",
///       "EnableQueueDepth": true,
///       "EnableConsumerRate": true,
///       "QueueNames": [ "orders", "payments" ],
///       "ConfirmLatencyBoundsMs": [1, 5, 10, 50, 100, 250, 500, 1000]
///     }
///   }
/// }
/// ]]></code>
/// <para>
/// <b>Manual construction (tests or console apps)</b>
/// </para>
/// <code language="csharp"><![CDATA[
/// var options = new RabbitMqModuleOptions
/// {
///     Uri = new Uri("amqp://guest:guest@localhost:5672/"),
///     ClientProvidedName = "NetMetric.RabbitMQ",
///     EnableConnectionHealth = true,
///     EnableChannelCount = true,
///     EnableQueueDepth = false, // toggle as needed
///     EnablePublishConfirmLatency = true,
///     EnableConsumerRate = false
/// };
/// ]]></code>
/// </example>
/// <seealso cref="SslPolicyErrors"/>
/// <seealso cref="NetMetric.RabbitMQ.Collectors.ConnectionHealthCollector"/>
/// <seealso cref="NetMetric.RabbitMQ.Collectors.ChannelCountCollector"/>
/// <seealso cref="NetMetric.RabbitMQ.Collectors.QueueDepthCollector"/>
/// <seealso cref="NetMetric.RabbitMQ.Collectors.PublishConfirmLatencyCollector"/>
/// <seealso cref="NetMetric.RabbitMQ.Collectors.ConsumerRateCollector"/>
/// </remarks>
public sealed class RabbitMqModuleOptions
{
    // Connection information

    /// <summary>
    /// Gets the AMQP/AMQPS connection string used to establish the RabbitMQ connection
    /// (e.g., <c>amqp://user:pass@host:5672/vhost</c> or <c>amqps://…</c>).
    /// </summary>
    /// <value>Defaults to <see langword="null"/>; if provided, typically overrides discrete host fields.</value>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Gets the AMQP/AMQPS <see cref="System.Uri"/> used to establish the RabbitMQ connection.
    /// </summary>
    /// <value>Defaults to <see langword="null"/>.</value>
    public Uri? Uri { get; init; }

    /// <summary>
    /// Gets the RabbitMQ server host name when not using <see cref="ConnectionString"/> or <see cref="Uri"/>.
    /// </summary>
    /// <value>Defaults to <see langword="null"/>.</value>
    public string? HostName { get; init; }

    /// <summary>
    /// Gets the TCP port used for the RabbitMQ connection when supplying discrete host fields.
    /// </summary>
    /// <value>Defaults to <see langword="null"/> (use server default when omitted).</value>
    public int? Port { get; init; }

    /// <summary>
    /// Gets the username used to authenticate to RabbitMQ when supplying discrete host fields.
    /// </summary>
    /// <value>Defaults to <see langword="null"/>.</value>
    public string? UserName { get; init; }

    /// <summary>
    /// Gets the password used to authenticate to RabbitMQ when supplying discrete host fields.
    /// </summary>
    /// <value>Defaults to <see langword="null"/>.</value>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the virtual host name used for the RabbitMQ connection when supplying discrete host fields.
    /// </summary>
    /// <value>Defaults to <see langword="null"/> (server default is <c>/</c>).</value>
    public string? VirtualHost { get; init; }

    /// <summary>
    /// Gets the client-provided connection name as it will appear in RabbitMQ management and logs.
    /// </summary>
    /// <value>Defaults to <c>"NetMetric.RabbitMQ"</c>.</value>
    public string ClientProvidedName { get; init; } = "NetMetric.RabbitMQ";

    // Resilience / Network

    /// <summary>
    /// Gets the desired AMQP heartbeat interval used to detect dead peers.
    /// </summary>
    /// <remarks>
    /// Very small values increase sensitivity but may lead to false positives on congested networks.
    /// </remarks>
    /// <value>Defaults to 00:00:30.</value>
    public TimeSpan RequestedHeartbeat { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the interval between automatic network recovery attempts after a disconnection.
    /// </summary>
    /// <value>Defaults to 00:00:10.</value>
    public TimeSpan NetworkRecoveryInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets a value indicating whether the client should automatically recover connections and channels after failure.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool AutomaticRecoveryEnabled { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to attempt topology recovery (exchanges, queues, bindings, and consumers) during automatic recovery.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool TopologyRecoveryEnabled { get; init; } = true;

    // TLS/SSL (optional)

    /// <summary>
    /// Gets a value indicating whether to use TLS/SSL for the RabbitMQ connection.
    /// </summary>
    /// <value>Defaults to <see langword="false"/>.</value>
    public bool UseSsl { get; init; }

    /// <summary>
    /// Gets the expected TLS server name (SNI) used during certificate validation.
    /// </summary>
    /// <remarks>
    /// Set this when the broker certificate’s common name (CN) or subject alternative names (SAN) require a specific hostname.
    /// </remarks>
    /// <value>Defaults to <see langword="null"/> (use broker host name).</value>
    public string? SslServerName { get; init; }

    /// <summary>
    /// Gets a value indicating whether to accept any server certificate without validation.
    /// </summary>
    /// <remarks>
    /// <b>Warning:</b> For development and testing only. Disables certificate validation and weakens security.
    /// Prefer using <see cref="SslAcceptablePolicyErrors"/> to selectively relax checks.
    /// </remarks>
    /// <value>Defaults to <see langword="false"/>.</value>
    public bool SslAcceptAnyServerCert { get; init; }

    /// <summary>
    /// Gets the set of acceptable TLS certificate policy errors, if any, to relax validation.
    /// </summary>
    /// <value>Defaults to <see cref="SslPolicyErrors.None"/>.</value>
    public SslPolicyErrors SslAcceptablePolicyErrors { get; init; }

    // Connection retry

    /// <summary>
    /// Gets the number of connection retry attempts before giving up.
    /// </summary>
    /// <value>Defaults to 5.</value>
    public int ConnectRetryCount { get; init; } = 5;

    /// <summary>
    /// Gets the base delay used when computing backoff between retries.
    /// </summary>
    /// <value>Defaults to 00:00:00.500.</value>
    public TimeSpan ConnectRetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets the maximum delay allowed between retry attempts.
    /// </summary>
    /// <value>Defaults to 00:00:08.</value>
    public TimeSpan ConnectRetryMaxDelay { get; init; } = TimeSpan.FromSeconds(8);

    // Metric prefix

    /// <summary>
    /// Gets an optional prefix attached to all RabbitMQ metric names exposed by the module.
    /// </summary>
    /// <value>Defaults to <see langword="null"/> (no prefix).</value>
    public string? MetricPrefix { get; init; }

    // Collector flags

    /// <summary>
    /// Gets a value indicating whether to enable the connection health collector.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool EnableConnectionHealth { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to enable the channel count collector.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool EnableChannelCount { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to enable the queue depth collector for queues listed in <see cref="QueueNames"/>.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool EnableQueueDepth { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to enable the publisher confirmation latency collector.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool EnablePublishConfirmLatency { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to enable the consumer rate estimator for queues listed in <see cref="QueueNames"/>.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool EnableConsumerRate { get; init; } = true;

    // Collector settings

    /// <summary>
    /// Gets the list of queue names that collectors should observe (e.g., for queue depth and consumer rate).
    /// </summary>
    /// <value>Defaults to an empty list.</value>
    public IReadOnlyList<string> QueueNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the optional histogram bucket upper bounds (in milliseconds) for confirm latency metrics.
    /// </summary>
    /// <remarks>
    /// Provide a monotonically increasing sequence of positive values (e.g., <c>[1, 5, 10, 50, 100, 250, 500, 1000]</c>).
    /// If <see langword="null"/>, the collector falls back to its internal defaults.
    /// </remarks>
    /// <value>Defaults to <see langword="null"/>.</value>
    public IReadOnlyList<double>? ConfirmLatencyBoundsMs { get; init; }
}
