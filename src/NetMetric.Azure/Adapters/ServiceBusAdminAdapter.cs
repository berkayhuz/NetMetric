// <copyright file="ServiceBusAdminAdapter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Azure;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace NetMetric.Azure.Adapters;

/// <summary>
/// Provides lightweight, read-only administrative operations for Azure Service Bus
/// using <see cref="ServiceBusAdministrationClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// This adapter focuses on querying queue <em>runtime</em> properties (not management/CRUD),
/// specifically total message counts. Transient faults are retried with
/// <see cref="RetryPolicy"/> (exponential backoff + jitter), and overall timeouts
/// are governed by <see cref="AzureCommonOptions.ClientTimeoutMs"/>.
/// </para>
/// <para><b>Thread safety:</b> The adapter is stateless and can be used concurrently
/// across threads. A short-lived <see cref="ServiceBusAdministrationClient"/> is
/// created per invocation; callers can own lifetime/caching externally if needed.
/// </para>
/// <para><b>Authentication:</b> Credentials are resolved via the injected
/// <see cref="IAzureCredentialProvider"/> (for example, a <c>DefaultAzureCredential</c>
/// chain). Ensure the principal has at least <c>Azure Service Bus Data Reader</c>
/// permissions (RBAC) on the target namespace to read runtime properties.
/// </para>
/// <para><b>Namespace:</b> Pass the fully qualified namespace (FQNS), such as
/// <c>contoso.servicebus.windows.net</c>. A connection string is <em>not</em> required.
/// </para>
/// <para><b>Error model:</b> Transient <see cref="ServiceBusException"/> (where
/// <see cref="ServiceBusException.IsTransient"/> is <see langword="true"/>) and
/// <see cref="RequestFailedException"/> with HTTP status 429/500/503 are retried. Non-transient
/// failures (authorization errors, not found, etc.) are surfaced to callers.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Build credential provider and options (e.g., via DI)
/// var common = new AzureCommonOptions
/// {
///     // Total wall-clock timeout for the underlying call (0 => infinite)
///     ClientTimeoutMs = 10_000
/// };
/// 
/// IAzureCredentialProvider cred = new DefaultAzureCredentialProvider(common);
/// 
/// // Create the adapter
/// var admin = new ServiceBusAdminAdapter(common, cred, log: null);
/// 
/// // Query total depth for a queue
/// CancellationToken ct = default;
/// string fqns = "contoso.servicebus.windows.net";
/// string queue = "orders";
/// 
/// long total = await admin.GetQueueMessageCountAsync(fqns, queue, ct);
/// // 'total' includes active, dead-letter, and transfer-dead-letter messages.
/// ]]></code>
/// </example>
/// <seealso cref="ServiceBusAdministrationClient"/>
/// <seealso cref="QueueRuntimeProperties"/>
/// <seealso cref="ServiceBusException"/>
internal sealed class ServiceBusAdminAdapter : IAzureServiceBusAdmin
{
    private readonly AzureCommonOptions _common;
    private readonly IAzureCredentialProvider _cred;
    private readonly Microsoft.Extensions.Logging.ILogger<ServiceBusAdminAdapter>? _log;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusAdminAdapter"/> class.
    /// </summary>
    /// <param name="common">Common Azure options (e.g., client timeout in milliseconds).</param>
    /// <param name="cred">Credential provider used to obtain a <see cref="TokenCredential"/>.</param>
    /// <param name="log">Optional logger for warnings and diagnostic information.</param>
    /// <remarks>
    /// The adapter does not validate or mutate the provided options/provider; callers
    /// should ensure that <paramref name="common"/> and <paramref name="cred"/> are configured
    /// and registered appropriately (typically via DI).
    /// </remarks>
    public ServiceBusAdminAdapter(
        AzureCommonOptions common,
        IAzureCredentialProvider cred,
        Microsoft.Extensions.Logging.ILogger<ServiceBusAdminAdapter>? log = null)
    {
        _common = common;
        _cred = cred;
        _log = log;
    }

    /// <summary>
    /// Retrieves the total number of messages currently in the given Service Bus queue,
    /// including active, dead-letter, and transfer-dead-letter messages.
    /// </summary>
    /// <param name="fqns">The fully qualified namespace of the Service Bus (e.g., <c>mybus.servicebus.windows.net</c>).</param>
    /// <param name="queueName">The name of the target queue.</param>
    /// <param name="ct">The cancellation token to observe.</param>
    /// <returns>
    /// The total number of messages in the queue at the time of the request.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Internally calls <see cref="ServiceBusAdministrationClient.GetQueueRuntimePropertiesAsync(string, CancellationToken)"/>
    /// and returns the sum of <see cref="QueueRuntimeProperties.ActiveMessageCount"/>,
    /// <see cref="QueueRuntimeProperties.DeadLetterMessageCount"/>, and
    /// <see cref="QueueRuntimeProperties.TransferDeadLetterMessageCount"/>.
    /// </para>
    /// <para><b>Retries &amp; timeouts:</b> Transient errors (HTTP 429/500/503 or
    /// <see cref="ServiceBusException.IsTransient"/>) are retried via <see cref="RetryPolicy"/>.
    /// The overall wall-clock timeout is controlled by <see cref="AzureCommonOptions.ClientTimeoutMs"/>.
    /// </para>
    /// <para><b>Behavior when the queue is missing:</b> A non-transient error (e.g., 404 Not Found)
    /// will propagate as <see cref="RequestFailedException"/>. Callers should handle this according
    /// to their own telemetry or provisioning flow.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="fqns"/> or <paramref name="queueName"/> are <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="RequestFailedException">
    /// Thrown when the Azure Service Bus service returns a non-transient error response (e.g., 401/403/404).
    /// </exception>
    /// <exception cref="ServiceBusException">
    /// Thrown for errors specific to Service Bus operations, not classified as transient.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown if the request is canceled via <paramref name="ct"/> or the overall timeout elapses.
    /// </exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Typical usage inside a collector loop
    /// var total = await _admin.GetQueueMessageCountAsync(
    ///     fqns: "contoso.servicebus.windows.net",
    ///     queueName: "payments",
    ///     ct: cancellationToken);
    ///
    /// _depth.AddSibling("azure.servicebus.queue.depth", "sb queue depth", total,
    ///     new Dictionary<string, string> { ["fqns"] = "contoso.servicebus.windows.net", ["queue"] = "payments" });
    /// ]]></code>
    /// </example>
    public async Task<long> GetQueueMessageCountAsync(string fqns, string queueName, CancellationToken ct)
    {
        var credential = (TokenCredential)_cred.CreateCredential();
        var admin = new ServiceBusAdministrationClient(fqns, credential);

        static bool IsTransient(Exception ex)
            => ex is ServiceBusException sbe && sbe.IsTransient
            || ex is RequestFailedException rfe && (rfe.Status == 429 || rfe.Status == 503 || rfe.Status == 500);

        var props = await RetryPolicy.ExecuteAsync(
            t => admin.GetQueueRuntimePropertiesAsync(queueName, t),
            IsTransient,
            TimeSpan.FromMilliseconds(Math.Max(1, _common.ClientTimeoutMs)),
            ct).ConfigureAwait(false);

        var p = props.Value;
        return p.ActiveMessageCount + p.DeadLetterMessageCount + p.TransferDeadLetterMessageCount;
    }
}
