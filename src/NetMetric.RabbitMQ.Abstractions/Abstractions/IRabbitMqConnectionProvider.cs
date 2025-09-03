// <copyright file="IRabbitMqConnectionProvider.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace NetMetric.RabbitMQ.Abstractions;

/// <summary>
/// Provides a high-level abstraction for creating and managing RabbitMQ connections and channels.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are responsible for the lifecycle of <see cref="IConnection"/> and <see cref="IChannel"/> instances,
/// including creation, reuse (pooling or caching), and coordinated disposal. This interface is intended to be
/// registered in a dependency injection (DI) container and shared across components that publish or consume messages.
/// </para>
/// <para>
/// The provider MUST be compatible with <c>RabbitMQ.Client</c> version 7.x or higher.
/// Consumers of this interface should not assume a particular connection or channel policy (e.g., single shared connection,
/// per-operation channel). Instead, rely on the contracts below and dispose channels when they are no longer needed.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> Implementations should be safe for concurrent use. Methods may be called from multiple
/// threads simultaneously. Returned <see cref="IChannel"/> instances are not guaranteed to be thread-safe and should be
/// confined to the calling logical operation unless the implementation explicitly documents otherwise.
/// </para>
/// <para>
/// <strong>Disposal:</strong> This provider implements <see cref="IAsyncDisposable"/>. Disposing the provider should
/// gracefully close any created connections/channels. Callers must dispose channels they create via
/// <see cref="CreateChannelAsync(CreateChannelOptions?, System.Threading.CancellationToken)"/> to avoid resource leaks.
/// </para>
/// </remarks>
/// <example>
/// The following example shows how to obtain a connection and publish a message using a short-lived channel:
/// <code language="csharp"><![CDATA[
/// using System.Text;
/// using System.Threading;
/// using System.Threading.Tasks;
/// using NetMetric.RabbitMQ.Abstractions;
/// using RabbitMQ.Client;
///
/// public sealed class OrderPublisher
/// {
///     private readonly IRabbitMqConnectionProvider _provider;
///
///     public OrderPublisher(IRabbitMqConnectionProvider provider) => _provider = provider;
///
///     public async Task PublishAsync(string routingKey, byte[] body, CancellationToken ct = default)
///     {
///         // Lazily creates or reuses an underlying connection.
///         IConnection connection = await _provider.GetOrCreateConnectionAsync(ct);
///
///         // Create a short-lived channel for this operation and dispose it when done.
///         await using IChannel channel = await _provider.CreateChannelAsync(ct: ct);
///
///         var props = new BasicProperties
///         {
///             DeliveryMode = DeliveryModes.Persistent,
///             ContentType = "application/octet-stream"
///         };
///
///         // Declare exchange/queue as needed (idempotent in RabbitMQ).
///         await channel.ExchangeDeclareAsync("orders", ExchangeType.Direct, durable: true, autoDelete: false, arguments: null, ct);
///
///         // Publish the message.
///         await channel.BasicPublishAsync(
///             exchange: "orders",
///             routingKey: routingKey,
///             mandatory: false,
///             basicProperties: props,
///             body: body,
///             cancellationToken: ct);
///     }
/// }
/// ]]></code>
/// </example>
/// <seealso cref="CreateChannelOptions"/>
public interface IRabbitMqConnectionProvider : IAsyncDisposable
{
    /// <summary>
    /// Gets an existing open connection or creates a new one if none is available.
    /// </summary>
    /// <param name="ct">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task that completes with an open <see cref="IConnection"/> suitable for creating channels and performing AMQP operations.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Implementations may return a cached connection or create a new one on demand. If the underlying connection has entered
    /// a failed state, implementations should attempt recovery or re-creation according to their policy.
    /// </para>
    /// <para>
    /// The returned connection is owned by the provider; callers must not dispose it directly. Dispose the provider to close
    /// the connection or rely on the host application's shutdown pipeline.
    /// </para>
    /// </remarks>
    /// <exception cref="BrokerUnreachableException">
    /// Thrown when a connection cannot be established to any configured broker endpoint.
    /// </exception>
    /// <exception cref="AuthenticationFailureException">
    /// Thrown when authentication fails due to invalid credentials or permissions.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown if the operation is canceled via <paramref name="ct"/>.
    /// </exception>
    Task<IConnection> GetOrCreateConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new AMQP channel over an existing connection.
    /// </summary>
    /// <param name="options">Optional channel creation options to influence QoS, confirms, or other channel-level features.</param>
    /// <param name="ct">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task that completes with an <see cref="IChannel"/> instance. Callers are responsible for disposing the channel when finished.
    /// </returns>
    /// <remarks>
    /// <para>
    /// If no connection exists, the provider will establish one by calling
    /// <see cref="GetOrCreateConnectionAsync(System.Threading.CancellationToken)"/> internally. Implementations may apply defaults
    /// (e.g., publisher confirms, prefetch values) based on <paramref name="options"/> or global configuration.
    /// </para>
    /// <para>
    /// Channels are lightweight and intended to be short-lived; prefer creating them per logical unit of work rather than sharing them
    /// across threads. If your workload requires channel pooling, consult the provider's documentation for pooling semantics.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationInterruptedException">
    /// Thrown when the broker closes the channel during creation or negotiation.
    /// </exception>
    /// <exception cref="AlreadyClosedException">
    /// Thrown when the underlying connection is closed and cannot create a channel.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown if the operation is canceled via <paramref name="ct"/>.
    /// </exception>
    Task<IChannel> CreateChannelAsync(CreateChannelOptions? options = null, CancellationToken ct = default);
}
