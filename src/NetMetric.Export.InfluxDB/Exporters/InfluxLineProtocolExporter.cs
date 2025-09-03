// <copyright file="InfluxLineProtocolExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace NetMetric.Export.InfluxDB.Exporters;

/// <summary>
/// Exports NetMetric metrics to InfluxDB v2's <c>/api/v2/write</c> endpoint using the Influx Line Protocol (ILP).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this exporter does.</b> It converts a sequence of <see cref="NetMetric.Abstractions.IMetric"/>
/// values into ILP lines, batches them by line count and/or byte size, and POSTs each batch to InfluxDB.
/// It supports optional gzip compression for large payloads and includes resilient retry handling for
/// transient failures.
/// </para>
/// <para>
/// <b>Key features.</b>
/// </para>
/// <list type="bullet">
///   <item><description>Batching by maximum line count and/or maximum byte size.</description></item>
///   <item><description>Concurrent dispatch of multiple in-flight HTTP requests.</description></item>
///   <item><description>Exponential backoff with jitter on <c>429 Too Many Requests</c> and HTTP <c>5xx</c> responses.</description></item>
///   <item><description>Request-scoped <c>Authorization: Token &lt;token&gt;</c> header for InfluxDB authentication.</description></item>
///   <item><description>Optional <c>Content-Encoding: gzip</c> for payloads over a configurable threshold.</description></item>
/// </list>
/// <para>
/// <b>Thread safety.</b> Instances are intended to be used as singletons and are thread-safe for concurrent
/// calls to <see cref="ExportAsync(System.Collections.Generic.IEnumerable{NetMetric.Abstractions.IMetric}, System.Threading.CancellationToken)"/>.
/// Internal batching uses local buffers per call.
/// </para>
/// <para>
/// <b>Configuration.</b> Behavior is driven by <see cref="NetMetric.Export.InfluxDB.Configurations.InfluxExporterOptions"/>,
/// including endpoint base address, organization, bucket, timestamp precision, batching and compression thresholds,
/// retry policy, and the name of the configured <see cref="System.Net.Http.IHttpClientFactory"/> client.
/// </para>
/// <para>
/// <b>Security.</b> The exporter sends an InfluxDB API token in the <c>Authorization</c> header. Protect this value and
/// prefer secure configuration sources (e.g., environment variables, secret stores). Avoid logging request bodies
/// and headers in production.
/// </para>
/// <para>
/// <b>Performance notes.</b> For high-throughput scenarios, prefer larger batch sizes and enable gzip compression when
/// payloads commonly exceed the threshold. Ensure the <see cref="System.Net.Http.HttpClient"/> created by
/// <see cref="System.Net.Http.IHttpClientFactory"/> is configured with appropriate timeouts and connection limits.
/// </para>
/// <example>
/// The following example shows how to register and use the exporter:
/// <code language="csharp"><![CDATA[
/// services.AddHttpClient("influx"); // Named HttpClient configuration
/// services.Configure<InfluxExporterOptions>(cfg =>
/// {
///     cfg.BaseAddress = new Uri("https://influxdb.example.com/");
///     cfg.Org = "my-org";
///     cfg.Bucket = "my-bucket";
///     cfg.Precision = "ns";
///     cfg.Token = Environment.GetEnvironmentVariable("INFLUXDB_TOKEN");
///     cfg.BatchSize = 500;                 // up to 500 lines per batch
///     cfg.MaxBatchBytes = 256 * 1024;      // or 256 KiB per batch, whichever hits first
///     cfg.EnableGzip = true;
///     cfg.MinGzipSizeBytes = 8 * 1024;
///     cfg.HttpClientName = "influx";
///     cfg.MaxRetries = 5;
///     cfg.BaseRetryDelay = TimeSpan.FromMilliseconds(200);
/// });
///
/// var exporter = services.BuildServiceProvider()
///                        .GetRequiredService<InfluxLineProtocolExporter>();
///
/// await exporter.ExportAsync(metrics, CancellationToken.None);
/// ]]></code>
/// </example>
/// </remarks>
/// <seealso cref="NetMetric.Export.InfluxDB.Configurations.InfluxExporterOptions"/>
/// <seealso cref="NetMetric.Export.InfluxDB.Internal.InfluxLineProtocolFormatter"/>
/// <seealso cref="NetMetric.Export.InfluxDB.Internal.RetryPolicy"/>
public sealed class InfluxLineProtocolExporter : IMetricExporter
{
    private readonly System.Net.Http.IHttpClientFactory _httpFactory;
    private readonly ITimeProvider _clock;
    private readonly NetMetric.Export.InfluxDB.Configurations.InfluxExporterOptions _opts;

