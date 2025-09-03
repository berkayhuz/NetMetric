// <copyright file="AzureMonitorExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Options;

namespace NetMetric.Export.AzureMonitor.Exporters;

/// <summary>
/// The AzureMonitorExporter class is responsible for exporting metrics to Azure Monitor.
/// It processes various metric types such as Gauge, Counter, Distribution, Summary, BucketHistogram, and MultiSample values,
/// enqueuing them to a specified Azure Monitor queue.
/// </summary>
public sealed class AzureMonitorExporter : IMetricExporter
{
    private readonly AzureMonitorChannel _queue;
    private readonly AzureMonitorExporterOptions _o;
    private readonly IOptions<MetricOptions>? _metricOpts;
    private readonly IAzureMonitorClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureMonitorExporter"/> class.
    /// </summary>
    /// <param name="queue">The Azure Monitor queue for enqueuing telemetry data.</param>
    /// <param name="client">The Azure Monitor client used for interacting with the Azure Monitor service.</param>
    /// <param name="options">The configuration options for the Azure Monitor exporter.</param>
    /// <param name="metricOptions">Optional metric options used for additional metric configuration.</param>
    internal AzureMonitorExporter(
    AzureMonitorChannel queue,
    IAzureMonitorClient client,
    IOptions<AzureMonitorExporterOptions> options,
    IOptions<MetricOptions>? metricOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _queue = queue;
        _client = client;
        _o = options.Value;
        _metricOpts = metricOptions;
    }

