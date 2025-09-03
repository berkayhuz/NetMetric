// <copyright file="ElasticExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace NetMetric.Export.Elastic.Exporters;

/// <summary>
/// Exports <see cref="IMetric"/> instances to Elasticsearch in bulk.
/// </summary>
/// <remarks>
/// <para>
/// The exporter maps incoming metrics into Elasticsearch bulk (NDJSON) lines using
/// <see cref="IElasticDocumentMapper"/> and sends them via <see cref="ElasticBulkClient"/>. It
/// respects both item-based batching and a byte-size ceiling to keep individual bulk requests
/// within the configured limits (see <see cref="ElasticExportOptions.BatchSize"/> and
/// <see cref="ElasticExportOptions.MaxBulkBytes"/>).
/// </para>
/// <para>
/// <b>Index naming.</b> The target index is resolved once per export operation using
/// <see cref="ElasticExportOptions.IndexPattern"/>. The pattern supports placeholders such as:
/// <list type="bullet">
///   <item><description><c>{date:FORMAT}</c> — replaced with the UTC time of the export using a .NET date/time format string (e.g., <c>{date:yyyy.MM.dd}</c>).</description></item>
///   <item><description><c>{env}</c> — replaced with <see cref="ElasticExportOptions.Environment"/> or, if not set, <c>MetricOptions.NmResource.DeploymentEnvironment</c>; falls back to <c>"default"</c>.</description></item>
///   <item><description><c>{service}</c> — replaced with <see cref="ElasticExportOptions.ServiceName"/> or, if not set, <c>MetricOptions.NmResource.ServiceName</c>; falls back to <c>"app"</c>.</description></item>
/// </list>
/// If <see cref="ElasticExportOptions.LowercaseIndexNames"/> is enabled, the resolved index name is converted to lowercase for Elasticsearch compatibility.
/// </para>
/// <para>
/// <b>Payload sizing.</b> The exporter accumulates interleaved bulk <em>action</em> and <em>document</em>
/// lines and accounts for the newline delimiter when estimating the request size (UTF-8 bytes for each line plus one byte for <c>'\n'</c>).
/// When <see cref="ElasticExportOptions.MaxBulkBytes"/> is positive, the exporter ensures no single request exceeds that limit.
/// </para>
/// <para>
/// <b>Thread-safety.</b> Instances of <see cref="ElasticExporter"/> are intended to be resolved from DI
/// with a single <see cref="ElasticBulkClient"/> and may be used concurrently from multiple callers.
/// </para>
/// <para>
/// <b>Trimming/Ahead-of-Time (AOT).</b> The exporter is annotated with
/// <see cref="RequiresUnreferencedCodeAttribute"/> because downstream exporters or mappers may use
/// reflection. If you enable trimming or AOT, ensure required members are preserved.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // In Startup/Program.cs
/// services.AddOptions<ElasticExportOptions>()
///         .BindConfiguration("NetMetric:Elastic")
///         .ValidateDataAnnotations()
///         .ValidateOnStart();
///
/// services.AddOptions<MetricOptions>()
///         .BindConfiguration("NetMetric:Metrics");
///
/// services.AddSingleton<ElasticBulkClient>();
/// services.AddSingleton<IElasticDocumentMapper, DefaultElasticDocumentMapper>();
/// services.AddSingleton<IMetricExporter, ElasticExporter>();
///
/// // Using the exporter
/// var exporter = provider.GetRequiredService<IMetricExporter>();
/// IReadOnlyCollection<IMetric> batch = CollectMetrics();
/// await exporter.ExportAsync(batch, ct);
/// ]]></code>
/// </example>
public sealed class ElasticExporter : IMetricExporter
{
    private readonly ElasticBulkClient _client;
    private readonly ElasticExportOptions _opt;
    private readonly IOptions<MetricOptions> _metricOpts;
    private readonly IElasticDocumentMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticExporter"/> class with the required dependencies and options.
    /// </summary>
    /// <param name="client">The low-level bulk client used to send NDJSON lines to Elasticsearch.</param>
    /// <param name="opt">The exporter configuration options.</param>
    /// <param name="metricOpts">Metric-level options, used for resource metadata (e.g., service/environment) when resolving the index name.</param>
    /// <param name="mapper">Maps an <see cref="IMetric"/> into the corresponding bulk action and document lines.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/>, <paramref name="opt"/>, <paramref name="metricOpts"/>, or <paramref name="mapper"/> is <see langword="null"/>.</exception>
    public ElasticExporter(ElasticBulkClient client, IOptions<ElasticExportOptions> opt, IOptions<MetricOptions> metricOpts, IElasticDocumentMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(opt);
        ArgumentNullException.ThrowIfNull(metricOpts);
        ArgumentNullException.ThrowIfNull(mapper);

        _client = client;
        _opt = opt.Value;
        _metricOpts = metricOpts;
        _mapper = mapper;
    }