    /// <summary>
    /// Initializes a new instance of the <see cref="InfluxLineProtocolExporter"/> class.
    /// </summary>
    /// <param name="httpFactory">The factory used to create named <see cref="System.Net.Http.HttpClient"/> instances.</param>
    /// <param name="options">The strongly typed options providing exporter configuration.</param>
    /// <param name="clock">
    /// An optional time provider used to obtain UTC timestamps for metrics; if <see langword="null"/>,
    /// a default <see cref="UtcTimeProvider"/> is used.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown if <paramref name="httpFactory"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public InfluxLineProtocolExporter(
        System.Net.Http.IHttpClientFactory httpFactory,
        IOptions<NetMetric.Export.InfluxDB.Configurations.InfluxExporterOptions> options,
        ITimeProvider? clock = null)
    {
        _httpFactory = httpFactory ?? throw new System.ArgumentNullException(nameof(httpFactory));
        _opts = options?.Value ?? throw new System.ArgumentNullException(nameof(options));
        _clock = clock ?? new UtcTimeProvider();
    }

    /// <summary>
    /// Converts the provided metrics into Influx Line Protocol and writes them to InfluxDB in batches.
    /// </summary>
    /// <param name="metrics">The sequence of <see cref="NetMetric.Abstractions.IMetric"/> values to export.</param>
    /// <param name="ct">A token that can be used to cancel the export operation.</param>
    /// <returns>
    /// A <see cref="System.Threading.Tasks.Task"/> that completes when all batches have been sent and acknowledged
    /// by InfluxDB or an error occurs.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="metrics"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">
    /// Thrown if a request ultimately fails after the configured number of retries.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown if the operation is canceled via <paramref name="ct"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method is <i>allocation-conscious</i> and uses a local <see cref="System.Text.StringBuilder"/> to build ILP lines.
    /// Batches are formed when either the line count or byte threshold is reached (based on
    /// <see cref="NetMetric.Export.InfluxDB.Configurations.InfluxExporterOptions.BatchSize"/> and
    /// <see cref="NetMetric.Export.InfluxDB.Configurations.InfluxExporterOptions.MaxBatchBytes"/>).
    /// </para>
    /// <para>
    /// When both thresholds are set, whichever is hit first triggers a batch flush. Each flushed batch is
    /// dispatched using <see cref="SendOneAsync(System.Net.Http.HttpClient, string, System.Threading.CancellationToken)"/>.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("IMetricExporter.ExportAsync may use reflection or members trimmed at AOT; keep members or disable trimming for exporters.")]
    public async System.Threading.Tasks.Task ExportAsync(
        System.Collections.Generic.IEnumerable<NetMetric.Abstractions.IMetric> metrics,
        System.Threading.CancellationToken ct = default)
    {
        System.ArgumentNullException.ThrowIfNull(metrics);

        var nowUtc = _clock.UtcNow;
        var sb = new System.Text.StringBuilder(capacity: 32 * 1024);
        var batches = new System.Collections.Generic.List<string>();

        // Local helper to finalize a batch.
        void FlushBatch()
        {
            if (sb.Length > 0)
            {
                batches.Add(sb.ToString());
                sb.Clear();
            }
        }

        foreach (var m in metrics)
        {
            ct.ThrowIfCancellationRequested();

            var before = sb.Length;
            NetMetric.Export.InfluxDB.Internal.InfluxLineProtocolFormatter.AppendMetricLine(sb, m, nowUtc, _opts.Precision);

            if (_opts.MaxBatchBytes > 0 && sb.Length >= _opts.MaxBatchBytes)
            {
                // If a single metric pushes us over the byte limit, flush previous content first.
                if (before > 0)
                {
                    var tailLen = sb.Length - before;
                    var tail = sb.ToString(before, tailLen);

                    sb.Length = before;

                    FlushBatch();

                    sb.Append(tail);
                }
                FlushBatch();
            }
            else if (_opts.BatchSize > 0 && CountLines(sb) >= _opts.BatchSize)
            {
                FlushBatch();
            }
        }
        FlushBatch();

        if (batches.Count == 0)
        {
            return;
        }

        var client = _httpFactory.CreateClient(_opts.HttpClientName);
        client.BaseAddress ??= _opts.BaseAddress;

        // Dispatch all batches concurrently.
        var tasks = batches.Select(batch => SendOneAsync(client, batch, ct)).ToArray();

        await System.Threading.Tasks.Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a single ILP batch to InfluxDB with retry logic applied for transient failures.
    /// </summary>
    /// <param name="client">The <see cref="System.Net.Http.HttpClient"/> used to send the request.</param>
    /// <param name="body">The ILP payload representing one batch of metric lines.</param>
    /// <param name="ct">A cancellation token for the outgoing request.</param>
    /// <returns>A task that completes when the request succeeds or fails definitively.</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">
    /// Thrown when the response indicates failure after retries, including the response error body (if present).
    /// </exception>
    /// <remarks>
    /// This method constructs the <c>/api/v2/write</c> URL with <c>org</c>, <c>bucket</c>, and <c>precision</c> query
    /// parameters, sets the <c>Authorization</c> header using the configured InfluxDB token, and posts the batch
    /// as either <c>text/plain</c> or <c>gzip</c>-encoded content depending on configuration.
    /// </remarks>
    private async System.Threading.Tasks.Task SendOneAsync(
        System.Net.Http.HttpClient client,
        string body,
        System.Threading.CancellationToken ct)
    {
        var requestModel = new NetMetric.Export.InfluxDB.Internal.InfluxWriteRequest(_opts.Org, _opts.Bucket, _opts.Precision, body);

        var resp = await NetMetric.Export.InfluxDB.Internal.RetryPolicy.SendWithRetryAsync(async innerCt =>
        {
            var url =
                $"/api/v2/write?org={System.Uri.EscapeDataString(requestModel.Org)}" +
                $"&bucket={System.Uri.EscapeDataString(requestModel.Bucket)}" +
                $"&precision={requestModel.Precision}";

            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
            {
                Content = CreateContent(body)
            };

            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", _opts.Token);
            req.Headers.UserAgent.ParseAdd("NetMetric.InfluxExporter/1.0");

            return await client.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, innerCt)
                               .ConfigureAwait(false);
        }, _opts.MaxRetries, _opts.BaseRetryDelay, ct).ConfigureAwait(false);

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var error = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new System.Net.Http.HttpRequestException($"Influx write failed {(int)resp.StatusCode}: {error}");
            }
        }
    }

    /// <summary>
    /// Creates HTTP content for a batch payload, optionally compressing with gzip when enabled and large enough.
    /// </summary>
    /// <param name="body">The raw, newline-delimited ILP string for the batch.</param>
    /// <returns>
    /// A <see cref="System.Net.Http.HttpContent"/> instance containing either uncompressed UTF-8 text
    /// (<see cref="System.Net.Http.StringContent"/>) or a <see cref="System.Net.Http.StreamContent"/> with
    /// <c>Content-Encoding: gzip</c>.
    /// </returns>
    /// <remarks>
    /// Compression is controlled by <see cref="NetMetric.Export.InfluxDB.Configurations.InfluxExporterOptions.EnableGzip"/>
    /// and <see cref="NetMetric.Export.InfluxDB.Configurations.InfluxExporterOptions.MinGzipSizeBytes"/>.
    /// </remarks>
    private System.Net.Http.HttpContent CreateContent(string body)
    {
        if (_opts.EnableGzip)
        {
            // Use raw text if below threshold.
            var rawBytes = System.Text.Encoding.UTF8.GetBytes(body);
            if (rawBytes.Length >= _opts.MinGzipSizeBytes)
            {
                var ms = new System.IO.MemoryStream(capacity: Math.Min(rawBytes.Length, 1_048_576));

                using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                    gz.Write(rawBytes, 0, rawBytes.Length);

                ms.Position = 0;

                var content = new System.Net.Http.StreamContent(ms);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
                content.Headers.ContentEncoding.Add("gzip");

                return content;
            }
        }

        // Fallback: uncompressed text.
        return new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "text/plain");
    }

    /// <summary>
    /// Counts the number of ILP lines in the given buffer by scanning for newline characters.
    /// </summary>
    /// <param name="sb">The buffer that currently holds ILP content.</param>
    /// <returns>The number of newline-terminated lines contained in <paramref name="sb"/>.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="sb"/> is <see langword="null"/>.</exception>
    private static int CountLines(System.Text.StringBuilder sb)
    {
        System.ArgumentNullException.ThrowIfNull(sb);

        var n = 0;

        for (int i = 0; i < sb.Length; i++)
        {
            if (sb[i] == '\n')
            {
                n++;
            }
        }

        return n;
    }
}
