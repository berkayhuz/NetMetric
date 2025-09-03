// <copyright file="ElasticExportOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Elastic.Configurations;

/// <summary>
/// Provides configuration settings for exporting metrics to an Elasticsearch cluster.
/// These options control endpoint connectivity, authentication, index naming, batching,
/// retry behavior, and request size limits.
/// </summary>
/// <remarks>
/// <para>
/// This options class is designed to be used with the .NET <c>IOptions&lt;T&gt;</c> pattern
/// (for example, <c>IOptions&lt;ElasticExportOptions&gt;</c> or <c>IOptionsMonitor&lt;ElasticExportOptions&gt;</c>)
/// and is safe to bind from configuration sources such as <c>appsettings.json</c>,
/// environment variables, and user secrets.
/// </para>
/// <para>
/// The exporter constructs bulk index requests according to these settings. It is
/// recommended to tune <see cref="BatchSize"/>, <see cref="MaxBulkBytes"/>, and retry parameters
/// based on your cluster's performance profile and network characteristics.
/// </para>
/// <para>
/// Index names often need to be lowercase to comply with Elasticsearch constraints.
/// When <see cref="LowercaseIndexNames"/> is <see langword="true"/>, the final index name is
/// normalized via <see cref="string.ToLowerInvariant()"/>.
/// </para>
/// </remarks>
/// <example>
/// <code language="json">
/// // appsettings.json
/// // -----------------
/// {
///   "Elastic": {
///     "Endpoint": "https://your-elastic:9200",
///     "AuthorizationHeader": "ApiKey BASE64(ID:KEY)",
///     "IndexPattern": "netmetric-{date:yyyy.MM.dd}-{env}-{service}",
///     "IngestPipeline": "metrics_ingest",
///     "BatchSize": 1000,
///     "HttpTimeoutSeconds": 15,
///     "MaxRetries": 5,
///     "RetryBaseDelayMs": 500,
///     "Environment": "prod",
///     "ServiceName": "orders-api",
///     "MaxBulkBytes": 7340032,
///     "LowercaseIndexNames": true
///   }
/// }
/// </code>
/// </example>
/// <example>
/// <code language="csharp">
/// // Program.cs (registration sketch)
/// // --------------------------------
/// builder.Services.Configure&lt;ElasticExportOptions&gt;(
///     builder.Configuration.GetSection("Elastic"));
///
/// // Example: constructing the options manually for tests
/// var options = new ElasticExportOptions
/// {
///     Endpoint = new Uri("https://your-elastic:9200"),
///     AuthorizationHeader = "Basic BASE64(user:pass)",
///     IndexPattern = "netmetric-{date:yyyy.MM.dd}-{env}-{service}",
///     IngestPipeline = "metrics_ingest",
///     BatchSize = 500,
///     HttpTimeoutSeconds = 10,
///     MaxRetries = 3,
///     RetryBaseDelayMs = 300,
///     Environment = "staging",
///     ServiceName = "payment-worker",
///     MaxBulkBytes = 5 * 1024 * 1024,
///     LowercaseIndexNames = true
/// };
/// </code>
/// </example>
public sealed class ElasticExportOptions
{
    /// <summary>
    /// Gets or sets the Elasticsearch endpoint URI to which requests will be sent.
    /// </summary>
    /// <value>
    /// A valid absolute <see cref="Uri"/> such as <c>https://your-elastic:9200</c> or a managed Elastic Cloud endpoint.
    /// </value>
    /// <remarks>
    /// This value is required and must include the scheme (<c>http</c> or <c>https</c>).
    /// </remarks>
    public required Uri Endpoint { get; init; }

    /// <summary>
    /// Gets or sets the optional HTTP <c>Authorization</c> header value included with each request.
    /// </summary>
    /// <value>
    /// For Elastic Cloud API keys, use <c>ApiKey {base64(apiKeyId:apiKey)}</c>.
    /// For Basic authentication, use <c>Basic {base64(username:password)}</c>.
    /// </value>
    /// <remarks>
    /// Leave <see langword="null"/> to send requests without authentication (for example, when the cluster is secured by IP allowlists or mutual TLS).
    /// </remarks>
    public string? AuthorizationHeader { get; init; }