    /// <summary>
    /// Exports the specified metrics to Elasticsearch using the bulk API.
    /// </summary>
    /// <param name="metrics">
    /// The sequence of metrics to export. If <see langword="null"/>, the call is treated as an empty batch and returns immediately.
    /// </param>
    /// <param name="ct">A token to observe while waiting for the export to complete.</param>
    /// <returns>A <see cref="Task"/> that completes when the bulk requests have been sent.</returns>
    /// <remarks>
    /// <para>
    /// Metrics are processed into interleaved bulk action/document lines by <see cref="IElasticDocumentMapper"/>.
    /// The exporter groups lines into requests that satisfy:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see cref="ElasticExportOptions.BatchSize"/> — a hint for the number of metrics per request when no byte limit is set.</description></item>
    ///   <item><description><see cref="ElasticExportOptions.MaxBulkBytes"/> — a hard limit (in bytes) for a single bulk request. When positive, this takes precedence.</description></item>
    /// </list>
    /// <para>
    /// If a single metric maps to bulk lines whose size alone exceeds <see cref="ElasticExportOptions.MaxBulkBytes"/>,
    /// the exporter sends that metric as its own request to avoid blocking the pipeline.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    [RequiresUnreferencedCode("IMetricExporter.ExportAsync may use reflection or members trimmed at AOT; keep members or disable trimming for exporters.")]
    public async Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        var arr = metrics?.ToArray() ?? Array.Empty<IMetric>();

        if (arr.Length == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var index = ResolveIndexName(now);

        var batchSizeHint = Math.Max(1, _opt.BatchSize);
        var maxBytes = _opt.MaxBulkBytes;

        var lines = new List<string>(batchSizeHint * 2);
        var byteCount = 0L;

        foreach (var m in arr)
        {
            ct.ThrowIfCancellationRequested();

            var mapped = _mapper.Map(m, now, index);
            var addedBytes = GetNdjsonBytes(mapped);

            if (maxBytes > 0 && addedBytes > maxBytes && lines.Count == 0)
            {
                await _client.SendBulkAsync(index, mapped.ToArray(), ct).ConfigureAwait(false);

                continue;
            }

            if (maxBytes > 0 && byteCount > 0 && (byteCount + addedBytes) > maxBytes)
            {
                await _client.SendBulkAsync(index, lines.ToArray(), ct).ConfigureAwait(false);

                lines.Clear();

                byteCount = 0L;
            }

            lines.AddRange(mapped);
            byteCount += addedBytes;

            if (maxBytes <= 0 && lines.Count >= batchSizeHint * 2)
            {
                await _client.SendBulkAsync(index, lines.ToArray(), ct).ConfigureAwait(false);

                lines.Clear();

                byteCount = 0L;
            }
        }

        if (lines.Count > 0)
        {
            await _client.SendBulkAsync(index, lines.ToArray(), ct).ConfigureAwait(false);
        }
    }

    private static long GetNdjsonBytes(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        long total = 0;

        foreach (var l in lines)
        {
            total += Encoding.UTF8.GetByteCount(l) + 1;
        }

        return total;
    }

    private string ResolveIndexName(DateTime utcNow)
    {
        var pat = _opt.IndexPattern ?? "netmetric-{date:yyyy.MM.dd}";

        var env = _opt.Environment ?? _metricOpts.Value.NmResource?.DeploymentEnvironment;
        var svc = _opt.ServiceName ?? _metricOpts.Value.NmResource?.ServiceName;

        pat = ReplaceDatePlaceholders(pat, utcNow);
        pat = pat.Replace("{env}", string.IsNullOrWhiteSpace(env) ? "default" : env, StringComparison.Ordinal);
        pat = pat.Replace("{service}", string.IsNullOrWhiteSpace(svc) ? "app" : svc, StringComparison.Ordinal);

        if (_opt.LowercaseIndexNames)
        {
            pat = pat.ToUpperInvariant();
        }

        return pat;
    }

    private static string ReplaceDatePlaceholders(string pattern, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        const string token = "{date:";
        int start = pattern.IndexOf(token, StringComparison.Ordinal);

        while (start >= 0)
        {
            int end = pattern.IndexOf('}', start);

            if (end < 0)
            {
                break;
            }

            var fmtSpan = pattern.AsSpan(start + token.Length, end - (start + token.Length));
            string formatted;

            try
            {
                formatted = utcNow.ToString(fmtSpan.ToString());
            }
            catch (FormatException)
            {
                formatted = utcNow.ToString("yyyy.MM.dd");
            }

            pattern = string.Concat(pattern.AsSpan(0, start), formatted, pattern.AsSpan(end + 1));
            start = pattern.IndexOf(token, StringComparison.Ordinal);
        }

        pattern = pattern.Replace("{date:yyyy.MM.dd}", utcNow.ToString("yyyy.MM.dd"), StringComparison.Ordinal);

        return pattern;
    }
}
