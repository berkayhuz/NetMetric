// <copyright file="StackdriverExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Google.Api;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.Monitoring.V3;
using Microsoft.Extensions.Options;

namespace NetMetric.Export.Stackdriver.Exporters;

/// <summary>
/// Exports NetMetric metrics to Google Cloud Monitoring (formerly Stackdriver).
/// </summary>
/// <remarks>
/// <para>
/// This exporter translates NetMetric metric values into Google Cloud Monitoring (GCM) time series
/// and submits them using a shared <see cref="Google.Cloud.Monitoring.V3.MetricServiceClient"/>. Metrics are
/// buffered up to <see cref="StackdriverExporterOptions.BatchSize"/> items per request and written in batches
/// for efficiency. Transient failures are retried with exponential backoff and jitter as configured by
/// <see cref="StackdriverExporterOptions.Retry"/>.
/// </para>
/// <para>
/// <strong>Metric mapping</strong> (high level):
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     Gauge values are exported as <em>GAUGE</em> metrics with <em>DOUBLE</em> value type.
///     </description>
///   </item>
///   <item>
///     <description>
///     Counter values are exported as <em>CUMULATIVE</em> metrics with <em>INT64</em> value type; the start time is tracked per
///     metric identity and defaults to the process start supplied by <see cref="StackdriverExporterOptions.ProcessStart"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///     Bucketed histograms are exported as <em>GAUGE/DISTRIBUTION</em> time series (count, range, and bucketized counts).
///     </description>
///   </item>
///   <item>
///     <description>
///     Summaries expose one gauge per configured quantile (e.g., <c>id/q50</c>, <c>id/q99</c>).
///     </description>
///   </item>
///   <item>
///     <description>
///     Distributions expose common rollups as individual gauges (e.g., <c>id/count</c>, <c>id/min</c>, <c>id/p99</c>, <c>id/max</c>).
///     </description>
///   </item>
///   <item>
///     <description>
///     Multi-sample values are exported per item with additional labels (e.g., <c>sample_id</c>, <c>sample_name</c>),
///     when applicable to the underlying sample value type.
///     </description>
///   </item>
/// </list>
/// <para>
/// When <see cref="StackdriverExporterOptions.EnableCreateDescriptors"/> is <see langword="true"/>, the exporter will ensure
/// that custom metric descriptors exist in the project before writing time series. Metric type names are constructed
/// using <see cref="StackdriverExporterOptions.MetricPrefix"/> (for example, <c>custom.googleapis.com/netmetric/...</c>).
/// Units are normalized to UCUM strings as needed.
/// </para>
/// <para>
/// <strong>Thread safety.</strong> Instances of <see cref="StackdriverExporter"/> are safe for concurrent use. Internal
/// state shared across calls (e.g., the cumulative counter start times) is stored in a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Batching buffers are local to each call to <see cref="ExportAsync(System.Collections.Generic.IEnumerable{IMetric}, System.Threading.CancellationToken)"/>.
/// </para>
/// <para>
/// <strong>Trimming/AOT.</strong> The exporter may employ limited reflection when projecting arbitrary metric
/// implementations to time series; see the <see cref="RequiresUnreferencedCodeAttribute"/> on <see cref="ExportAsync(System.Collections.Generic.IEnumerable{IMetric}, System.Threading.CancellationToken)"/>.
/// If you enable trimming, ensure your metric types are preserved appropriately.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// using Google.Cloud.Monitoring.V3;
/// using Microsoft.Extensions.DependencyInjection;
/// using Microsoft.Extensions.Options;
/// using NetMetric.Export.Stackdriver.Exporters;
///
/// // Configure options
/// var opts = Options.Create(new StackdriverExporterOptions
/// {
///     ProjectId = "my-gcp-project",
///     MetricPrefix = "custom.googleapis.com/netmetric",
///     ResourceType = "global",
///     ResourceLabels = { ["location"] = "europe-west1" },
///     EnableCreateDescriptors = true,
///     BatchSize = 200,
///     Retry = new RetryOptions { MaxAttempts = 5, InitialBackoffMs = 500, MaxBackoffMs = 30000, Jitter = 0.2 },
///     ProcessStart = () => DateTimeOffset.UtcNow // or your process start source
/// });
///
/// // Create the Google client (it is thread-safe and reusable)
/// MetricServiceClient client = await MetricServiceClient.CreateAsync();
///
/// var exporter = new StackdriverExporter(client, opts);
///
/// // Export a snapshot (IMetric is a NetMetric abstraction)
/// await exporter.ExportAsync(metrics, cancellationToken);
/// ]]></code>
/// </example>
/// <seealso cref="StackdriverExporterOptions"/>
public sealed partial class StackdriverExporter : IMetricExporter
{
    private readonly MetricServiceClient _client;
    private readonly StackdriverExporterOptions _opt;
    private readonly DescriptorCache _descCache;
    private readonly MonitoredResource _resource;
    private readonly DateTimeOffset _processStart;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _counterStarts = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="StackdriverExporter"/> class.
    /// </summary>
    /// <param name="client">The Google Cloud Monitoring client used to send time series.</param>
    /// <param name="options">The exporter configuration options.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="client"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The exporter constructs a <see cref="MonitoredResource"/> from <see cref="StackdriverExporterOptions.ResourceType"/>
    /// and <see cref="StackdriverExporterOptions.ResourceLabels"/>, and caches metric descriptors per project to avoid
    /// redundant creation requests.
    /// </remarks>
    public StackdriverExporter(
        MetricServiceClient client,
        IOptions<StackdriverExporterOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _opt = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _descCache = new DescriptorCache(client, _opt.ProjectId);

        _resource = new MonitoredResource { Type = _opt.ResourceType };
        foreach (var kv in _opt.ResourceLabels)
            _resource.Labels[kv.Key] = kv.Value ?? string.Empty;

        _processStart = _opt.ProcessStart();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Buffers are flushed whenever the in-memory batch reaches <see cref="StackdriverExporterOptions.BatchSize"/>,
    /// and once more at the end of the enumeration. If <see cref="System.Threading.CancellationToken"/> is signaled,
    /// the method throws <see cref="OperationCanceledException"/>.
    /// </para>
    /// <para>
    /// If <see cref="StackdriverExporterOptions.EnableCreateDescriptors"/> is enabled, the exporter ensures that descriptors
    /// exist for each metric type before writing time series. Descriptor creation is idempotent.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metrics"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is canceled.</exception>
    /// <exception cref="Grpc.Core.RpcException">
    /// May be propagated from the underlying Google Cloud Monitoring client if retries are exhausted
    /// or a non-retryable error occurs.
    /// </exception>
    [RequiresUnreferencedCode(
        "This method may use reflection when mapping NetMetric types to GCM time series. " +
        "If trimming is enabled, ensure metric implementations are preserved via DynamicDependency, " +
        "DynamicallyAccessedMembers, or a linker descriptor.")]
    public async Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var projectName = ProjectName.FromProject(_opt.ProjectId);
        var now = DateTimeOffset.UtcNow;

        var buffer = new List<TimeSeries>(_opt.BatchSize);

        async Task FlushIfNeededAsync()
        {
            if (buffer.Count >= _opt.BatchSize)
            {
                // Snapshot to a read-only collection to satisfy the analyzer and avoid mutation during send.
                var snapshot = new ReadOnlyCollection<TimeSeries>(buffer.ToList());
                await WriteBatchAsync(projectName, snapshot, ct).ConfigureAwait(false);
                buffer.Clear();
            }
        }

        foreach (var m in metrics)
        {
            ct.ThrowIfCancellationRequested();

            var (unitRaw, desc, _) = NetMetric.Export.Reflection.MetricIntrospection.ReadMeta(m);
            var unit = Ucum.Normalize(unitRaw);
            var labels = LabelMapper.ToLabels(m.Tags, _opt);

            switch (m.GetValue())
            {
                case GaugeValue g:
                    {
                        string metricType = MetricNameResolver.BuildCustomMetricType(_opt.MetricPrefix, m.Id);
                        if (_opt.EnableCreateDescriptors)
                        {
                            await _descCache.EnsureAsync(
                                metricType,
                                MetricDescriptor.Types.MetricKind.Gauge,
                                MetricDescriptor.Types.ValueType.Double,
                                unit, desc, ct).ConfigureAwait(false);
                        }

                        buffer.Add(TimeSeriesFactory.GaugeDouble(metricType, _resource, labels, g.Value, now));
                        await FlushIfNeededAsync().ConfigureAwait(false);
                        break;
                    }

                case CounterValue c:
                    {
                        string metricType = MetricNameResolver.BuildCustomMetricType(_opt.MetricPrefix, m.Id);
                        if (_opt.EnableCreateDescriptors)
                        {
                            var unitForCounter = Ucum.Normalize(unitRaw ?? "1");
                            await _descCache.EnsureAsync(
                                metricType,
                                MetricDescriptor.Types.MetricKind.Cumulative,
                                MetricDescriptor.Types.ValueType.Int64,
                                unitForCounter, desc, ct).ConfigureAwait(false);
                        }

                        var start = _counterStarts.GetOrAdd(m.Id, _ => _processStart);
                        buffer.Add(TimeSeriesFactory.CumulativeInt64(metricType, _resource, labels, c.Value, start, now));
                        await FlushIfNeededAsync().ConfigureAwait(false);
                        break;
                    }

                case BucketHistogramValue bh:
                    {
                        string metricType = MetricNameResolver.BuildCustomMetricType(_opt.MetricPrefix, m.Id);
                        if (_opt.EnableCreateDescriptors)
                        {
                            await _descCache.EnsureAsync(
                                metricType,
                                MetricDescriptor.Types.MetricKind.Gauge,
                                MetricDescriptor.Types.ValueType.Distribution,
                                unit, desc, ct).ConfigureAwait(false);
                        }

                        buffer.Add(TimeSeriesFactory.Distribution(
                            metricType, _resource, labels,
                            bh.Count, bh.Min, bh.Max, bh.Buckets, bh.Counts, now));
                        await FlushIfNeededAsync().ConfigureAwait(false);
                        break;
                    }

                case SummaryValue s:
                    {
                        foreach (var q in s.Quantiles)
                        {
                            string metricType = MetricNameResolver.BuildCustomMetricType(_opt.MetricPrefix, $"{m.Id}/q{q.Key:0.##}");
                            if (_opt.EnableCreateDescriptors)
                            {
                                await _descCache.EnsureAsync(
                                    metricType,
                                    MetricDescriptor.Types.MetricKind.Gauge,
                                    MetricDescriptor.Types.ValueType.Double,
                                    unit, desc, ct).ConfigureAwait(false);
                            }

                            buffer.Add(TimeSeriesFactory.GaugeDouble(metricType, _resource, labels, q.Value, now));
                            await FlushIfNeededAsync().ConfigureAwait(false);
                        }
                        break;
                    }

                case DistributionValue d:
                    {
                        var map = new (string suffix, double value)[]
                        {
                        ("count", d.Count),
                        ("min",   d.Min),
                        ("p50",   d.P50),
                        ("p90",   d.P90),
                        ("p99",   d.P99),
                        ("max",   d.Max)
                        };

                        foreach (var (suffix, val) in map)
                        {
                            string metricType = MetricNameResolver.BuildCustomMetricType(_opt.MetricPrefix, $"{m.Id}/{suffix}");
                            if (_opt.EnableCreateDescriptors)
                            {
                                await _descCache.EnsureAsync(
                                    metricType,
                                    MetricDescriptor.Types.MetricKind.Gauge,
                                    MetricDescriptor.Types.ValueType.Double,
                                    unit, desc, ct).ConfigureAwait(false);
                            }

                            buffer.Add(TimeSeriesFactory.GaugeDouble(metricType, _resource, labels, val, now));
                            await FlushIfNeededAsync().ConfigureAwait(false);
                        }
                        break;
                    }

                case MultiSampleValue ms:
                    {
                        foreach (var item in ms.Items)
                        {
                            var itemLabels = new Dictionary<string, string>(labels, StringComparer.Ordinal);
                            if (!string.IsNullOrWhiteSpace(item.Id)) itemLabels["sample_id"] = item.Id;
                            if (!string.IsNullOrWhiteSpace(item.Name)) itemLabels["sample_name"] = item.Name;

                            if (item.Value is GaugeValue gv)
                            {
                                string metricType = MetricNameResolver.BuildCustomMetricType(_opt.MetricPrefix, m.Id);
                                if (_opt.EnableCreateDescriptors)
                                {
                                    await _descCache.EnsureAsync(
                                        metricType,
                                        MetricDescriptor.Types.MetricKind.Gauge,
                                        MetricDescriptor.Types.ValueType.Double,
                                        unit, desc, ct).ConfigureAwait(false);
                                }

                                buffer.Add(TimeSeriesFactory.GaugeDouble(metricType, _resource, itemLabels, gv.Value, now));
                                await FlushIfNeededAsync().ConfigureAwait(false);
                            }
                        }
                        break;
                    }

                default:
                    // Unknown metric type: skip
                    break;
            }
        }

        if (buffer.Count > 0)
        {
            var snapshot = new ReadOnlyCollection<TimeSeries>(buffer.ToList());
            await WriteBatchAsync(projectName, snapshot, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determines whether a gRPC <see cref="Grpc.Core.StatusCode"/> is considered transient and therefore retriable.
    /// </summary>
    /// <param name="code">The gRPC status code to classify.</param>
    /// <returns><see langword="true"/> if the status is considered retryable; otherwise, <see langword="false"/>.</returns>
    private static bool IsRetryableStatus(Grpc.Core.StatusCode code)
        => code is Grpc.Core.StatusCode.Unavailable
                 or Grpc.Core.StatusCode.DeadlineExceeded
                 or Grpc.Core.StatusCode.ResourceExhausted
                 or Grpc.Core.StatusCode.Internal
                 or Grpc.Core.StatusCode.Aborted;

    /// <summary>
    /// Writes a batch of time series to Google Cloud Monitoring using retries with exponential backoff and jitter.
    /// </summary>
    /// <param name="project">The target Google Cloud project name.</param>
    /// <param name="batch">The collection of time series to write. Must not be <see langword="null"/>.</param>
    /// <param name="ct">A cancellation token to observe.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="batch"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is canceled.</exception>
    /// <exception cref="Grpc.Core.RpcException">May be thrown by the underlying client when retries are exhausted or the error is non-retryable.</exception>
    private async Task WriteBatchAsync(ProjectName project, ReadOnlyCollection<TimeSeries> batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var sw = Stopwatch.StartNew();
                await _client.CreateTimeSeriesAsync(project, batch, ct).ConfigureAwait(false);
                sw.Stop();

                return;
            }
            catch (Grpc.Core.RpcException ex) when (IsRetryableStatus(ex.StatusCode) && attempt < _opt.Retry.MaxAttempts)
            {
                attempt++;

                var baseDelay = Math.Min(
                    _opt.Retry.InitialBackoffMs * (1 << (attempt - 1)),
                    _opt.Retry.MaxBackoffMs);

                // Secure jitter in [1 - J, 1 + J]
                var jitterFactor = 1.0 + (_opt.Retry.Jitter * (NextDoubleSecure() * 2.0 - 1.0));
                var delayMs = (int)(baseDelay * jitterFactor);

                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Returns a cryptographically secure random <see cref="double"/> in the half-open interval [0, 1).
    /// </summary>
    /// <remarks>
    /// Uses <see cref="RandomNumberGenerator.GetInt32(int)"/> with an exclusive upper bound of <see cref="int.MaxValue"/>
    /// to generate an unbiased integer which is then normalized to a double.
    /// </remarks>
    /// <returns>A uniformly distributed random value in [0, 1).</returns>
    private static double NextDoubleSecure()
    {
        // Returns a double in [0,1) using crypto-grade randomness.
        // GetInt32 is inclusive lower bound, exclusive upper bound.
        var n = RandomNumberGenerator.GetInt32(int.MaxValue);
        return n / (double)int.MaxValue;
    }
}
