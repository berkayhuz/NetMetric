// <copyright file="ICosmosDiagnosticsProvider.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Azure.Abstractions;

/// <summary>
/// Abstraction for collecting lightweight diagnostics from Azure Cosmos DB.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are expected to issue minimal, read-only queries against Cosmos DB in order to
/// estimate request unit (RU) consumption and client-perceived latency, without exposing direct
/// Azure SDK dependencies to higher layers. A typical implementation is <c>CosmosDiagnosticsAdapter</c>
/// which performs a tiny query to gather RU and latency data.
/// </para>
/// <para>
/// This interface is consumed by higher-level collectors (e.g., <c>CosmosDiagnosticsCollector</c>)
/// to publish metrics such as <c>azure.cosmos.request.charge</c> and <c>azure.cosmos.latency.ms</c>.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Resolve via DI:
/// var provider = serviceProvider.GetRequiredService<ICosmosDiagnosticsProvider>();
///
/// // Sample RU and latency for a specific container:
/// var (ru, latencyMs) = await provider.SampleRuAndLatencyAsync(
///     endpoint: "https://myaccount.documents.azure.com:443/",
///     database: "appdb",
///     container: "items",
///     ct: CancellationToken.None);
///
/// Console.WriteLine($"RU: {ru:F2}, latency: {latencyMs:F1} ms");
/// ]]></code>
/// </example>
public interface ICosmosDiagnosticsProvider
{
    /// <summary>
    /// Executes a lightweight sample query against the specified database and container to measure RU charge and latency.
    /// </summary>
    /// <param name="endpoint">The Cosmos DB account endpoint URI (e.g., <c>https://myaccount.documents.azure.com:443/</c>).</param>
    /// <param name="database">The name of the Cosmos DB database.</param>
    /// <param name="container">The name of the container within the database.</param>
    /// <param name="ct">A <see cref="System.Threading.CancellationToken"/> to observe.</param>
    /// <returns>
    /// A task that resolves to a tuple <c>(ru, latencyMs)</c> where:
    /// <list type="bullet">
    /// <item><description><c>ru</c>: The request units consumed by the sample query.</description></item>
    /// <item><description><c>latencyMs</c>: The measured elapsed time of the query in milliseconds.</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Implementations should minimize impact on the target container (e.g., using <c>SELECT TOP 1</c>
    /// with a small page size) and may employ retry logic for transient failures (e.g., HTTP 429/503).
    /// </remarks>
    Task<(double ru, double latencyMs)> SampleRuAndLatencyAsync(
        string endpoint,
        string database,
        string container,
        CancellationToken ct);
}
