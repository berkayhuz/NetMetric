// <copyright file="IAzureServiceBusAdmin.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Azure.Abstractions;

/// <summary>
/// Defines an abstraction for lightweight administrative operations against Azure Service Bus,
/// such as retrieving queue runtime properties.
/// </summary>
/// <remarks>
/// This interface is designed to decouple higher layers of NetMetric from direct Azure SDK dependencies.
/// Concrete implementations in the Adapters layer typically wrap the Azure SDK’s administration client.
/// The primary operation exposed is retrieving the total message count for a queue
/// (active, dead-letter, and transfer-dead-letter).
/// </remarks>
/// <example>
/// Example usage:
/// <code>
/// // IAzureServiceBusAdmin admin = new ServiceBusAdminAdapter(commonOptions, credentialProvider, logger);
/// long count = await admin.GetQueueMessageCountAsync(
///     fqns: "mybus.servicebus.windows.net",
///     queueName: "orders",
///     ct: CancellationToken.None);
/// Console.WriteLine($"Queue depth: {count}");
/// </code>
/// </example>
public interface IAzureServiceBusAdmin
{
    /// <summary>
    /// Gets the total number of messages currently present in the specified Service Bus queue.
    /// </summary>
    /// <param name="fqns">
    /// The fully qualified namespace (FQNS) of the Service Bus instance,
    /// for example: <c>mybus.servicebus.windows.net</c>.
    /// </param>
    /// <param name="queueName">The name of the target queue.</param>
    /// <param name="ct">A cancellation token to observe.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the total number
    /// of messages in the queue, including active, dead-letter, and transfer-dead-letter messages.
    /// </returns>
    /// <remarks>
    /// Implementations may throw underlying service or request exceptions originating from the Azure SDK.
    /// Cancellation is honored by propagating <see cref="OperationCanceledException"/>.
    /// </remarks>
    Task<long> GetQueueMessageCountAsync(string fqns, string queueName, CancellationToken ct);
}