    /// <summary>
    /// Gets or sets the index name pattern used to build the target index at runtime.
    /// </summary>
    /// <value>
    /// Supports placeholders:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <c>{date:format}</c> formats the current UTC date using a standard or custom <c>format</c> string (for example, <c>yyyy.MM.dd</c>).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>{env}</c> is the environment name (for example, <c>dev</c>, <c>staging</c>, <c>prod</c>).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>{service}</c> is the logical service or application name.
    /// </description>
    /// </item>
    /// </list>
    /// The default pattern is <c>netmetric-{date:yyyy.MM.dd}</c>.
    /// </value>
    /// <remarks>
    /// If <see cref="Environment"/> or <see cref="ServiceName"/> are not provided, the exporter may resolve them
    /// from your metric configuration (for example, <c>MetricOptions.NmResource</c>) or omit them if unavailable.
    /// </remarks>
    /// <example>
    /// <code language="text">
    /// Example expansion:
    /// IndexPattern: "netmetric-{date:yyyy.MM.dd}-{env}-{service}"
    /// Environment:  "prod"
    /// ServiceName:  "orders-api"
    /// On 2025-09-02 (UTC), final index -> "netmetric-2025.09.02-prod-orders-api"
    /// </code>
    /// </example>
    public string IndexPattern { get; init; } = "netmetric-{date:yyyy.MM.dd}";

    /// <summary>
    /// Gets or sets the optional ingest pipeline to apply when indexing documents.
    /// </summary>
    /// <value>
    /// The ingest pipeline name, or <see langword="null"/> to skip pipeline processing.
    /// </value>
    /// <remarks>
    /// Use an ingest pipeline to enrich or transform metric documents before they are indexed.
    /// </remarks>
    public string? IngestPipeline { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of metric documents to include in a single bulk request.
    /// </summary>
    /// <value>
    /// An integer greater than zero. The default is <c>500</c>.
    /// </value>
    /// <remarks>
    /// Decrease this value if you encounter <c>413 Payload Too Large</c> or slow ingestion; increase it to improve throughput.
    /// The effective batch may be further limited by <see cref="MaxBulkBytes"/>.
    /// </remarks>
    public int BatchSize { get; init; } = 500;

    /// <summary>
    /// Gets or sets the HTTP timeout, in seconds, for Elasticsearch requests.
    /// </summary>
    /// <value>
    /// A positive integer. The default is <c>10</c> seconds.
    /// </value>
    /// <remarks>
    /// Applies per request. Consider increasing for geographically distant clusters or under heavy load.
    /// </remarks>
    public int HttpTimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// Gets or sets the maximum number of retries for transient failures.
    /// </summary>
    /// <value>
    /// A non-negative integer. The default is <c>3</c>.
    /// </value>
    /// <remarks>
    /// Retries typically use exponential backoff with jitter based on <see cref="RetryBaseDelayMs"/>.
    /// </remarks>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Gets or sets the base delay, in milliseconds, used to compute retry backoff.
    /// </summary>
    /// <value>
    /// A non-negative integer. The default is <c>300</c> ms.
    /// </value>
    /// <remarks>
    /// The exporter applies jitter to reduce thundering herd effects across instances.
    /// </remarks>
    public int RetryBaseDelayMs { get; init; } = 300;

    /// <summary>
    /// Gets or sets the environment token used when expanding <c>{env}</c> in <see cref="IndexPattern"/>.
    /// </summary>
    /// <value>
    /// A short environment label (for example, <c>dev</c>, <c>staging</c>, <c>prod</c>), or <see langword="null"/> to let the exporter
    /// infer it from your metric configuration (for example, <c>MetricOptions.NmResource</c>) if available.
    /// </value>
    public string? Environment { get; init; }

    /// <summary>
    /// Gets or sets the service name token used when expanding <c>{service}</c> in <see cref="IndexPattern"/>.
    /// </summary>
    /// <value>
    /// A logical service or application name (for example, <c>orders-api</c>), or <see langword="null"/> to let the exporter
    /// infer it from your metric configuration (for example, <c>MetricOptions.NmResource</c>) if available.
    /// </value>
    public string? ServiceName { get; init; }

    /// <summary>
    /// Gets or sets the maximum allowed bulk request size in bytes.
    /// </summary>
    /// <value>
    /// A non-negative 64-bit integer. The default is 5 MiB (<c>5 * 1024 * 1024</c>).
    /// A value of <c>0</c> or negative disables the size limit.
    /// </value>
    /// <remarks>
    /// This limit is applied in addition to <see cref="BatchSize"/> to prevent large payloads that may be rejected by the cluster
    /// or intermediate proxies or load balancers.
    /// </remarks>
    public long MaxBulkBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>
    /// Gets or sets a value indicating whether the finalized index name should be normalized to lowercase.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to convert the computed index name using <see cref="string.ToLowerInvariant()"/>;
    /// otherwise, <see langword="false"/>. The default is <see langword="true"/>.
    /// </value>
    /// <remarks>
    /// Many Elasticsearch clusters enforce lowercase index names. Leave enabled unless you have a specific reason to preserve case.
    /// </remarks>
    public bool LowercaseIndexNames { get; init; } = true;
}
