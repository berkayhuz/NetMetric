// <copyright file="StorageQueuesOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Azure.Options;

/// <summary>
/// Provides configuration options for Azure Storage Queues integration.
/// </summary>
/// <remarks>
/// <para>
/// These options define which storage account and queues will be targeted
/// by the Storage Queue depth collector
/// (<see cref="NetMetric.Azure.Collectors.StorageQueueDepthCollector"/>).
/// </para>
/// <para>
/// Validation rules are enforced by
/// <see cref="NetMetric.Azure.Options.Validation.StorageQueuesOptionsValidator"/>.
/// </para>
/// </remarks>
/// <example>
/// <para><b>appsettings.json</b></para>
/// <code language="json">
/// {
///   "NetMetric": {
///     "Azure": {
///       "StorageQueues": {
///         "AccountName": "mystorageaccount",
///         "Queues": [ "orders", "invoices" ],
///         "EndpointSuffix": "core.windows.net",
///         "MaxQueuesPerCollect": 8
///       }
///     }
///   }
/// }
/// </code>
/// <para><b>Program.cs</b></para>
/// <code language="csharp"><![CDATA[
/// builder.Services.Configure<StorageQueuesOptions>(
///     builder.Configuration.GetSection("NetMetric:Azure:StorageQueues"));
///
/// // Later, the Azure module reads StorageQueuesOptions via IOptions<StorageQueuesOptions>
/// // and activates StorageQueueDepthCollector if AccountName and Queues are provided.
/// ]]></code>
/// </example>
public sealed class StorageQueuesOptions
{
    /// <summary>
    /// Gets the name of the Azure Storage account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Example: <c>mystorageaccount</c>.
    /// </para>
    /// <para>
    /// If empty, no Storage Queue collectors will be activated.
    /// This is enforced by <see cref="NetMetric.Azure.Options.Validation.StorageQueuesOptionsValidator"/>.
    /// </para>
    /// </remarks>
    public string AccountName { get; init; } = "";

    /// <summary>
    /// Gets the list of queue names to monitor for approximate message count.
    /// </summary>
    /// <remarks>
    /// Each entry should be the exact name of an Azure Storage Queue.  
    /// Defaults to an empty collection (no queues monitored).
    /// </remarks>
    public IReadOnlyList<string> Queues { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the optional DNS suffix for the endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <c>core.windows.net</c>.
    /// </para>
    /// <para>
    /// Useful for sovereign or specialized Azure clouds (e.g., <c>core.chinacloudapi.cn</c>).  
    /// Must not include a URI scheme (e.g., <c>https://</c>).
    /// </para>
    /// </remarks>
    public string? EndpointSuffix { get; init; } = "core.windows.net";

    /// <summary>
    /// Gets the maximum number of queues to query concurrently during a collection cycle.
    /// </summary>
    /// <remarks>
    /// If <c>null</c>, defaults to <see cref="System.Environment.ProcessorCount"/>.  
    /// A value of <c>1</c> forces sequential collection.
    /// </remarks>
    public int? MaxQueuesPerCollect { get; init; }
}
