// <copyright file="ElasticBulkClient.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace NetMetric.Export.Elastic.Internal;

/// <summary>
/// Sends NDJSON bulk metric documents to Elasticsearch via the HTTP Bulk API (<c>_bulk</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this client does.</b> <see cref="ElasticBulkClient"/> builds a newline-delimited JSON (NDJSON)
/// payload and posts it to the target index's <c>_bulk</c> endpoint. It applies transient-failure
/// handling with exponential backoff and cryptographic jitter, and it honors an optional
/// <c>Authorization</c> header (e.g., Bearer or Basic) sourced from
/// <see cref="NetMetric.Export.Elastic.Configurations.ElasticExportOptions"/>.
/// </para>
/// <para>
/// <b>Reliability model.</b> The retry loop treats HTTP 5xx, <see cref="HttpStatusCode.RequestTimeout"/>,
/// and <c>429 Too Many Requests</c> as transient. For each retry, the delay is doubled and a 0–100 ms jitter
/// is added using <see cref="RandomNumberGenerator"/> to reduce thundering herds. The request body is rebuilt
/// once per attempt to avoid accidental reuse of disposed content.
/// </para>
/// <para>
/// <b>NDJSON format.</b> Elasticsearch expects newline-delimited action and source lines. 
/// This client appends a trailing newline to the final payload. Callers should pass each 
/// pre-serialized NDJSON line (without trailing newline) as elements in the collection 
/// provided to <c>SendBulkAsync</c>.
/// </para>
/// <para>
/// <b>Thread-safety.</b> The instance is safe for concurrent use as long as the provided
/// <see cref="HttpClient"/> is safe to use concurrently (which it is by design).
/// </para>
/// <para>
/// <b>Idempotency.</b> Bulk indexing is not guaranteed to be idempotent. If your pipeline or ingest logic is
/// not idempotent, consider using deterministic document IDs or external versioning to avoid duplicates on retry.
/// </para>
/// </remarks>
/// <example>
/// The following example shows how to register and use <see cref="ElasticBulkClient"/> with
/// <see cref="IOptions{TOptions}"/> and <c>IHttpClientFactory</c>.
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using Microsoft.Extensions.Options;
/// using NetMetric.Export.Elastic.Configurations;
/// using NetMetric.Export.Elastic.Internal;
///
/// var services = new ServiceCollection();
///
/// services.AddHttpClient("elastic")
///         .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
///
/// services.Configure<ElasticExportOptions>(o =>
/// {
///     o.Endpoint = new Uri("https://your-elastic:9200/");
///     o.AuthorizationHeader = "Bearer <token>"; // or "Basic base64(user:pass)"
///     o.MaxRetries = 3;
///     o.RetryBaseDelayMs = 200;
///     o.IngestPipeline = "metrics-pipeline";
/// });
///
/// var sp = services.BuildServiceProvider();
/// var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("elastic");
/// var opt = sp.GetRequiredService<IOptions<ElasticExportOptions>>();
///
/// var client = new ElasticBulkClient(http, opt);
///
/// // Prepare NDJSON lines (no trailing newline needed; the client will add one)
/// var lines = new[]
/// {
///     "{\"index\":{\"_index\":\"metrics-2025.09.02\"}}",
///     "{\"@timestamp\":\"2025-09-02T12:34:56Z\",\"name\":\"cpu.util\",\"value\":0.42}\"
/// };
///
/// await client.SendBulkAsync("metrics-2025.09.02", lines, CancellationToken.None);
/// ]]></code>
/// </example>

public sealed class ElasticBulkClient
{
    private readonly HttpClient _http;
    private readonly NetMetric.Export.Elastic.Configurations.ElasticExportOptions _opt;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticBulkClient"/> class.
    /// </summary>
    /// <param name="http">The <see cref="HttpClient"/> used to send requests to Elasticsearch.</param>
    /// <param name="opt">The configuration options (<see cref="IOptions{TOptions}"/>) for the exporter.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="http"/> or <paramref name="opt"/> is <see langword="null"/>.
    /// </exception>
    public ElasticBulkClient(HttpClient http, IOptions<NetMetric.Export.Elastic.Configurations.ElasticExportOptions> opt)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(opt);

