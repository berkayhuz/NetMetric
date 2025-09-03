// <copyright file="CosmosDiagnosticsAdapter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Azure.Core;

namespace NetMetric.Azure.Adapters;

/// <summary>
/// Provides lightweight diagnostics against an Azure Cosmos DB account by executing
/// a minimal query to estimate request unit (RU) consumption and end-to-end latency.
/// </summary>
/// <remarks>
/// <para>
/// This adapter issues a very small read query to minimize impact on the target container while still
/// obtaining a meaningful RU charge and measuring client-perceived latency. It is intended for
/// health/diagnostic scenarios and not for full-fledged performance testing.
/// </para>
/// <para>
/// Internally, it executes <c>SELECT TOP 1 c.id FROM c</c> with <c>MaxItemCount = 1</c>. Transient Cosmos DB
/// errors (HTTP <c>429</c>, <c>503</c>, <c>500</c>) are retried using <see cref="RetryPolicy.ExecuteAsync{T}(System.Func{System.Threading.CancellationToken,System.Threading.Tasks.Task{T}}, System.Func{System.Exception,bool}, System.TimeSpan, System.Threading.CancellationToken)"/>.
/// </para>
/// <para><b>Thread safety:</b> Instances are safe to use concurrently across multiple callers, as a new
/// short-lived <see cref="Microsoft.Azure.Cosmos.CosmosClient"/> is created per operation.</para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Given DI registrations for AzureCommonOptions and IAzureCredentialProvider:
/// ICosmosDiagnosticsProvider diag = new CosmosDiagnosticsAdapter(commonOptions, credentialProvider);
///
/// // Sample RU and latency for a specific database/container:
/// var (ru, ms) = await diag.SampleRuAndLatencyAsync(
///     endpoint: "https://myacct.documents.azure.com:443/",
///     database: "appdb",
///     container: "items",
///     ct: CancellationToken.None);
///
/// Console.WriteLine($"RU={ru:F2}, latency(ms)={ms:F1}");
/// ]]></code>
/// </example>
internal sealed class CosmosDiagnosticsAdapter : ICosmosDiagnosticsProvider
{
    private readonly AzureCommonOptions _common;
    private readonly IAzureCredentialProvider _cred;

    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosDiagnosticsAdapter"/> class.
    /// </summary>
    /// <param name="common">Common Azure client options such as timeout settings.</param>
    /// <param name="cred">Credential provider used to create a <see cref="global::Azure.Core.TokenCredential"/>.</param>
    public CosmosDiagnosticsAdapter(
        AzureCommonOptions common,
        IAzureCredentialProvider cred)
    {
        _common = common;
        _cred = cred;
    }

    /// <summary>
    /// Executes a small query against the specified Cosmos DB container to measure
    /// the request units (RUs) consumed and the client-side latency.
    /// </summary>
    /// <param name="endpoint">
    /// The Cosmos DB account endpoint URI.  
    /// Example: <c>https://&lt;account&gt;.documents.azure.com:443/</c>.
    /// </param>
    /// <param name="database">The database identifier to target.</param>
    /// <param name="container">The container identifier to target.</param>
    /// <param name="ct">A <see cref="System.Threading.CancellationToken"/> to observe.</param>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    ///   <item><description><c>ru</c>: The request charge reported by Cosmos DB for the query.</description></item>
    ///   <item><description><c>latencyMs</c>: The elapsed time in milliseconds from query start to completion (including retries).</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// A short-lived <see cref="Microsoft.Azure.Cosmos.CosmosClient"/> is created using the <see cref="global::Azure.Core.TokenCredential"/>
    /// returned by <see cref="IAzureCredentialProvider.CreateCredential"/>. The request timeout is derived from
    /// <see cref="AzureCommonOptions.ClientTimeoutMs"/> (values &lt;= 0 imply no explicit client timeout).
    /// </para>
    /// <para>
    /// Only transient errors are retried; non-transient errors are propagated to the caller.
    /// The overall retry window is also bounded by <see cref="AzureCommonOptions.ClientTimeoutMs"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="System.OperationCanceledException">Thrown if the operation is canceled.</exception>
    /// <exception cref="Microsoft.Azure.Cosmos.CosmosException">
    /// Thrown when the Cosmos DB service returns a non-transient error or when retries are exhausted.
    /// </exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var (ru, ms) = await adapter.SampleRuAndLatencyAsync(
    ///     "https://myacct.documents.azure.com:443/", "db", "items", CancellationToken.None);
    /// // RU might be ~2.8 for a tiny indexed read; latency will include any retries.
    /// ]]></code>
    /// </example>
    public async Task<(double ru, double latencyMs)> SampleRuAndLatencyAsync(
        string endpoint,
        string database,
        string container,
        CancellationToken ct)
    {
        var tokenCred = (TokenCredential)_cred.CreateCredential();

        using var cosmos = new Microsoft.Azure.Cosmos.CosmosClient(
            endpoint,
            tokenCred,
            new Microsoft.Azure.Cosmos.CosmosClientOptions
            {
                RequestTimeout = TimeSpan.FromMilliseconds(Math.Max(1, _common.ClientTimeoutMs))
            });

        var cont = cosmos.GetContainer(database, container);

        static bool IsTransient(Exception ex)
            => ex is Microsoft.Azure.Cosmos.CosmosException ce
               && ((int)ce.StatusCode == 429 || (int)ce.StatusCode == 503 || (int)ce.StatusCode == 500);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var ru = await RetryPolicy.ExecuteAsync(
            async t =>
            {
                var q = new Microsoft.Azure.Cosmos.QueryDefinition("SELECT TOP 1 c.id FROM c");
                using var iter = cont.GetItemQueryIterator<dynamic>(
                    q,
                    requestOptions: new Microsoft.Azure.Cosmos.QueryRequestOptions { MaxItemCount = 1 });

                if (iter.HasMoreResults)
                {
                    var page = await iter.ReadNextAsync(t).ConfigureAwait(false);
                    return page.RequestCharge;
                }

                return 0.0;
            },
            IsTransient,
            TimeSpan.FromMilliseconds(Math.Max(1, _common.ClientTimeoutMs)),
            ct).ConfigureAwait(false);

        sw.Stop();
        return (ru, sw.Elapsed.TotalMilliseconds);
    }
}
