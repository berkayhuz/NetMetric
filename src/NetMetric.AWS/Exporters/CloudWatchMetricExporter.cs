// <copyright file="CloudWatchMetricExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.Runtime;

namespace NetMetric.AWS.Exporters;

/// <summary>
/// Metric exporter that sends metrics to Amazon CloudWatch using <c>PutMetricData</c>.  
/// Units are resolved from metric tags, and distribution metrics can be exported
/// as <see cref="StatisticSet"/> when possible.
/// </summary>
/// <remarks>
/// <para>
/// This exporter batches metrics, applies retry policies with exponential backoff
/// and jitter for transient errors, and optionally merges default environment
/// dimensions (from <see cref="IAwsEnvironmentInfo"/>).
/// </para>
/// <para>
/// CloudWatch limits batches to 20 metrics. This implementation enforces the limit
/// and retries transient failures (HTTP 429, 500, 503, throttling/limit errors).
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// var client = new AmazonCloudWatchClient();
/// var opts = new CloudWatchExporterOptions { Namespace = "MyApp" };
/// var exporter = new CloudWatchMetricExporter(client, opts);
/// await exporter.ExportAsync(myMetrics);
/// </code>
/// </example>
public sealed class CloudWatchMetricExporter : IMetricExporter, IDisposable
{
    private readonly IAmazonCloudWatch _cw;
    private readonly CloudWatchExporterOptions _opts;
    private readonly IAwsEnvironmentInfo? _envInfo; // optional for default dimensions
    private bool _disposed;
    private CardinalityGuard? _guard;

    /// <summary>
    /// Initializes a new instance of the <see cref="CloudWatchMetricExporter"/> class.
    /// </summary>
    /// <param name="cwClient">The AWS CloudWatch client.</param>
    /// <param name="options">The exporter configuration options.</param>
    /// <param name="envInfo">Optional AWS environment info for merging default dimensions.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="cwClient"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown if the CloudWatch namespace is not specified.</exception>
    public CloudWatchMetricExporter(
        IAmazonCloudWatch cwClient,
        CloudWatchExporterOptions options,
        IAwsEnvironmentInfo? envInfo = null)
    {
        _cw = cwClient ?? throw new ArgumentNullException(nameof(cwClient));
        _opts = options ?? throw new ArgumentNullException(nameof(options));
        _envInfo = envInfo;

        if (string.IsNullOrWhiteSpace(_opts.Namespace))
            throw new ArgumentException("CloudWatch Namespace required.", nameof(options));

        if (_opts.MaxBatchSize <= 0 || _opts.MaxBatchSize > 20)
            _opts.MaxBatchSize = 20; // CloudWatch limit
    }

    /// <summary>
    /// Exports the provided metrics to CloudWatch, batching as needed.
    /// </summary>
    /// <param name="metrics">The metrics to export.</param>
    /// <param name="ct">A cancellation token to cancel the export.</param>
    /// <returns>
    /// A <see cref="Task"/> that completes when metrics are flushed to CloudWatch
    /// or cancelled via <paramref name="ct"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="metrics"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is cancelled.</exception>
    [RequiresUnreferencedCode("IMetricExporter.ExportAsync may use reflection or members trimmed at AOT; keep members or disable trimming for exporters.")]
    public async Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var metricData = new List<MetricDatum>(capacity: 128);

        foreach (var m in metrics)
        {
            ct.ThrowIfCancellationRequested();

            var name = _opts.UseDotsCase ? m.Name : m.Name.Replace('.', '_');

            if (_opts.UseStatisticSetForDistributions &&
                TryExtractStatisticSet(m, out var stat, out var unitFromTags))
            {
                var datum = new MetricDatum
                {
                    MetricName = name,
                    Timestamp = DateTime.UtcNow,
                    Unit = unitFromTags,
                    StatisticValues = stat
                };
                AddDimensions(datum, m.Tags);
                metricData.Add(datum);
            }
            else if (TryExtractNumeric(m, out var asDouble))
            {
                var datum = new MetricDatum
                {
                    MetricName = name,
                    Timestamp = DateTime.UtcNow,
                    Unit = CloudWatchMapping.MapUnitFromTags(m.Tags),
                    Value = asDouble
                };
                AddDimensions(datum, m.Tags);
                metricData.Add(datum);
            }

            if (metricData.Count >= _opts.MaxBatchSize)
            {
                await FlushBatchAsync(metricData, ct).ConfigureAwait(false);
                metricData.Clear();
            }
        }

