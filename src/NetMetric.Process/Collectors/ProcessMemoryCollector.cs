// <copyright file="ProcessMemoryCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Abstractions;
using NetMetric.Process.Abstractions;
using NetMetric.Process.Configuration;

namespace NetMetric.Process.Collectors;

/// <summary>
/// Collects and calculates memory usage metrics for the current process.
/// This includes metrics for working set, private memory, and paged memory.
/// </summary>
public sealed class ProcessMemoryCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;
    private readonly IProcessInfoProvider _proc;
    private readonly ProcessOptions _opts;

    // Predefined tags for different types of memory usage
    private static readonly IReadOnlyDictionary<string, string>
        TagWorking = new Dictionary<string, string> { ["kind"] = "working_set" },
        TagPrivate = new Dictionary<string, string> { ["kind"] = "private" },
        TagPaged = new Dictionary<string, string> { ["kind"] = "paged" };

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessMemoryCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to build and collect metrics.</param>
    /// <param name="proc">The process information provider to fetch process memory data.</param>
    /// <param name="opts">The options that control the memory metric collection behavior.</param>
    public ProcessMemoryCollector(IMetricFactory factory, IProcessInfoProvider proc, ProcessOptions opts)
        => (_factory, _proc, _opts) = (factory, proc, opts);

    /// <summary>
    /// Collects the memory usage metrics asynchronously for the current process.
    /// This includes working set, private memory, and paged memory.
    /// </summary>
    /// <param name="ct">The cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, with the resulting memory metric.</returns>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IMetric?>(ct);
        }

        var p = _proc.Current;

        // Create a multi-gauge to track various memory statistics
        var mg = _factory.MultiGauge($"{_opts.MetricPrefix}.memory.bytes", "Process memory stats (bytes)").WithUnit("bytes").WithDescription("working_set/private/paged via tags").WithProcessDefaultTags(_proc, _opts).Build();

        // Set memory values to the corresponding tags
        mg.SetValue(p.WorkingSet64, TagWorking);
        mg.SetValue(p.PrivateMemorySize64, TagPrivate);

        // Set paged memory size only on Windows, as it may not be available on other OS
        if (OperatingSystem.IsWindows())
        {
            try
            {
                mg.SetValue(p.PagedMemorySize64, TagPaged);
            }
            catch
            {
                throw;
            }
        }

        return Task.FromResult<IMetric?>(mg);
    }

    /// <summary>
    /// Creates a summary metric for memory usage, optionally including quantiles and tags.
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
    /// Creates a histogram metric for memory usage with specified bucket upper bounds and tags.
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
