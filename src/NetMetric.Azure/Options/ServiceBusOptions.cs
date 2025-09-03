// <copyright file="ServiceBusOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Azure.Options;

/// <summary>
/// Provides configuration options for Azure Service Bus integration.
/// </summary>
/// <remarks>
/// <para>
/// These options define which Service Bus namespace and queues will be targeted
/// by the Service Bus queue depth collector
/// (<see cref="NetMetric.Azure.Collectors.ServiceBusQueueDepthCollector"/>).
/// </para>
/// <para>
/// Validation rules are applied by
/// <see cref="NetMetric.Azure.Options.Validation.ServiceBusOptionsValidator"/>.
/// </para>
/// </remarks>
/// <example>
/// <para><b>appsettings.json</b></para>
/// <code language="json">
/// {
///   "NetMetric": {
///     "Azure": {
///       "ServiceBus": {
///         "FullyQualifiedNamespace": "mybus.servicebus.windows.net",
///         "Queues": [ "payments", "invoices-dlq" ],
///         "MaxQueuesPerCollect": 4
///       }
///     }
///   }
/// }
/// </code>
/// <para><b>Program.cs</b></para>
/// <code language="csharp"><![CDATA[
/// builder.Services.Configure<ServiceBusOptions>(
///     builder.Configuration.GetSection("NetMetric:Azure:ServiceBus"));
///
/// // Later, the Azure module/collector reads ServiceBusOptions via IOptions<ServiceBusOptions>
/// // and collects "azure.servicebus.queue.depth" metrics accordingly.
/// ]]></code>
/// </example>
public sealed class ServiceBusOptions
{
    /// <summary>
    /// Gets the fully qualified namespace (FQNS) of the Service Bus resource.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This should be the namespace host, for example:
    /// <c>mybus.servicebus.windows.net</c>.
    /// </para>
    /// <para>
    /// If empty, no Service Bus collectors will be activated.
    /// This constraint is also enforced by
    /// <see cref="NetMetric.Azure.Options.Validation.ServiceBusOptionsValidator"/>.
    /// </para>
    /// </remarks>
    public string FullyQualifiedNamespace { get; init; } = "";

    /// <summary>
    /// Gets the list of queue names to monitor for message depth.
    /// </summary>
    /// <remarks>
    /// Each entry should be the exact name of a Service Bus queue.  
    /// Defaults to an empty collection (no queues monitored).
    /// </remarks>
    public IReadOnlyList<string> Queues { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the maximum number of queues to query concurrently during a collection cycle.
    /// </summary>
    /// <remarks>
    /// If <c>null</c>, defaults to <see cref="System.Environment.ProcessorCount"/>.  
    /// A value of <c>1</c> forces sequential collection.
    /// </remarks>
    public int? MaxQueuesPerCollect { get; init; }
}
