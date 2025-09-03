// <copyright file="StorageQueueAdminAdapter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// </copyright>

using Azure;
using Azure.Core;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace NetMetric.Azure.Adapters;

/// <summary>
/// Provides lightweight administrative operations for Azure Storage Queues,
/// such as retrieving the approximate message count for a given queue.
/// </summary>
/// <remarks>
/// <para>
/// This adapter wraps <see cref="QueueClient"/> to query queue metadata with
/// conservative defaults and retry handling for transient failures (e.g., 429/503).
/// It is intentionally minimal and read-only: it does <b>not</b> create, delete,
/// or mutate queues or messages.
/// </para>
/// <para>
/// <b>Thread-safety</b><br/>
/// The adapter holds no shared mutable state and is safe to use concurrently
/// from multiple threads. Each call constructs a short-lived <see cref="QueueClient"/>.
/// </para>
/// <para>
/// <b>Authentication</b><br/>
/// The adapter obtains a <see cref="TokenCredential"/> via <see cref="IAzureCredentialProvider"/>.
/// Ensure the principal has at least <c>Microsoft.Storage/storageAccounts/queueServices/queues/read</c>
/// permissions (or equivalent) on the target account/queue.
/// </para>
/// <para>
/// <b>Approximate count semantics</b><br/>
/// <see cref="QueueProperties.ApproximateMessagesCount"/> is an <i>estimate</i> provided by the service and
/// may lag actual enqueues/dequeues. Treat the value as a health/rough capacity signal
/// rather than an exact inventory.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Typical usage inside a collector:
/// var count = await admin.GetApproxMessageCountAsync(
///     accountName: "mystorage",
///     queueName: "orders",
///     endpointSuffix: null,            // defaults to "core.windows.net"
///     ct: cancellationToken);
///
/// // Sovereign cloud (e.g., Azure China):
/// var cnCount = await admin.GetApproxMessageCountAsync(
///     "mystoragecn", "orders", "core.chinacloudapi.cn", cancellationToken);
/// </code>
/// </example>
internal sealed class StorageQueueAdminAdapter : IAzureStorageQueueAdmin
{
    private readonly AzureCommonOptions _common;
    private readonly IAzureCredentialProvider _cred;
    private readonly Microsoft.Extensions.Logging.ILogger<StorageQueueAdminAdapter>? _log;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageQueueAdminAdapter"/> class.
    /// </summary>
    /// <param name="common">Common Azure options (for example, client timeout in milliseconds).</param>
    /// <param name="cred">Credential provider used to obtain a <see cref="TokenCredential"/>.</param>
    /// <param name="log">Optional logger for warnings and diagnostic messages.</param>
    /// <remarks>
    /// The <paramref name="common"/> timeout is applied as an overall ceiling for the
    /// underlying request (including retries).
    /// </remarks>
    public StorageQueueAdminAdapter(
        AzureCommonOptions common,
        IAzureCredentialProvider cred,
        Microsoft.Extensions.Logging.ILogger<StorageQueueAdminAdapter>? log = null)
    {
        _common = common;
        _cred = cred;
        _log = log;
    }

    /// <summary>
    /// Gets the approximate number of messages in the specified Azure Storage Queue.
    /// </summary>
    /// <param name="accountName">The storage account name (without protocol or suffix), e.g. <c>mystorage</c>.</param>
    /// <param name="queueName">The name of the target queue.</param>
    /// <param name="endpointSuffix">
    /// Optional DNS suffix. If <see langword="null"/> or empty, defaults to <c>core.windows.net</c>.
    /// Useful for sovereign/specialized clouds such as <c>core.chinacloudapi.cn</c> or <c>core.usgovcloudapi.net</c>.
    /// </param>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe.</param>
    /// <returns>
    /// The approximate number of messages currently in the queue as reported by the service.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Implementation details:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>Builds the endpoint as <c>https://{accountName}.queue.{suffix}/{queueName}</c>.</description>
    ///   </item>
    ///   <item>
    ///     <description>Configures <see cref="QueueClientOptions.MessageEncoding"/> to <see cref="QueueMessageEncoding.Base64"/> to ensure consistent decoding for metadata calls.</description>
    ///   </item>
    ///   <item>
    ///     <description>Invokes <see cref="QueueClient.GetPropertiesAsync(CancellationToken)"/> and reads <see cref="QueueProperties.ApproximateMessagesCount"/>.</description>
    ///   </item>
    ///   <item>
    ///     <description>Applies <see cref="RetryPolicy.ExecuteAsync{T}(System.Func{System.Threading.CancellationToken,System.Threading.Tasks.Task{T}}, System.Func{System.Exception,bool}, System.TimeSpan, System.Threading.CancellationToken)"/> for transient <see cref="RequestFailedException"/> (429/500/503).</description>
    ///   </item>
    /// </list>
    /// <para>
    /// The returned value is not guaranteed to be exact at the moment of the call.
    /// Consider smoothing/averaging it when building dashboards or alerts.
    /// </para>
    /// </remarks>
    /// <exception cref="RequestFailedException">
    /// Thrown when the storage service returns a non-transient error (for example, 404 for a missing queue, or 403 for insufficient permissions).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown if the request is canceled via <paramref name="ct"/> or the overall timeout elapses.
    /// </exception>
    /// <example>
    /// <code>
    /// // Reading counts for multiple queues:
    /// foreach (var q in new[] { "orders", "payments", "invoices" })
    /// {
    ///     var approx = await adapter.GetApproxMessageCountAsync("mystorage", q, null, ct);
    ///     Console.WriteLine($"{q}: ~{approx} messages");
    /// }
    /// </code>
    /// </example>
    public async Task<long> GetApproxMessageCountAsync(string accountName, string queueName, string? endpointSuffix, CancellationToken ct)
    {
        var endpoint = $"https://{accountName}.queue.{(string.IsNullOrWhiteSpace(endpointSuffix) ? "core.windows.net" : endpointSuffix)}";
        var tokenCred = (TokenCredential)_cred.CreateCredential();

        var client = new QueueClient(
            new Uri($"{endpoint}/{queueName}"),
            tokenCred,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });

        static bool IsTransient(Exception ex)
            => ex is RequestFailedException rfe && (rfe.Status == 429 || rfe.Status == 503 || rfe.Status == 500);

        Response<QueueProperties> props =
            await RetryPolicy.ExecuteAsync<Response<QueueProperties>>(
                t => client.GetPropertiesAsync(t),
                IsTransient,
                TimeSpan.FromMilliseconds(Math.Max(1, _common.ClientTimeoutMs)),
                ct).ConfigureAwait(false);

        return props.Value.ApproximateMessagesCount;
    }
}
