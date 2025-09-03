// <copyright file="ProcessCpuCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Process.Abstractions;
using NetMetric.Process.Configuration;

namespace NetMetric.Process.Collectors;

/// <summary>
/// Collects and calculates the CPU usage metrics for the current process.
/// The CPU usage is calculated as a percentage, optionally smoothed using Exponential Weighted Moving Average (EWMA).
/// </summary>
public sealed class ProcessCpuCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;
    private readonly IProcessInfoProvider _proc;
    private readonly ProcessOptions _opts;

    // Lock for EWMA calculation to avoid race conditions in parallel collectors
    private readonly object _emaLock = new();
    private double? _ema;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessCpuCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to build and collect metrics.</param>
    /// <param name="proc">The process information provider to fetch process CPU data.</param>
    /// <param name="opts">The options that control the CPU metric collection behavior.</param>
    public ProcessCpuCollector(IMetricFactory factory, IProcessInfoProvider proc, ProcessOptions opts)
        => (_factory, _proc, _opts) = (factory, proc, opts);

    /// <summary>
    /// Collects the CPU usage metric asynchronously for the current process.
    /// The metric is calculated as a percentage, with optional smoothing applied.
    /// </summary>
    /// <param name="ct">The cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, with the resulting metric.</returns>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IMetric?>(ct);
        }

        var nowTs = Stopwatch.GetTimestamp();
        var total = _proc.Current.TotalProcessorTime;
        var (lastTotal, lastTs) = _proc.GetLast();

        // Delta calculations (first sample if lastTs == 0)
        double cpuPercent = 0d;
        double dtSec = 0d;

        if (lastTs != 0)
        {
            var ticksDelta = nowTs - lastTs;

            if (ticksDelta > 0)
            {
                dtSec = ticksDelta / (double)Stopwatch.Frequency;
                double cpuSec = (total - lastTotal).TotalSeconds;
                cpuPercent = (cpuSec / (dtSec * Math.Max(1, _proc.ProcessorCount))) * 100.0;
                cpuPercent = Math.Clamp(cpuPercent, 0d, 100d);
            }
        }

        // Update reference point
        _proc.SetLast(total, nowTs);

        // Optional EWMA smoothing
        if (_opts.CpuSmoothingWindowMs > 0 && dtSec > 0)
        {
            var windowSec = Math.Max(0.001, _opts.CpuSmoothingWindowMs / 1000.0);
            var alpha = 1.0 - Math.Exp(-dtSec / windowSec);

            lock (_emaLock)
            {
                _ema = _ema is null ? cpuPercent : _ema + alpha * (cpuPercent - _ema.Value);
                cpuPercent = _ema.Value;
            }
        }

        var g = _factory.Gauge($"{_opts.MetricPrefix}.cpu.percent", "Process CPU Usage (%)").WithUnit("%").WithDescription("Normalized by logical processor count").WithProcessDefaultTags(_proc, _opts).Build();
        g.SetValue(cpuPercent);

        return Task.FromResult<IMetric?>(g);
    }

    // ---- Explicit IMetricCollector helpers (factory delegation + tag/bound honoring) ----

    /// <summary>
    /// Creates a summary metric for CPU usage, optionally including quantiles and tags.
    /// </summary>
    /// <param name="id">The metric ID.</param>
    /// <param name="name">The display name of the metric.</param>
    /// <param name="quantiles">The quantiles for the summary.</param>
    /// <param name="tags">The tags associated with the metric.</param>
    /// <param name="resetOnGet">Indicates whether to reset the summary metric on each retrieval.</param>
    /// <returns>A summary metric object.</returns>
    ISummaryMetric IMetricCollector.CreateSummary(string id, string name, IEnumerable<double> quantiles, IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
    {
        var sb = _factory.Summary(id, name);

        CollectorBuilderHelpers.ApplyTags(sb, tags);

        if (quantiles is not null)
        {
            sb = sb.WithQuantiles(quantiles.ToArray());
        }

        return sb.Build();
    }

    /// <summary>
    /// Creates a histogram metric for CPU usage with specified bucket upper bounds and tags.
    /// </summary>
    /// <param name="id">The metric ID.</param>
    /// <param name="name">The display name of the metric.</param>
    /// <param name="bucketUpperBounds">The upper bounds of the histogram buckets.</param>
    /// <param name="tags">The tags associated with the metric.</param>
    /// <returns>A bucket histogram metric object.</returns>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
    {
        var hb = _factory.Histogram(id, name);

        CollectorBuilderHelpers.ApplyTags(hb, tags);

        if (bucketUpperBounds is not null)
        {
            hb = hb.WithBounds(bucketUpperBounds.ToArray());
        }

        return hb.Build();
    }
}
