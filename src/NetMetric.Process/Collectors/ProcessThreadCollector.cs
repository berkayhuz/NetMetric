// <copyright file="ProcessThreadCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Process.Abstractions;
using NetMetric.Process.Configuration;

namespace NetMetric.Process.Collectors;

/// <summary>
/// Collects and calculates thread and handle count metrics for the current process.
/// This includes metrics for the number of threads and handles used by the process.
/// </summary>
public sealed class ProcessThreadCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;
    private readonly IProcessInfoProvider _proc;
    private readonly ProcessOptions _opts;

    // Predefined tags for thread and handle counts
    private static readonly IReadOnlyDictionary<string, string>
        TagThreads = new Dictionary<string, string> { ["kind"] = "threads" },
        TagHandles = new Dictionary<string, string> { ["kind"] = "handles" };

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessThreadCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to build and collect metrics.</param>
    /// <param name="proc">The process information provider to fetch process thread and handle data.</param>
    /// <param name="opts">The options that control the resource metric collection behavior.</param>
    public ProcessThreadCollector(IMetricFactory factory, IProcessInfoProvider proc, ProcessOptions opts)
        => (_factory, _proc, _opts) = (factory, proc, opts);

    /// <summary>
    /// Collects the thread and handle count metrics asynchronously for the current process.
    /// </summary>
    /// <param name="ct">The cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, with the resulting resource metrics.</returns>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IMetric?>(ct);
        }

        var p = _proc.Current;

        // Create a multi-gauge to track the number of threads and handles
        var mg = _factory.MultiGauge($"{_opts.MetricPrefix}.resources.count", "Process resource counts")
                        .WithDescription("threads/handles via tags")
                        .WithProcessDefaultTags(_proc, _opts)
                        .Build();

        // Set thread count to the corresponding tag
        mg.SetValue(p.Threads?.Count ?? 0, TagThreads);

        // Set handle count only on Windows, as it may not be available on other OS
        if (OperatingSystem.IsWindows())
        {
            try
            {
                mg.SetValue(p.HandleCount, TagHandles);
            }
            catch
            {
                throw;
            }
        }

        return Task.FromResult<IMetric?>(mg);
    }

    /// <summary>
    /// Creates a summary metric for thread and handle counts, optionally including quantiles and tags.
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
    /// Creates a histogram metric for thread and handle counts with specified bucket upper bounds and tags.
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
