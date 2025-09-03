// <copyright file="CosmosOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Azure.Options;

/// <summary>
/// Provides configuration options for Azure Cosmos DB integration.
/// </summary>
/// <remarks>
/// <para>
/// These options define which Cosmos DB account and containers will be targeted
/// by diagnostics and metric collectors.
/// </para>
/// <para>
/// If no <see cref="AccountEndpoint"/> or containers are specified, Cosmos-related collectors
/// will not be activated.
/// </para>
/// </remarks>
/// <example>
/// Example configuration in <c>appsettings.json</c>:
/// <code><![CDATA[
/// {
///   "CosmosOptions": {
///     "AccountEndpoint": "https://mycosmosaccount.documents.azure.com:443/",
///     "Containers": [
///       { "Database": "orders", "Container": "activeOrders" },
///       { "Database": "billing", "Container": "invoices" }
///     ]
///   }
/// }
/// ]]></code>
/// </example>
public sealed class CosmosOptions
{
    /// <summary>
    /// Gets the account endpoint URI of the Cosmos DB account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This should be a fully qualified endpoint, for example:
    /// <c>https://mycosmosaccount.documents.azure.com:443/</c>.
    /// </para>
    /// <para>
    /// If this property is empty or <see langword="null"/>, no Cosmos collectors will be activated.
    /// </para>
    /// </remarks>
    public string AccountEndpoint { get; init; } = "";

    /// <summary>
    /// Gets the set of target databases and containers for which diagnostics should be collected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each element in the collection is a tuple containing:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>Database</c>: The name of the Cosmos DB database.</description></item>
    ///   <item><description><c>Container</c>: The name of the container within the database.</description></item>
    /// </list>
    /// <para>
    /// Defaults to an empty collection if not specified.
    /// </para>
    /// </remarks>
    /// <example>
    /// Example C# initialization:
    /// <code><![CDATA[
    /// var options = new CosmosOptions
    /// {
    ///     AccountEndpoint = "https://mycosmosaccount.documents.azure.com:443/",
    ///     Containers = new List<(string Database, string Container)>
    ///     {
    ///         ("orders", "activeOrders"),
    ///         ("billing", "invoices")
    ///     }
    /// };
    /// ]]></code>
    /// </example>
    public IReadOnlyList<(string Database, string Container)> Containers { get; init; }
        = Array.Empty<(string Database, string Container)>();
}
