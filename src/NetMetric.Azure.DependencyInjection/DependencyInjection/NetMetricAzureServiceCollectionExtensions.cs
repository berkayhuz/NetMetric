// <copyright file="NetMetricAzureServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace NetMetric.Azure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering the NetMetric Azure module and its dependencies
/// into an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// This type wires up option binding and validation, a default Azure credential provider,
/// and the <see cref="AzureModule"/> that composes Azure collectors (Service Bus, Storage Queues, Cosmos DB).
/// </para>
/// <para><b>Thread-safety</b><br/>
/// Registration methods are typically called during application startup (single-threaded). The resulting services
/// (credential provider, module, options) are registered as singletons, consistent with typical usage.
/// </para>
/// </remarks>
/// <example>
/// Minimal registration in a generic host:
/// <code><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using Microsoft.Extensions.Hosting;
/// using NetMetric.Azure.DependencyInjection;
///
/// var host = Host.CreateDefaultBuilder(args)
///     .ConfigureServices(services =>
///     {
///         services.AddNetMetricCore(); // your core metrics registration
///
///         services.AddNetMetricAzure(
///             common: o => { o.ClientTimeoutMs = 10_000; },
///             serviceBus: sb =>
///             {
///                 sb.FullyQualifiedNamespace = "mybus.servicebus.windows.net";
///                 sb.Queues = new[] { "orders", "billing-dlq" };
///                 sb.MaxQueuesPerCollect = 4;
///             },
///             storageQueues: sq =>
///             {
///                 sq.AccountName = "mystorageaccount";
///                 sq.Queues = new[] { "events", "errors" };
///                 sq.EndpointSuffix = "core.windows.net";
///                 sq.MaxQueuesPerCollect = 4;
///             },
///             cosmos: cs =>
///             {
///                 cs.AccountEndpoint = "https://mycosmos.documents.azure.com:443/";
///                 cs.Containers = new List<(string Database, string Container)>
///                 {
///                     ("orders", "activeOrders"),
///                     ("billing", "invoices")
///                 };
///             });
///     })
///     .Build();
///
/// await host.RunAsync();
/// ]]></code>
/// </example>
public static class NetMetricAzureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the NetMetric Azure module along with option bindings, validators,
    /// the default Azure credential provider, and the Azure module itself.
    /// </summary>
    /// <param name="services">The DI service collection to register services into.</param>
    /// <param name="common">Optional delegate to configure <see cref="AzureCommonOptions"/>.</param>
    /// <param name="serviceBus">Optional delegate to configure <see cref="ServiceBusOptions"/>.</param>
    /// <param name="storageQueues">Optional delegate to configure <see cref="StorageQueuesOptions"/>.</param>
    /// <param name="cosmos">Optional delegate to configure <see cref="CosmosOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// <para>This method performs the following:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     Binds <see cref="AzureCommonOptions"/>, <see cref="ServiceBusOptions"/>,
    ///     <see cref="StorageQueuesOptions"/>, and <see cref="CosmosOptions"/> via <c>IOptions&lt;T&gt;</c>.
    ///   </description></item>
    ///   <item><description>
    ///     Adds validators:
    ///     <see cref="AzureCommonOptionsValidator"/>,
    ///     <see cref="ServiceBusOptionsValidator"/>,
    ///     <see cref="StorageQueuesOptionsValidator"/>,
    ///     and <see cref="CosmosOptionsValidator"/>.
    ///   </description></item>
    ///   <item><description>
    ///     Registers <see cref="IAzureCredentialProvider"/> with a default implementation:
    ///     <see cref="DefaultAzureCredentialProvider"/>.
    ///   </description></item>
    ///   <item><description>
    ///     Registers the compositional <see cref="AzureModule"/> as an <see cref="IModule"/> singleton,
    ///     which instantiates collectors based on provided options.
    ///   </description></item>
    /// </list>
    /// <para>
    /// To disable a specific collector set, leave its corresponding options unconfigured or empty.
    /// For example, omitting <see cref="ServiceBusOptions.Queues"/> prevents Service Bus collectors from being created.
    /// </para>
    /// </remarks>
    /// <example>
    /// Enabling only Service Bus with fluent options:
    /// <code><![CDATA[
    /// services.AddNetMetricAzure(
    ///     serviceBus: sb =>
    ///     {
    ///         sb.FullyQualifiedNamespace = "mybus.servicebus.windows.net";
    ///         sb.Queues = new[] { "orders" };
    ///     });
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricAzure(
        this IServiceCollection services,
        Action<AzureCommonOptions>? common = null,
        Action<ServiceBusOptions>? serviceBus = null,
        Action<StorageQueuesOptions>? storageQueues = null,
        Action<CosmosOptions>? cosmos = null)
    {
        // Options + Validators
        services.AddOptions<AzureCommonOptions>().Configure(o => common?.Invoke(o));
        services.AddOptions<ServiceBusOptions>().Configure(o => serviceBus?.Invoke(o));
        services.AddOptions<StorageQueuesOptions>().Configure(o => storageQueues?.Invoke(o));
        services.AddOptions<CosmosOptions>().Configure(o => cosmos?.Invoke(o));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AzureCommonOptions>, AzureCommonOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ServiceBusOptions>, ServiceBusOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StorageQueuesOptions>, StorageQueuesOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CosmosOptions>, CosmosOptionsValidator>());

        // Credential Provider (port)
        services.TryAddSingleton<IAzureCredentialProvider>(sp =>
        {
            var commonOpts = sp.GetRequiredService<IOptions<AzureCommonOptions>>().Value;
            return new DefaultAzureCredentialProvider(commonOpts);
        });

        // Module
        services.AddSingleton<IModule>(sp =>
        {
            var factory = sp.GetRequiredService<IMetricFactory>();
            var cred = sp.GetRequiredService<IAzureCredentialProvider>();
            var loggerF = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();

            var optCommon = sp.GetRequiredService<IOptions<AzureCommonOptions>>();
            var optSB = sp.GetService<IOptions<ServiceBusOptions>>();
            var optSQ = sp.GetService<IOptions<StorageQueuesOptions>>();
            var optCS = sp.GetService<IOptions<CosmosOptions>>();

            return new AzureModule(cred, optCommon, optSB, optSQ, optCS, factory, loggerF);
        });

        return services;
    }
}
