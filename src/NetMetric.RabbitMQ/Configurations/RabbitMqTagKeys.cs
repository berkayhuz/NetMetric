// <copyright file="RabbitMqTagKeys.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.RabbitMQ.Configurations;

/// <summary>
/// Provides a collection of constant tag keys used for labeling RabbitMQ-related metrics.
/// </summary>
/// <remarks>
/// <para>
/// These tag keys standardize the way metadata is attached to RabbitMQ metrics,
/// enabling consistent observability across systems. They are typically applied
/// to metrics such as connection health, channel count, queue depth, publish latency,
/// and consumer rates.
/// </para>
/// <para>
/// Example usage:
/// <code language="csharp">
/// var tags = new TagList
/// {
///     { RabbitMqTagKeys.VHost, "my-vhost" },
///     { RabbitMqTagKeys.Queue, "order-queue" },
///     { RabbitMqTagKeys.Host, "rabbit1.mycompany.local" }
/// };
/// 
/// _metrics.Gauge("rabbitmq.queue.depth", currentDepth, tags);
/// </code>
/// </para>
/// </remarks>
public static class RabbitMqTagKeys
{
    /// <summary>
    /// The tag key that identifies a RabbitMQ virtual host (vhost).
    /// </summary>
    /// <remarks>
    /// Example: <c>"rabbitmq.vhost"</c> with value <c>"production"</c>.
    /// </remarks>
    public const string VHost = "rabbitmq.vhost";

    /// <summary>
    /// The tag key that identifies the RabbitMQ host.
    /// </summary>
    /// <remarks>
    /// Example: <c>"rabbitmq.host"</c> with value <c>"rabbit1.company.net"</c>.
    /// </remarks>
    public const string Host = "rabbitmq.host";

    /// <summary>
    /// The tag key that identifies a RabbitMQ queue.
    /// </summary>
    /// <remarks>
    /// Example: <c>"rabbitmq.queue"</c> with value <c>"orders-queue"</c>.
    /// </remarks>
    public const string Queue = "rabbitmq.queue";

    /// <summary>
    /// The tag key that identifies a RabbitMQ exchange.
    /// </summary>
    /// <remarks>
    /// Example: <c>"rabbitmq.exchange"</c> with value <c>"exchange.direct"</c>.
    /// </remarks>
    public const string Exchange = "rabbitmq.exchange";

    /// <summary>
    /// The tag key that identifies a RabbitMQ routing key.
    /// </summary>
    /// <remarks>
    /// Example: <c>"rabbitmq.routing_key"</c> with value <c>"order.created"</c>.
    /// </remarks>
    public const string RouteKey = "rabbitmq.routing_key";

    /// <summary>
    /// The tag key that identifies the RabbitMQ connection name.
    /// </summary>
    /// <remarks>
    /// Example: <c>"rabbitmq.connection"</c> with value <c>"consumer-connection-1"</c>.
    /// </remarks>
    public const string ConnName = "rabbitmq.connection";
}
