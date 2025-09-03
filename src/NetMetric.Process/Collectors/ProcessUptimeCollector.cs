// <copyright file="ProcessUptimeCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Process.Abstractions;
using NetMetric.Process.Configuration;

namespace NetMetric.Process.Collectors;

/// <summary>
/// Collects and calculates the process uptime metrics for the current process.
/// This includes metrics for the start time (in Unix format) and the total uptime in seconds.
/// </summary>
public sealed class ProcessUptimeCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;
    private readonly IProcessInfoProvider _proc;
    private readonly ProcessOptions _opts;

    // Predefined tags for process start time and uptime
    private static readonly IReadOnlyDictionary<string, string>
        TagStart = new Dictionary<string, string> { ["kind"] = "start_time_unix" },
        TagUptime = new Dictionary<string, string> { ["kind"] = "uptime_seconds" };

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessUptimeCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to build and collect metrics.</param>
    /// <param name="proc">The process information provider to fetch process uptime data.</param>
    /// <param name="opts">The options that control the uptime metric collection behavior.</param>
    public ProcessUptimeCollector(IMetricFactory factory, IProcessInfoProvider proc, ProcessOptions opts)
        => (_factory, _proc, _opts) = (factory, proc, opts);

    /// <summary>
    /// Collects the uptime metrics asynchronously for the current process.
    /// This includes the process start time and total uptime in seconds.
    /// </summary>
    /// <param name="ct">The cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, with the resulting uptime metric.</returns>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IMetric?>(ct);
        }

        var start = _proc.StartTimeUtc;
        var up = _proc.UptimeUtc();

        // Create a multi-gauge to track the process lifecycle metrics
        var mg = _factory.MultiGauge($"{_opts.MetricPrefix}.lifecycle", "Process lifecycle")
                        .WithDescription("start_time_unix/uptime_seconds via tags")
                        .WithProcessDefaultTags(_proc, _opts)
                        .Build();

        // Convert the process start time to Unix timestamp and set the metric values
        var startUnix = new DateTimeOffset(start).ToUnixTimeSeconds();

        mg.SetValue(startUnix, TagStart);
        mg.SetValue(up.TotalSeconds, TagUptime);

        return Task.FromResult<IMetric?>(mg);
    }

    /// <summary>
    /// Creates a summary metric for uptime, optionally including quantiles and tags.
    /// </summary>
    /// <param name="id">The metric ID.</param>
    /// <param name="name">The display name of the metric.</param>
    /// <param name="quantiles">The quantiles for the summary.</param>
    /// <param name="tags">The tags associated with the metric.</param>
    /// <param name="resetOnGet">Indicates whether to reset the summary metric on each retrieval.</param>
    /// <returns>A summary metric object.</returns>
    ISummaryMetric IMetricCollector.CreateSummary(string id, string name, IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
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
    /// Creates a histogram metric for uptime with specified bucket upper bounds and tags.
    /// </summary>
    /// <param name="id">The metric ID.</param>
    /// <param name="name">The display name of the metric.</param>
    /// <param name="bucketUpperBounds">The upper bounds of the histogram buckets.</param>
    /// <param name="tags">The tags associated with the metric.</param>
    /// <returns>A bucket histogram metric object.</returns>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(string id, string name,
        IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
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