    /// <summary>
    /// Exports the specified metrics asynchronously to Azure Monitor.
    /// </summary>
    /// <param name="metrics">A collection of metrics to export.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RequiresUnreferencedCode("IMetricExporter.ExportAsync may use reflection or members trimmed at AOT; keep members or disable trimming for exporters.")]
    public Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        foreach (var m in metrics)
        {
            ct.ThrowIfCancellationRequested();

            BuildMeta(m, out var unit, out var desc);
            var name = BuildName(m.Name);

            switch (m.GetValue())
            {
                case GaugeValue g:
                    Enqueue(name, g.Value, MergeFilterAndLimitTags(m.Tags), unit, desc);
                    break;

                case CounterValue c:
                    Enqueue(name, c.Value, MergeFilterAndLimitTags(m.Tags), unit, desc);
                    break;

                case DistributionValue d:
                    var pd = MergeFilterAndLimitTags(m.Tags);
                    pd["agg.count"] = d.Count.ToString();
                    pd["agg.min"] = d.Min.ToString(CultureInfo.InvariantCulture);
                    pd["agg.max"] = d.Max.ToString(CultureInfo.InvariantCulture);
                    pd["agg.p50"] = d.P50.ToString(CultureInfo.InvariantCulture);
                    pd["agg.p90"] = d.P90.ToString(CultureInfo.InvariantCulture);
                    pd["agg.p99"] = d.P99.ToString(CultureInfo.InvariantCulture);
                    Enqueue(name, d.Count, pd, unit, desc);
                    break;

                case SummaryValue s:
                    var ps = MergeFilterAndLimitTags(m.Tags);
                    ps["agg.count"] = s.Count.ToString();
                    ps["agg.min"] = s.Min.ToString(CultureInfo.InvariantCulture);
                    ps["agg.max"] = s.Max.ToString(CultureInfo.InvariantCulture);
                    foreach (var (q, v) in s.Quantiles)
                        ps[$"q{q:0.##}"] = v.ToString(CultureInfo.InvariantCulture);
                    Enqueue(name, s.Count, ps, unit, desc);
                    break;

                case BucketHistogramValue bh:
                    var ph = MergeFilterAndLimitTags(m.Tags);
                    ph["agg.count"] = bh.Count.ToString();
                    ph["agg.min"] = bh.Min.ToString(CultureInfo.InvariantCulture);
                    ph["agg.max"] = bh.Max.ToString(CultureInfo.InvariantCulture);
                    ph["agg.sum"] = bh.Sum.ToString(CultureInfo.InvariantCulture);
                    ph["buckets"] = string.Join(",", bh.Buckets);
                    ph["counts"] = string.Join(",", bh.Counts);
                    Enqueue(name, bh.Count, ph, unit, desc);
                    break;

                case MultiSampleValue ms:
                    foreach (var item in ms.Items)
                    {
                        if (item.Value is GaugeValue gv)
                        {
                            var sub = BuildName(item.Name ?? m.Name);
                            var props = MergeFilterAndLimitTags(item.Tags);
                            Enqueue(sub, gv.Value, props, unit, desc);
                        }
                    }
                    break;

                default:
                    break;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Enqueues a metric to the Azure Monitor queue.
    /// </summary>
    /// <param name="name">The name of the metric.</param>
    /// <param name="value">The value of the metric.</param>
    /// <param name="props">The properties associated with the metric.</param>
    /// <param name="unit">The unit of measurement for the metric.</param>
    /// <param name="desc">The description of the metric.</param>
    private void Enqueue(string name, double value, Dictionary<string, string> props, string? unit, string? desc)
    {
        ArgumentNullException.ThrowIfNull(props);

        var mt = new MetricTelemetry(name, value);

        if (!string.IsNullOrWhiteSpace(unit))
        {
            mt.Properties["unit"] = unit!;
        }
        if (!string.IsNullOrWhiteSpace(desc))
        {
            mt.Properties["desc"] = desc!;
        }

        foreach (var kv in props)
        {
            mt.Properties[kv.Key] = kv.Value ?? string.Empty;
        }

        // Kuyruk doluysa TryWrite false döner; queue kendisi drop sayacını günceller.
        _queue.TryWrite(new TelemetryEnvelope(mt));
    }

    /// <summary>
    /// Builds a sanitized metric name by adding a prefix and removing invalid characters.
    /// </summary>
    /// <param name="name">The original metric name.</param>
    /// <returns>A sanitized metric name.</returns>
    private string BuildName(string name)
    {
        var n = string.IsNullOrEmpty(_o.NamePrefix) ? name : _o.NamePrefix + name;

        if (!_o.SanitizeMetricNames)
        {
            return n;
        }

        Span<char> buf = stackalloc char[n.Length];

        for (int i = 0; i < n.Length; i++)
        {
            char c = n[i];

            buf[i] = char.IsLetterOrDigit(c) ? c : (c == ' ' ? '_' : '-');
        }

        return new string(buf);
    }

    /// <summary>
    /// Extracts metadata from a metric, such as its unit and description.
    /// </summary>
    /// <param name="m">The metric to extract metadata from.</param>
    /// <param name="unit">The extracted unit of measurement.</param>
    /// <param name="desc">The extracted description.</param>
    private static void BuildMeta(IMetric m, out string? unit, out string? desc)
    {
        ArgumentNullException.ThrowIfNull(m);

        var t = typeof(IMetric);
        unit = t.GetProperty("Unit")?.GetValue(m) as string;
        desc = t.GetProperty("Description")?.GetValue(m) as string;
    }

    /// <summary>
    /// Merges global tags, resource tags, and local tags, applying various filters and limits.
    /// </summary>
    /// <param name="local">The local tags to merge with global and resource tags.</param>
    /// <returns>A merged dictionary of tags.</returns>
    private Dictionary<string, string> MergeFilterAndLimitTags(IReadOnlyDictionary<string, string> local)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        var opts = _metricOpts?.Value;
        if (opts?.GlobalTags is { Count: > 0 })
        {
            foreach (var kv in opts.GlobalTags)
            {
                if (!string.IsNullOrEmpty(kv.Key))
                {
                    dict[kv.Key] = kv.Value ?? string.Empty;
                }
            }
        }

        if (opts?.NmResource is { } r)
        {
            if (!string.IsNullOrWhiteSpace(r.ServiceName))
            {
                dict["service.name"] = r.ServiceName!;
            }
            if (!string.IsNullOrWhiteSpace(r.ServiceVersion))
            {
                dict["service.version"] = r.ServiceVersion!;
            }
            if (!string.IsNullOrWhiteSpace(r.DeploymentEnvironment))
            {
                dict["deployment.environment"] = r.DeploymentEnvironment!;
            }
            if (!string.IsNullOrWhiteSpace(r.HostName))
            {
                dict["host.name"] = r.HostName!;
            }
            if (r.Additional is { Count: > 0 })
            {
                foreach (var kv in r.Additional)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                    {
                        dict[kv.Key] = kv.Value ?? string.Empty;
                    }
                }
            }
        }

        if (local is { Count: > 0 })
        {
            foreach (var kv in local)
            {
                if (!string.IsNullOrEmpty(kv.Key))
                {
                    dict[kv.Key] = kv.Value ?? string.Empty;
                }
            }
        }

        if (_o.TagAllowList is { Count: > 0 })
        {
            var allow = new HashSet<string>(_o.TagAllowList, StringComparer.Ordinal);
            dict = dict.Where(kv => allow.Contains(kv.Key))
                       .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }
        if (_o.TagBlockList is { Count: > 0 })
        {
            foreach (var k in _o.TagBlockList)
            {
                dict.Remove(k);
            }
        }

        if (_o.MaxTagKeyLength > 0 || _o.MaxTagValueLength > 0 || _o.MaxTagsPerMetric is > 0)
        {
            var maxK = _o.MaxTagKeyLength;
            var maxV = _o.MaxTagValueLength;
            var maxC = _o.MaxTagsPerMetric ?? int.MaxValue;

            var trimmed = new Dictionary<string, string>(Math.Min(dict.Count, maxC), StringComparer.Ordinal);
            foreach (var kv in dict)
            {
                if (trimmed.Count >= maxC)
                {
                    break;
                }

                var k = maxK > 0 && kv.Key.Length > maxK ? kv.Key[..maxK] : kv.Key;
                var v = kv.Value ?? string.Empty;

                if (maxV > 0 && v.Length > maxV)
                {
                    v = v[..maxV];
                }

                trimmed[k] = v;
            }

            return trimmed;
        }

        return dict;
    }
}
