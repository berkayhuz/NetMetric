// <copyright file="AzureModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.4
// </copyright>

using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetMetric.Azure.Modules;

/// <summary>
/// Composes Azure-specific metric collectors based on the provided option sets
/// (Service Bus, Storage Queues, and Cosmos DB).
/// </summary>
/// <remarks>
/// <para>
/// This module exposes only abstraction interfaces to the outside (e.g., <see cref="IMetricCollector"/>),
/// while concrete Azure SDK clients are encapsulated behind adapter classes in the
/// <c>NetMetric.Azure.Adapters</c> layer (e.g., <see cref="ServiceBusAdminAdapter"/>,
/// <see cref="StorageQueueAdminAdapter"/>, <see cref="CosmosDiagnosticsAdapter"/>).
/// </para>
/// <para>
/// At construction time, the module inspects the bound option objects:
/// <list type="bullet">
///   <item><description>
///     If <see cref="ServiceBusOptions"/> is configured (non-empty FQNS and at least one queue),
///     a <see cref="ServiceBusQueueDepthCollector"/> is registered.
///   </description></item>
///   <item><description>
///     If <see cref="StorageQueuesOptions"/> is configured (non-empty account name and at least one queue),
///     a <see cref="StorageQueueDepthCollector"/> is registered.
///   </description></item>
///   <item><description>
///     If <see cref="CosmosOptions"/> is configured (non-empty endpoint and at least one (database, container) pair),
///     a <see cref="CosmosDiagnosticsCollector"/> is registered.
///   </description></item>
/// </list>
/// </para>
/// <para><b>Thread safety:</b> The module is intended to be constructed once and treated as immutable;
/// the internal collector list is only populated in the constructor.</para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Configure options (e.g., in Program.cs or a test)
/// services.Configure<AzureCommonOptions>(o =>
/// {
///     o.ManagedIdentityClientId = "<client-id-if-any>";
///     o.ClientTimeoutMs = 10_000;
/// });
/// 
/// services.Configure<ServiceBusOptions>(o =>
/// {
///     o.FullyQualifiedNamespace = "mybus.servicebus.windows.net";
///     o.Queues = new[] { "orders", "payments" };
///     o.MaxQueuesPerCollect = 4;
/// });
/// 
/// services.Configure<StorageQueuesOptions>(o =>
/// {
///     o.AccountName = "mystorage";
///     o.Queues = new[] { "ingress", "egress" };
///     o.EndpointSuffix = "core.windows.net";
///     // o.MaxQueuesPerCollect = null; // defaults to Environment.ProcessorCount
/// });
/// 
/// services.Configure<CosmosOptions>(o =>
/// {
///     o.AccountEndpoint = "https://mycosmos.documents.azure.com:443/";
///     o.Containers = new (string Database, string Container)[] { ("appdb", "users"), ("appdb", "orders") };
/// });
/// 
/// // Suppose you resolved these from DI:
/// var credProvider = new DefaultAzureCredentialProvider(new AzureCommonOptions());
/// var common = services.BuildServiceProvider().GetRequiredService<IOptions<AzureCommonOptions>>();
/// var sb = services.BuildServiceProvider().GetService<IOptions<ServiceBusOptions>>();
/// var sq = services.BuildServiceProvider().GetService<IOptions<StorageQueuesOptions>>();
/// var cs = services.BuildServiceProvider().GetService<IOptions<CosmosOptions>>();
/// var metricFactory = new MyMetricFactory(); // your IMetricFactory implementation
/// var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
/// 
/// // Create the module
/// var module = new AzureModule(
///     credProvider,
///     common,
///     sb,
///     sq,
///     cs,
///     metricFactory,
///     loggerFactory);
/// 
/// // Retrieve the collectors and run a collection cycle
/// foreach (var collector in module.GetCollectors())
/// {
///     await collector.CollectAsync(CancellationToken.None);
/// }
/// ]]></code>
/// </example>
/// <seealso cref="ServiceBusQueueDepthCollector"/>
/// <seealso cref="StorageQueueDepthCollector"/>
/// <seealso cref="CosmosDiagnosticsCollector"/>
/// <seealso cref="ServiceBusAdminAdapter"/>
/// <seealso cref="StorageQueueAdminAdapter"/>
/// <seealso cref="CosmosDiagnosticsAdapter"/>
public sealed class AzureModule : IModule, IModuleLifecycle
{
    // List<T> yerine Collection<T> kullanıldı
    private readonly Collection<IMetricCollector> _collectors;

