// <copyright file="IAzureStorageQueueAdmin.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Azure.Abstractions;

/// <summary>
/// Defines an abstraction for lightweight administrative operations against Azure Storage Queues.  
/// This interface decouples higher-level metric collectors from direct Azure SDK dependencies,
/// allowing adapters (e.g., <c>StorageQueueAdminAdapter</c>) to provide the actual implementation.
/// </summary>
/// <remarks>
/// <para>
/// The primary responsibility is to expose the approximate queue depth as reported by Azure Storage.
/// The count is approximate by nature and should be used for monitoring and capacity planning, not
/// for exact dequeue logic.
/// </para>
/// <para>
/// Typical implementations wrap <c>Azure.Storage.Queues.QueueClient</c> and use a
/// <see cref="global::System.Threading.CancellationToken"/> to call an API that returns
/// <c>QueueProperties.ApproximateMessagesCount</c>.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// public class MyCollector
/// {
///     private readonly IAzureStorageQueueAdmin _admin;
///
///     public MyCollector(IAzureStorageQueueAdmin admin) => _admin = admin;
///
///     public Task<long> CollectDepthAsync(CancellationToken ct) =>
///         _admin.GetApproxMessageCountAsync("mystorageaccount", "orders", "core.windows.net", ct);
/// }
/// ]]></code>
/// </example>
public interface IAzureStorageQueueAdmin
{
    /// <summary>
    /// Retrieves the approximate number of messages currently in the specified Azure Storage Queue.
    /// </summary>
    /// <param name="accountName">
    /// The Azure Storage account name (e.g., <c>mystorageaccount</c>), without protocol or suffix.
    /// </param>
    /// <param name="queueName">The target queue name (e.g., <c>orders</c>).</param>
    /// <param name="endpointSuffix">
    /// The DNS suffix for the queue endpoint (e.g., <c>core.windows.net</c>). If <see langword="null"/>,
    /// the implementation should default to the public Azure cloud suffix.
    /// </param>
    /// <param name="ct">A <see cref="global::System.Threading.CancellationToken"/> to observe.</param>
    /// <returns>
    /// A task that completes with the approximate number of messages in the queue, as reported by
    /// <c>QueueProperties.ApproximateMessagesCount</c>.
    /// </returns>
    /// <remarks>
    /// Implementations should apply retries for transient errors (for example, HTTP 429, 500, 503).
    /// </remarks>
    /// <exception cref="global::System.OperationCanceledException">
    /// Thrown if the operation is canceled via <paramref name="ct"/>.
    /// </exception>
    Task<long> GetApproxMessageCountAsync(string accountName, string queueName, string? endpointSuffix, CancellationToken ct);
}
