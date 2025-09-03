// <copyright file="SimpleConnectionProvider.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Net.Security;
using Microsoft.Extensions.Options;
using NetMetric.RabbitMQ.Abstractions;
using NetMetric.RabbitMQ.Configurations;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace NetMetric.RabbitMQ.DependencyInjection;

/// <summary>
/// Provides a lightweight <see cref="IRabbitMqConnectionProvider"/> that manages a single
/// RabbitMQ connection instance with retry/backoff, optional TLS, and async disposal semantics.
/// </summary>
/// <remarks>
/// <para>
/// This provider lazily creates a connection on first use and reuses it for subsequent requests.
/// If the connection is closed or becomes invalid, a new one will be established on demand.
/// </para>
/// <para>
/// Connection attempts honor <see cref="RabbitMqModuleOptions.ConnectRetryCount"/> with exponential backoff starting at
/// <see cref="RabbitMqModuleOptions.ConnectRetryBaseDelay"/> and capped by <see cref="RabbitMqModuleOptions.ConnectRetryMaxDelay"/>.
/// </para>
/// <para>
/// All public members are thread-safe. A <see cref="System.Threading.SemaphoreSlim"/> protects the connection lifecycle.
/// </para>
/// <para>
/// This implementation targets RabbitMQ.Client 7.x and uses asynchronous connection and channel APIs.
/// </para>
/// <example>
/// The following example shows how to register and use <see cref="SimpleConnectionProvider"/> in an ASP.NET Core application:
/// <code language="csharp"><![CDATA[
/// services.Configure<RabbitMqModuleOptions>(cfg =>
/// {
///     cfg.ConnectionString = "amqps://user:pass@host/vhost";
///     cfg.AutomaticRecoveryEnabled = true;
///     cfg.TopologyRecoveryEnabled = true;
///     cfg.UseSsl = true;
///     cfg.SslServerName = "host";
///     cfg.ConnectRetryCount = 3;
///     cfg.ConnectRetryBaseDelay = TimeSpan.FromSeconds(1);
///     cfg.ConnectRetryMaxDelay = TimeSpan.FromSeconds(10);
/// });
///
/// services.AddSingleton<IRabbitMqConnectionProvider, SimpleConnectionProvider>();
///
/// // Later in your code (e.g., in a background service):
/// var provider = services.GetRequiredService<IRabbitMqConnectionProvider>();
/// using var channel = await (await provider.GetOrCreateConnectionAsync())
///     .CreateChannelAsync(new CreateChannelOptions { PublisherConfirmations = true });
/// ]]></code>
/// </example>
/// </remarks>
public sealed class SimpleConnectionProvider : IRabbitMqConnectionProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RabbitMqModuleOptions _opts;
    private IConnection? _conn;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleConnectionProvider"/> class.
    /// </summary>
    /// <param name="opts">Configuration options that control connection and retry behavior.</param>
    /// <remarks>
    /// If <paramref name="opts"/> is <see langword="null"/>, a new <see cref="RabbitMqModuleOptions"/> with default values is used.
    /// </remarks>
    public SimpleConnectionProvider(IOptions<RabbitMqModuleOptions> opts)
    {
        _opts = opts?.Value ?? new RabbitMqModuleOptions();
    }

    /// <summary>
    /// Gets an existing open connection or creates a new one if necessary.
    /// </summary>
    /// <param name="ct">A cancellation token to observe while waiting for the connection.</param>
    /// <returns>An open <see cref="IConnection"/> instance.</returns>
    /// <remarks>
    /// <para>
    /// If a previous connection exists but is closed, this method disposes it and attempts to
    /// create a new connection using the configured factory and retry policy.
    /// </para>
    /// <para>
    /// The returned connection is cached and shared across subsequent callers until it is closed
    /// (e.g., by broker shutdown), at which point it will be recreated on demand.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is canceled.</exception>
    /// <exception cref="InvalidOperationException">Thrown if all retry attempts fail to establish a connection.</exception>
    public async Task<IConnection> GetOrCreateConnectionAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_conn is { IsOpen: true })
            {
                return _conn;
            }

            if (_conn is not null)
            {
                try
                {
                    await _conn.DisposeAsync().ConfigureAwait(false);
                }
                catch (OperationInterruptedException)
                {
                    // Intentionally suppressed: disposing a broken connection may raise OperationInterruptedException.
                }

                _conn = null;
            }

            var factory = BuildFactory(_opts);

            Exception? last = null;
            var delay = _opts.ConnectRetryBaseDelay;

            // AttemptCount = ConnectRetryCount + 1 (first try + retries).
            for (var attempt = 1; attempt <= Math.Max(1, _opts.ConnectRetryCount + 1); attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var conn = await factory.CreateConnectionAsync(cancellationToken: ct).ConfigureAwait(false);

                    WireConnectionEvents(conn);

                    _conn = conn;

                    return _conn;
                }
                catch (BrokerUnreachableException ex) when (attempt <= _opts.ConnectRetryCount)
                {
                    last = ex;

                    // Exponential backoff with upper bound.
                    await Task.Delay(delay, ct).ConfigureAwait(false);

                    var next = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                    delay = next <= _opts.ConnectRetryMaxDelay ? next : _opts.ConnectRetryMaxDelay;
                }
            }

            throw last ?? new InvalidOperationException("Unable to connect to RabbitMQ.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Creates a new channel (<see cref="IChannel"/>) on the managed connection.
    /// </summary>
    /// <param name="options">Optional channel creation options (e.g., publisher confirmations).</param>
    /// <param name="ct">A cancellation token to observe while creating the channel.</param>
    /// <returns>A newly created <see cref="IChannel"/>.</returns>
    /// <remarks>
    /// Ensures a valid underlying connection by calling <see cref="GetOrCreateConnectionAsync(System.Threading.CancellationToken)"/> as needed.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is canceled.</exception>
    public async Task<IChannel> CreateChannelAsync(CreateChannelOptions? options = null, CancellationToken ct = default)
    {
        var conn = await GetOrCreateConnectionAsync(ct).ConfigureAwait(false);
        return await conn.CreateChannelAsync(options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously disposes the provider by closing and disposing the underlying connection, if any.
    /// </summary>
    /// <remarks>
    /// This method will attempt a graceful close of the connection and then dispose it.
    /// Any <see cref="OperationInterruptedException"/> thrown during close or dispose is suppressed.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_conn is null)
            {
                return;
            }

            try
            {
                await _conn.CloseAsync().ConfigureAwait(false);
            }
            catch (OperationInterruptedException)
            {
                // Suppressed: closing an already-faulted/broken connection may interrupt.
            }

            try
            {
                await _conn.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationInterruptedException)
            {
                // Suppressed: disposing an already-faulted/broken connection may interrupt.
            }

            _conn = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    /// <summary>
    /// Builds a configured <see cref="ConnectionFactory"/> from <see cref="RabbitMqModuleOptions"/>.
    /// </summary>
    /// <param name="o">The RabbitMQ module options to apply.</param>
    /// <returns>A configured <see cref="ConnectionFactory"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="o"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Prefers <see cref="RabbitMqModuleOptions.Uri"/> or <see cref="RabbitMqModuleOptions.ConnectionString"/> when provided;
    /// otherwise falls back to discrete host/port/credentials/virtual host settings.
    /// </para>
    /// <para>
    /// If <see cref="RabbitMqModuleOptions.UseSsl"/> is <see langword="true"/>,
    /// TLS is enabled with the specified server name and acceptable policy errors.
    /// </para>
    /// </remarks>
    private static ConnectionFactory BuildFactory(RabbitMqModuleOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);

        var f = new ConnectionFactory
        {
            AutomaticRecoveryEnabled = o.AutomaticRecoveryEnabled,
            TopologyRecoveryEnabled = o.TopologyRecoveryEnabled,
            RequestedHeartbeat = o.RequestedHeartbeat,
            NetworkRecoveryInterval = o.NetworkRecoveryInterval,
            ClientProvidedName = o.ClientProvidedName,
        };

        var uri = o.Uri?.AbsoluteUri ?? (!string.IsNullOrWhiteSpace(o.ConnectionString) ? o.ConnectionString : null);

        if (!string.IsNullOrWhiteSpace(uri))
        {
            f.Uri = new Uri(uri);
        }
        else
        {
            f.HostName = o.HostName ?? "localhost";

            if (o.Port is int p)
            {
                f.Port = p;
            }
            if (!string.IsNullOrWhiteSpace(o.UserName))
            {
                f.UserName = o.UserName!;
            }
            if (!string.IsNullOrWhiteSpace(o.Password))
            {
                f.Password = o.Password!;
            }
            if (!string.IsNullOrWhiteSpace(o.VirtualHost))
            {
                f.VirtualHost = o.VirtualHost!;
            }
        }

        if (o.UseSsl)
        {
            f.Ssl = new SslOption
            {
                Enabled = true,
                ServerName = o.SslServerName ?? f.HostName,
                AcceptablePolicyErrors = o.SslAcceptAnyServerCert
                    ? SslPolicyErrors.RemoteCertificateNameMismatch
                      | SslPolicyErrors.RemoteCertificateChainErrors
                      | SslPolicyErrors.RemoteCertificateNotAvailable
                    : o.SslAcceptablePolicyErrors
            };
        }

        return f;
    }

    /// <summary>
    /// Subscribes to key connection-level events for diagnostics and lifecycle management.
    /// </summary>
    /// <param name="conn">The connection to wire up.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="conn"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><see cref="IConnection.ConnectionShutdownAsync"/> clears the cached connection.</description></item>
    /// <item><description><see cref="IConnection.CallbackExceptionAsync"/> and <see cref="IConnection.ConnectionBlockedAsync"/> are observed and ignored here; consider logging in a higher layer.</description></item>
    /// <item><description><see cref="IConnection.ConnectionUnblockedAsync"/> is observed and ignored; the provider will reconnect if the broker shuts the connection down.</description></item>
    /// </list>
    /// </remarks>
    private void WireConnectionEvents(IConnection conn)
    {
        ArgumentNullException.ThrowIfNull(conn);

        conn.ConnectionShutdownAsync += async (_, args) =>
        {
            // When the broker shuts the connection down, clear the cache
            // so the next call re-establishes a fresh connection.
            await Task.Yield();
            _conn = null;
        };
        conn.CallbackExceptionAsync += async (_, args) =>
        {
            // Intentionally no-op; hook here if you need to log.
            await Task.CompletedTask.ConfigureAwait(false);
        };
        conn.ConnectionBlockedAsync += async (_, args) =>
        {
            // Intentionally no-op; hook here if you need to log.
            await Task.CompletedTask.ConfigureAwait(false);
        };
        conn.ConnectionUnblockedAsync += async (_, __) =>
        {
            // Intentionally no-op; hook here if you need to log.
            await Task.CompletedTask.ConfigureAwait(false);
        };
    }
}