    /// <summary>
    /// Gets the logical module name used for tagging and reporting.
    /// </summary>
    public string Name => "Azure";

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureModule"/> class and registers
    /// Azure-related collectors based on the provided options.
    /// </summary>
    /// <param name="credentialProvider">
    /// The Azure credential provider used by adapter classes to create SDK clients
    /// (e.g., <see cref="global::Azure.Core.TokenCredential"/> instances).
    /// </param>
    /// <param name="commonOptions">
    /// Common Azure client options (e.g., <see cref="AzureCommonOptions.ClientTimeoutMs"/>).
    /// Must not be <c>null</c> and must contain a non-<c>null</c> <see cref="OptionsWrapper{T}.Value"/>.
    /// </param>
    /// <param name="serviceBusOptions">
    /// Optional Service Bus configuration. If provided and valid, registers a
    /// <see cref="ServiceBusQueueDepthCollector"/>.
    /// </param>
    /// <param name="storageQueuesOptions">
    /// Optional Storage Queues configuration. If provided and valid, registers a
    /// <see cref="StorageQueueDepthCollector"/>.
    /// </param>
    /// <param name="cosmosOptions">
    /// Optional Cosmos DB configuration. If provided and valid, registers a
    /// <see cref="CosmosDiagnosticsCollector"/>.
    /// </param>
    /// <param name="metricFactory">
    /// Factory used to create NetMetric metric instruments (gauges, summaries, histograms).
    /// </param>
    /// <param name="loggerFactory">
    /// Optional logger factory used to create adapter/collector loggers for diagnostics.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="commonOptions"/> is <c>null</c> or its <see cref="IOptions{TOptions}.Value"/> is <c>null</c>.
    /// </exception>
    public AzureModule(
        IAzureCredentialProvider credentialProvider,
        IOptions<AzureCommonOptions> commonOptions,
        IOptions<ServiceBusOptions>? serviceBusOptions,
        IOptions<StorageQueuesOptions>? storageQueuesOptions,
        IOptions<CosmosOptions>? cosmosOptions,
        IMetricFactory metricFactory,
        ILoggerFactory? loggerFactory = null)
    {
        _collectors = new();

        ArgumentNullException.ThrowIfNull(commonOptions);

        var common = commonOptions.Value ?? throw new ArgumentNullException(nameof(commonOptions));
        var sb = serviceBusOptions?.Value;
        var sq = storageQueuesOptions?.Value;
        var cs = cosmosOptions?.Value;

        var sbLog = loggerFactory?.CreateLogger<ServiceBusAdminAdapter>();
        var sqLog = loggerFactory?.CreateLogger<StorageQueueAdminAdapter>();
        var cCos = loggerFactory?.CreateLogger<CosmosDiagnosticsCollector>();

        // Service Bus
        if (sb is not null && !string.IsNullOrWhiteSpace(sb.FullyQualifiedNamespace) && sb.Queues is { Count: > 0 })
        {
            var admin = new ServiceBusAdminAdapter(common, credentialProvider, sbLog);
            _collectors.Add(new ServiceBusQueueDepthCollector(
                metricFactory,
                admin,
                sb.FullyQualifiedNamespace,
                sb.Queues,
                common.ClientTimeoutMs,
                sb.MaxQueuesPerCollect));
        }

        // Storage Queues
        if (sq is not null && !string.IsNullOrWhiteSpace(sq.AccountName) && sq.Queues is { Count: > 0 })
        {
            var admin = new StorageQueueAdminAdapter(common, credentialProvider, sqLog);
            _collectors.Add(new StorageQueueDepthCollector(
                metricFactory,
                admin,
                sq.AccountName,
                sq.Queues,
                sq.EndpointSuffix ?? "core.windows.net",
                sq.MaxQueuesPerCollect));
        }

        // Cosmos
        if (cs is not null && !string.IsNullOrWhiteSpace(cs.AccountEndpoint) && cs.Containers is { Count: > 0 })
        {
            // Constructor takes (common, credentialProvider)
            var diag = new CosmosDiagnosticsAdapter(common, credentialProvider);
            _collectors.Add(new CosmosDiagnosticsCollector(
                metricFactory, diag, cs.AccountEndpoint, cs.Containers, cCos));
        }
    }

    /// <summary>
    /// Returns the collectors that were registered according to the configured options.
    /// Consumers can enumerate and invoke <see cref="IMetricCollector.CollectAsync(System.Threading.CancellationToken)"/>
    /// during their collection cycles.
    /// </summary>
    /// <returns>A sequence of initialized <see cref="IMetricCollector"/> instances.</returns>
    public IEnumerable<IMetricCollector> GetCollectors() => _collectors;

    /// <summary>
    /// Lifecycle hook invoked when the module is initialized. No operation by default.
    /// </summary>
    public void OnInit() { }

    /// <summary>
    /// Lifecycle hook invoked before a collection cycle starts. No operation by default.
    /// </summary>
    public void OnBeforeCollect() { }

    /// <summary>
    /// Lifecycle hook invoked after a collection cycle completes. No operation by default.
    /// </summary>
    public void OnAfterCollect() { }

    /// <summary>
    /// Lifecycle hook invoked when the module is disposed or unloaded. No operation by default.
    /// </summary>
    public void OnDispose() { }
}