        if (metricData.Count > 0)
            await FlushBatchAsync(metricData, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Flushes a batch of metrics to CloudWatch, applying retry and backoff policies.
    /// </summary>
    /// <param name="data">The batch of metric data to send.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">If <paramref name="ct"/> is cancelled.</exception>
    private async Task FlushBatchAsync(IReadOnlyCollection<MetricDatum> data, CancellationToken ct)
    {
        var req = new PutMetricDataRequest
        {
            Namespace = _opts.Namespace,
            MetricData = data.ToList()
        };

        var backoff = _opts.BaseDelayMs;

        for (int attempt = 0; attempt <= _opts.MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var cts = new CancellationTokenSource(_opts.TimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

            try
            {
                await _cw.PutMetricDataAsync(req, linked.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                if (attempt == _opts.MaxRetries) throw;
                await Task.Delay(AddJitter(backoff), ct).ConfigureAwait(false);
                backoff = Math.Min(backoff * 2, 8000);
            }
            catch (AmazonServiceException ex) when (IsTransient(ex))
            {
                if (attempt == _opts.MaxRetries) throw;
                await Task.Delay(AddJitter(backoff), ct).ConfigureAwait(false);
                backoff = Math.Min(backoff * 2, 8000);
            }
        }
    }

    /// <summary>
    /// Determines whether the specified exception represents a transient error.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns><see langword="true"/> if the error is considered transient; otherwise <see langword="false"/>.</returns>
    private static bool IsTransient(AmazonServiceException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var code = ex.ErrorCode ?? string.Empty;
        var http = (int)ex.StatusCode;

        if (http is 429 or 500 or 503)
        {
            return true;
        }

        if (code.Contains("throttl", StringComparison.OrdinalIgnoreCase)) return true;
        if (code.Contains("limit", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    /// <summary>
    /// Adds random jitter to the base backoff value to prevent thundering herd.
    /// </summary>
    /// <param name="baseMs">The base backoff delay in milliseconds.</param>
    /// <returns>A randomized delay with ±20% jitter.</returns>
    private static int AddJitter(int baseMs)
    {
        if (baseMs < 0) baseMs = 0;
        var jitter = (int)(baseMs * 0.2); // +/-20%
        var offset = RandomNumberGenerator.GetInt32(-jitter, jitter + 1);
        return baseMs + offset;
    }

    /// <summary>
    /// Attempts to extract a numeric value from an <see cref="IMetric"/>.
    /// </summary>
    /// <param name="metric">The metric to evaluate.</param>
    /// <param name="value">When this method returns, contains the extracted numeric value.</param>
    /// <returns><see langword="true"/> if a numeric value was extracted; otherwise <see langword="false"/>.</returns>
    private static bool TryExtractNumeric(IMetric metric, out double value)
    {
        ArgumentNullException.ThrowIfNull(metric);

        // IMetric<T> pattern matching: no reflection
        if (metric is IMetric<double> d) { value = d.GetValue(); return true; }
        if (metric is IMetric<float> f) { value = f.GetValue(); return true; }
        if (metric is IMetric<decimal> de) { value = (double)de.GetValue(); return true; }
        if (metric is IMetric<long> l) { value = l.GetValue(); return true; }
        if (metric is IMetric<int> i) { value = i.GetValue(); return true; }
        if (metric is IMetric<short> s) { value = s.GetValue(); return true; }
        if (metric is IMetric<ulong> ul) { value = ul.GetValue(); return true; }
        if (metric is IMetric<uint> ui) { value = ui.GetValue(); return true; }
        if (metric is IMetric<ushort> usv) { value = usv.GetValue(); return true; }

        // Last resort: enumerable values
        var v = metric.GetValue();
        if (v is IEnumerable<double> ed) { value = SumOrLast(ed, out var ok); return ok; }
        if (v is IEnumerable<float> ef) { value = SumOrLast(AsDoubles(ef), out var ok); return ok; }
        if (v is IEnumerable<int> ei) { value = SumOrLast(AsDoubles(ei), out var ok); return ok; }
        if (v is IEnumerable<long> el) { value = SumOrLast(AsDoubles(el), out var ok); return ok; }
        if (v is IEnumerable<decimal> eDec) { value = SumOrLast(AsDoubles(eDec), out var ok); return ok; }

        value = default;
        return false;

        static IEnumerable<double> AsDoubles<TNum>(IEnumerable<TNum> seq)
            where TNum : struct, IConvertible
        {
            foreach (var x in seq) yield return Convert.ToDouble(x, CultureInfo.InvariantCulture);
        }

        static double SumOrLast(IEnumerable<double> seq, out bool ok)
        {
            ok = false;
            double sum = 0;
            int n = 0;
            foreach (var x in seq) { sum += x; n++; }
            if (n == 0) return 0;
            ok = true;
            return sum;
        }
    }

    /// <summary>
    /// Adds dimensions from metric tags and optionally merges default environment dimensions,
    /// applying cardinality guard rules.
    /// </summary>
    /// <param name="datum">The CloudWatch metric datum to populate.</param>
    /// <param name="tags">The metric tags to consider as dimensions.</param>
    private void AddDimensions(MetricDatum datum, IReadOnlyDictionary<string, string>? tags)
    {
        ArgumentNullException.ThrowIfNull(datum);

        var guard = _guard ??= new CardinalityGuard(_opts);

        if (tags is { Count: > 0 })
        {
            foreach (var key in _opts.DimensionTagKeys)
            {
                if (datum.Dimensions.Count >= 10) break;
                if (tags.TryGetValue(key, out var val))
                    guard.TryAddDimension(datum, key, val);
            }
        }

        if (_opts.MergeDefaultDimensions && _envInfo is not null && datum.Dimensions.Count < 10)
        {
            var defaults = _envInfo.GetDefaultDimensions();
            foreach (var kv in defaults)
            {
                if (datum.Dimensions.Count >= 10) break;

                if (datum.Dimensions.Exists(d => string.Equals(d.Name, kv.Key, StringComparison.Ordinal)))
                    continue;

                guard.TryAddDimension(datum, kv.Key, kv.Value);
            }
        }
    }

    /// <summary>
    /// Attempts to extract a <see cref="StatisticSet"/> from an <see cref="IMetric"/>.
    /// </summary>
    /// <param name="metric">The metric to evaluate.</param>
    /// <param name="stat">When this method returns, contains the constructed statistic set.</param>
    /// <param name="unitFromTags">When this method returns, contains the unit derived from tags.</param>
    /// <returns><see langword="true"/> if a valid statistic set was created; otherwise <see langword="false"/>.</returns>
    private static bool TryExtractStatisticSet(IMetric metric, out StatisticSet stat, out StandardUnit unitFromTags)
    {
        ArgumentNullException.ThrowIfNull(metric);
        unitFromTags = CloudWatchMapping.MapUnitFromTags(metric.Tags);

        // IEnumerable<double> fast-path
        if (metric is IMetric<IEnumerable<double>> mseq)
        {
            if (ComputeStats(mseq.GetValue(), out stat)) return true;
        }

        // Boxed values
        var v = metric.GetValue();
        if (v is IEnumerable<double> seq && ComputeStats(seq, out stat)) return true;
        if (v is IEnumerable<float> ef && ComputeStats(AsDoubles(ef), out stat)) return true;
        if (v is IEnumerable<int> ei && ComputeStats(AsDoubles(ei), out stat)) return true;
        if (v is IEnumerable<long> el && ComputeStats(AsDoubles(el), out stat)) return true;
        if (v is IEnumerable<decimal> ed && ComputeStats(AsDoubles(ed), out stat)) return true;

        stat = default!;
        return false;

        static IEnumerable<double> AsDoubles<TNum>(IEnumerable<TNum> seq)
            where TNum : struct, IConvertible
        {
            foreach (var x in seq) yield return Convert.ToDouble(x, CultureInfo.InvariantCulture);
        }

        static bool ComputeStats(IEnumerable<double> seq, out StatisticSet s)
        {
            double? min = null, max = null, sum = 0;
            long cnt = 0;
            foreach (var d in seq)
            {
                cnt++; sum += d;
                if (min is null || d < min) min = d;
                if (max is null || d > max) max = d;
            }
            if (cnt > 0 && min is not null && max is not null)
            {
                s = new StatisticSet { SampleCount = cnt, Sum = sum, Minimum = min.Value, Maximum = max.Value };
                return true;
            }
            s = default!;
            return false;
        }
    }

    /// <summary>
    /// Disposes the exporter and releases the underlying CloudWatch client.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _cw.Dispose();
        _disposed = true;
    }
}