        _http = http;
        _opt = opt.Value;
    }

    /// <summary>
    /// Sends an NDJSON bulk request to the Elasticsearch <c>_bulk</c> API for the specified index.
    /// Applies exponential backoff with jitter for transient failures.
    /// </summary>
    /// <param name="indexName">
    /// The target Elasticsearch index name. If an ingest pipeline is configured in options,
    /// it will be applied to this request.
    /// </param>
    /// <param name="ndjsonLines">
    /// The NDJSON lines (one action or source per element) to send. The client appends a final newline.
    /// </param>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe while awaiting the request.</param>
    /// <returns>A task that completes when the bulk request has succeeded or throws on failure.</returns>
    /// <remarks>
    /// <para>
    /// Transient errors considered for retry include:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Any HTTP status in the 5xx range.</description></item>
    /// <item><description><see cref="HttpStatusCode.RequestTimeout"/>.</description></item>
    /// <item><description><c>429 Too Many Requests</c>.</description></item>
    /// </list>
    /// <para>
    /// The initial delay is <c>RetryBaseDelayMs</c> and doubles on each attempt, plus a 0–100 ms secure jitter.
    /// Retries stop after <c>MaxRetries</c> attempts, at which point a <see cref="HttpRequestException"/> is thrown.
    /// </para>
    /// </remarks>
    /// <exception cref="HttpRequestException">
    /// Thrown when the server returns a non-transient error or when all retry attempts are exhausted.
    /// The exception message includes the HTTP status, reason phrase, and response body (if available).
    /// </exception>
    /// <exception cref="TaskCanceledException">
    /// Thrown if the request is canceled via <paramref name="ct"/> or the <see cref="HttpClient"/> times out.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown if the underlying <see cref="HttpClient"/> has been disposed.
    /// </exception>
    public async Task SendBulkAsync(string indexName, ReadOnlyMemory<string> ndjsonLines, CancellationToken ct)
    {
        var uri = BuildBulkUri(indexName);

        var payload = string.Join('\n', ndjsonLines.ToArray()) + "\n";

        var delay = _opt.RetryBaseDelayMs;

        for (int attempt = 0; ; attempt++)
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/x-ndjson");
            using var req = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };

            if (!string.IsNullOrWhiteSpace(_opt.AuthorizationHeader))
            {
                req.Headers.Authorization = AuthenticationHeaderValue.Parse(_opt.AuthorizationHeader);
            }

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if ((int)resp.StatusCode < 500 &&
                resp.StatusCode is not (HttpStatusCode.RequestTimeout or (HttpStatusCode)429))
            {
                resp.EnsureSuccessStatusCode();
                return;
            }

            if (attempt >= _opt.MaxRetries)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException($"Elastic bulk failed [{(int)resp.StatusCode}] {resp.ReasonPhrase}: {body}");
            }

            // Cryptographically secure jitter (0–100 ms)
            var jitter = RandomNumberGenerator.GetInt32(0, 101);

            await Task.Delay(delay + jitter, ct).ConfigureAwait(false);

            delay *= 2;
        }
    }

    /// <summary>
    /// Builds the Elasticsearch Bulk API URI for <paramref name="indexName"/>, optionally attaching the configured ingest pipeline.
    /// </summary>
    /// <param name="indexName">The index to which the bulk request will be sent.</param>
    /// <returns>A <see cref="Uri"/> pointing to <c>/{indexName}/_bulk</c> (with <c>?pipeline=...</c> if configured).</returns>
    private Uri BuildBulkUri(string indexName)
    {
        var baseUri = _opt.Endpoint;

        var path = string.IsNullOrWhiteSpace(_opt.IngestPipeline)
            ? $"/{indexName}/_bulk"
            : $"/{indexName}/_bulk?pipeline={Uri.EscapeDataString(_opt.IngestPipeline)}";

        return new Uri(baseUri, path);
    }
}
