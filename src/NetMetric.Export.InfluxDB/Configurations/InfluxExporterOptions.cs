// <copyright file="InfluxExporterOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.InfluxDB.Configurations;

/// <summary>
/// Provides configuration options for the InfluxDB v2 write API (<c>/api/v2/write</c>).
/// </summary>
/// <remarks>
/// <para>
/// These options control batching, retry policies, compression, and target connection settings
/// when publishing metrics to an InfluxDB v2 instance.
/// </para>
/// <para>
/// Default values aim to balance throughput and reliability. Tune them based on workload, network
/// conditions, and the capacity limits of your InfluxDB cluster.
/// </para>
/// <para>
/// The options are typically bound via <c>IOptions&lt;InfluxExporterOptions&gt;</c> from configuration (e.g., <c>appsettings.json</c>),
/// and an <see cref="HttpClient"/> named by <see cref="HttpClientName"/> is used for outbound requests.
/// </para>
/// </remarks>
/// <example>
/// Example configuration in <c>appsettings.json</c>:
/// <code language="json"><![CDATA[
/// {
///   "NetMetric": {
///     "Export": {
///       "InfluxDB": {
///         "BaseAddress": "https://influx.example.com",
///         "Org": "my-org",
///         "Bucket": "metrics",
///         "Token": "my-secret-token",
///         "Precision": "ns",
///         "BatchSize": 500,
///         "MaxBatchBytes": 1048576,
///         "FlushInterval": "00:00:10",
///         "MaxInFlight": 4,
///         "MaxRetries": 5,
///         "BaseRetryDelay": "00:00:00.200",
///         "HttpClientName": "NetMetric.InfluxDB",
///         "EnableGzip": true,
///         "MinGzipSizeBytes": 8192
///       }
///     }
///   }
/// }
/// ]]></code>
///
/// Example registration with named <see cref="HttpClient"/>:
/// <code language="csharp"><![CDATA[
/// services.AddHttpClient("NetMetric.InfluxDB", c =>
/// {
///     // BaseAddress is set per-request from options; you can set defaults here if desired.
///     c.Timeout = TimeSpan.FromSeconds(30);
/// });
///
/// services.Configure<InfluxExporterOptions>(configuration.GetSection("NetMetric:Export:InfluxDB"));
/// ]]></code>
/// </example>
/// <seealso cref="HttpClient"/>
public sealed class InfluxExporterOptions
{
    /// <summary>
    /// Gets the base URI of the InfluxDB server, including scheme and host.
    /// </summary>
    /// <value>Example: <c>https://influx.example.com</c>.</value>
    public required Uri BaseAddress { get; init; }

    /// <summary>
    /// Gets the InfluxDB organization identifier or name.
    /// </summary>
    public required string Org { get; init; }

    /// <summary>
    /// Gets the bucket (database-like container) into which metrics are written.
    /// </summary>
    public required string Bucket { get; init; }

    /// <summary>
    /// Gets the API token used to authenticate with InfluxDB.
    /// </summary>
    /// <remarks>
    /// Treat as a secret. Prefer secure configuration providers (environment variables,
    /// secret stores, managed identities) and avoid logging this value.
    /// </remarks>
    public required string Token { get; init; }

    /// <summary>
    /// Gets the timestamp precision for line protocol writes. Defaults to <c>"ns"</c> (nanoseconds).
    /// </summary>
    /// <remarks>
    /// Supported values: <c>ns</c>, <c>us</c>, <c>ms</c>, <c>s</c>.
    /// Choose the coarsest precision that meets your requirements to reduce payload size.
    /// </remarks>
    public string Precision { get; init; } = "ns";

    /// <summary>
    /// Gets the maximum number of points per batch before a flush is triggered. Default is <c>1000</c>.
    /// </summary>
    /// <remarks>
    /// Higher values improve throughput at the cost of latency and memory pressure.
    /// </remarks>
    public int BatchSize { get; init; } = 1000;

    /// <summary>
    /// Gets the maximum payload size (in bytes) for a batch before a flush occurs. Default is <c>1,048,576</c> (1 MiB).
    /// </summary>
    /// <remarks>
    /// This limit prevents oversized requests that may be rejected by proxies or the server.
    /// </remarks>
    public int MaxBatchBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// Gets the interval after which pending metrics are automatically flushed even if the batch size has not been reached.
    /// Default is <see cref="TimeSpan.FromSeconds(double)"/> with a value of 5 seconds.
    /// </summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the maximum number of concurrent in-flight HTTP write requests. Default is <c>2</c>.
    /// </summary>
    /// <remarks>
    /// Increasing this can improve throughput on high-latency links but may increase backpressure on the server.
    /// </remarks>
    public int MaxInFlight { get; init; } = 2;

    /// <summary>
    /// Gets the maximum number of retry attempts for failed HTTP requests. Default is <c>3</c>.
    /// </summary>
    /// <remarks>
    /// Retries typically apply to transient failures (e.g., 5xx, timeouts, or <c>429 Too Many Requests</c>).
    /// </remarks>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Gets the base delay used for exponential backoff when retrying failed requests.
    /// Default is <see cref="TimeSpan.FromMilliseconds(double)"/> with a value of 200 ms.
    /// </summary>
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets the name of the <see cref="HttpClient"/> to use for requests. Default is <c>"NetMetric.InfluxDB"</c>.
    /// </summary>
    public string HttpClientName { get; init; } = "NetMetric.InfluxDB";

    /// <summary>
    /// Gets a value indicating whether Gzip compression should be enabled for HTTP request bodies.
    /// Default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Compression is most beneficial when line protocol payloads are large and compressible.
    /// </remarks>
    public bool EnableGzip { get; init; } = true;

    /// <summary>
    /// Gets the minimum request payload size (in bytes) before Gzip compression is applied. Default is <c>8192</c> (8 KiB).
    /// </summary>
    /// <remarks>
    /// Below this threshold, compression overhead can outweigh benefits.
    /// </remarks>
    public int MinGzipSizeBytes { get; init; } = 8 * 1024;
}
