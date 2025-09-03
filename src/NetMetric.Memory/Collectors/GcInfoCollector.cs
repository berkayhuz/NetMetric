// <copyright file="GcInfoCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Memory.Collectors;

/// <summary>
/// Collects information related to the .NET garbage collector (GC) statistics.
/// Provides metrics about the total number of collections for each GC generation, 
/// as well as memory information like heap size and fragmentation.
/// </summary>
public sealed class GcInfoCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;
    private readonly ITimeProvider _clock;

    private long _lastTotalCollectionsGen0;
    private long _lastTotalCollectionsGen1;
    private long _lastTotalCollectionsGen2;
    private bool _hasBaseline;
    private DateTime _lastTsUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="GcInfoCollector"/> class.
    /// </summary>
    /// <param name="factory">The factory used to create metrics.</param>
    /// <param name="clock">An optional time provider; uses UTC time by default.</param>
    /// <exception cref="ArgumentNullException">Thrown if the factory is null.</exception>
    public GcInfoCollector(IMetricFactory factory, ITimeProvider? clock = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _clock = clock ?? new UtcTimeProvider();
        _lastTsUtc = default;
        _hasBaseline = false;
    }

    /// <summary>
    /// Creates a summary metric that aggregates GC collection data for each generation.
    /// </summary>
    /// <param name="id">The identifier for the summary metric.</param>
    /// <param name="name">The name for the summary metric.</param>
    /// <param name="quantiles">The quantiles for the summary (default: 0.5, 0.9, 0.99).</param>
    /// <param name="tags">Optional tags to associate with the summary metric.</param>
    /// <param name="resetOnGet">Indicates if the summary should reset when accessed (not used in this context).</param>
    /// <returns>A built summary metric.</returns>
    public ISummaryMetric CreateSummary(string id, string name, IEnumerable<double> quantiles, IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
    {
        var q = quantiles?.ToArray() ?? new[] { 0.5, 0.9, 0.99 };
        var builder = _factory.Summary(id, name).WithQuantiles(q);

        if (tags != null)
        {
            foreach (var kv in tags)
            {
                builder.WithTag(kv.Key, kv.Value);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates a bucket histogram metric that tracks GC collections per second.
    /// </summary>
    /// <param name="id">The identifier for the histogram metric.</param>
    /// <param name="name">The name for the histogram metric.</param>
    /// <param name="bucketUpperBounds">The upper bounds for the histogram buckets.</param>
    /// <param name="tags">Optional tags to associate with the histogram metric.</param>
    /// <returns>A built bucket histogram metric.</returns>
    public IBucketHistogramMetric CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
    {
        var bounds = bucketUpperBounds?.ToArray() ?? Array.Empty<double>();
        var builder = _factory.Histogram(id, name).WithBounds(bounds);

        if (tags != null)
        {
            foreach (var kv in tags)
            {
                builder.WithTag(kv.Key, kv.Value);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Collects GC statistics and generates a multi-gauge metric for the GC information.
    /// The collected data includes total GC collections per generation, 
    /// collections per second, and memory statistics (pause time, heap size, fragmentation).
    /// </summary>
    /// <param name="ct">A cancellation token to allow task cancellation.</param>
    /// <returns>A task that represents the asynchronous operation, 
    /// containing the generated metrics as the result.</returns>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        const string id = "gc.info";
        const string name = ".NET GC Info";

        try
        {
            ct.ThrowIfCancellationRequested();

            var mg = _factory.MultiGauge(id, name).WithResetOnGet(true).Build();

            // Collect the total number of collections for each generation
            long g0 = GC.CollectionCount(0);
            long g1 = GC.CollectionCount(1);
            long g2 = GC.CollectionCount(2);

            mg.SetValue(g0, Tag("collections", gen: "0"));
            mg.SetValue(g1, Tag("collections", gen: "1"));
            mg.SetValue(g2, Tag("collections", gen: "2"));

            // Calculate collections per second
            var now = _clock.UtcNow;

            if (_hasBaseline)
            {
                var dtSec = Math.Max(0.001, (now - _lastTsUtc).TotalSeconds);

                mg.SetValue((g0 - _lastTotalCollectionsGen0) / dtSec, Tag("collections_per_sec", gen: "0"));
                mg.SetValue((g1 - _lastTotalCollectionsGen1) / dtSec, Tag("collections_per_sec", gen: "1"));
                mg.SetValue((g2 - _lastTotalCollectionsGen2) / dtSec, Tag("collections_per_sec", gen: "2"));
            }

            _lastTotalCollectionsGen0 = g0;
            _lastTotalCollectionsGen1 = g1;
            _lastTotalCollectionsGen2 = g2;
            _lastTsUtc = now;
            _hasBaseline = true;

            // Collect GC memory information
            var gi = GC.GetGCMemoryInfo();

            mg.SetValue(gi.PauseTimePercentage, Tag(kind: "pause.percent"));
            mg.SetValue(gi.HeapSizeBytes, Tag(kind: "heap.size.bytes"));
            mg.SetValue(gi.FragmentedBytes, Tag(kind: "heap.fragmented.bytes"));

            return Task.FromResult<IMetric?>(mg);
        }
        catch (OperationCanceledException)
        {
            var mg = _factory.MultiGauge(id, name).WithResetOnGet(true).Build();

            mg.SetValue(0, new Dictionary<string, string> { ["status"] = "cancelled" });

            return Task.FromResult<IMetric?>(mg);
        }
        catch (Exception ex)
        {
            var mg = _factory.MultiGauge(id, name).WithResetOnGet(true).Build();

            mg.SetValue(0, new Dictionary<string, string>
            {
                ["status"] = "error",
                ["error"] = ex.GetType().Name,
                ["reason"] = Short(ex.Message)
            });

            return Task.FromResult<IMetric?>(mg);

            throw;
        }
    }

    /// <summary>
    /// Creates a dictionary of tags for metric collection.
    /// </summary>
    /// <param name="kind">Logical kind of the metric (e.g., "collections", "pause.percent").</param>
    /// <param name="gen">Optional GC generation label (e.g., "0", "1", "2").</param>
    /// <returns>
    /// A mutable <see cref="Dictionary{TKey,TValue}"/> pre-sized for expected entries,
    /// using <see cref="StringComparer.Ordinal"/> for key comparisons.
    /// </returns>
    private static Dictionary<string, string> Tag(string kind, string? gen = null)
    {
        // capacity: kind + status + optional gen
        var dict = new Dictionary<string, string>(gen is null ? 2 : 3, StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["status"] = "ok"
        };

        if (!string.IsNullOrEmpty(gen))
        {
            dict["gen"] = gen;
        }

        return dict;
    }

    /// <summary>
    /// Shortens a string to 160 characters for logging purposes.
    /// </summary>
    /// <param name="s">The string to shorten.</param>
    /// <returns>The shortened string.</returns>
    private static string Short(string s)
    {
        return string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
    }
}
