// <copyright file="AllProcessesCpuUsageCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Collects per-process CPU usage percentages across the host.
/// </summary>
/// <remarks>
/// <para>
/// This collector enumerates all operating-system processes on each collection cycle and emits one or more
/// gauge samples per process. For each process that has been observed at least once before, the collector computes:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <c>cpu.process.percent</c> — the instantaneous CPU utilization percentage for a process, normalized by the
///     number of logical CPU cores (<see cref="Environment.ProcessorCount"/>).
///     </description>
///   </item>
/// </list>
/// <para>
/// Because it calls <see cref="Process.GetProcesses()"/> and reads <see cref="Process.TotalProcessorTime"/> for each
/// process, this is a <b>heavyweight</b> operation. Prefer longer collection intervals (e.g., ≥ 10s) in production,
/// or sample selectively if you have a large number of processes.
/// </para>
/// <para>
/// The collector maintains an internal cache of the last seen CPU time and timestamp per process ID in order to
/// compute deltas between successive runs. Entries for processes that have exited are removed automatically after
/// each cycle.
/// </para>
/// <para>
/// <b>Thread-safety:</b> access to the internal cache is protected by a private lock to allow the collector to be
/// invoked concurrently. The returned metric instance is created via <see cref="IMetricFactory"/> and its own
/// thread-safety characteristics apply beyond this collector.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// // Register and use in a module
/// IMetricFactory factory = ...;
/// ITimeProvider clock = new UtcTimeProvider();
/// var collector = new AllProcessesCpuUsageCollector(factory, clock);
///
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
/// IMetric? metric = await collector.CollectAsync(cts.Token);
/// // Expose 'metric' via your metrics endpoint
/// </code>
/// </example>
public sealed class AllProcessesCpuUsageCollector : IMetricCollector
{
    private readonly object _lock = new object();
    private readonly Dictionary<int, (TimeSpan cpu, DateTime ts)> _last = new();
    private readonly int _cores = Environment.ProcessorCount;
    private readonly IMetricFactory _factory;
    private readonly ITimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="AllProcessesCpuUsageCollector"/> class.
    /// </summary>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to create metric instances.</param>
    /// <param name="clock">
    /// Optional time provider used to timestamp samples; when <see langword="null"/>, a
    /// <see cref="UtcTimeProvider"/> is created and used.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public AllProcessesCpuUsageCollector(IMetricFactory factory, ITimeProvider? clock = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _clock = clock ?? new UtcTimeProvider();
    }

#pragma warning disable CA1031
    /// <summary>
    /// Collects per-process CPU usage metrics asynchronously.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method emits a multi-gauge metric with one sample per process when prior state exists to compute a delta.
    /// Samples include the following tags:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>pid</c> — the process ID.</description></item>
    ///   <item><description><c>name</c> — the process name (or <c>unknown</c> when unavailable).</description></item>
    ///   <item><description><c>status</c> — <c>ok</c>, <c>empty</c>, <c>cancelled</c>, or <c>error</c>.</description></item>
    /// </list>
    /// <para>
    /// When the collector cannot access process information (e.g., due to permissions), it emits an error sample with
    /// <c>status=error</c> and a concise <c>reason</c>. The collector attempts to continue processing remaining processes.
    /// </para>
    /// <para>
    /// If no sample could be produced (e.g., on the very first run), a single <c>status=empty</c> sample with value <c>0</c> is emitted.
    /// </para>
    /// </remarks>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation.</param>
    /// <returns>
    /// A task that, when completed, yields an <see cref="IMetric"/> instance (or <see langword="null"/> if the factory returns null).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        const string id = "cpu.process.percent";
        const string name = "Per-Process CPU Usage %";

        try
        {
            ct.ThrowIfCancellationRequested();

            var now = _clock.UtcNow;
            var mg = _factory
                .MultiGauge(id, name)
                .WithResetOnGet(true)
                .Build();

            Process[] procs;

            // Obtain the process list; emit a single error sample if this fails.
            try
            {
                procs = Process.GetProcesses();
            }
            catch (UnauthorizedAccessException ex)
            {
                var error = new Dictionary<string, string>
                {
                    ["status"] = "error",
                    ["reason"] = $"Access denied: {ex.Message}"
                };

                mg.SetValue(0, error);
                return Task.FromResult<IMetric?>(mg);
            }
            catch (AccessViolationException ex)
            {
                var error = new Dictionary<string, string>
                {
                    ["status"] = "error",
                    ["reason"] = $"Memory access error: {ex.Message}"
                };

                mg.SetValue(0, error);
                return Task.FromResult<IMetric?>(mg);
            }
            catch (IOException ex)
            {
                var error = new Dictionary<string, string>
                {
                    ["status"] = "error",
                    ["reason"] = $"I/O error: {ex.Message}"
                };

                mg.SetValue(0, error);
                return Task.FromResult<IMetric?>(mg);
            }
            catch (InvalidOperationException ex)
            {
                var error = new Dictionary<string, string>
                {
                    ["status"] = "error",
                    ["reason"] = $"Invalid operation: {ex.Message}"
                };

                mg.SetValue(0, error);
                return Task.FromResult<IMetric?>(mg);
            }

            var added = 0;

            try
            {
                foreach (var p in procs)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var cpu = p.TotalProcessorTime;
                        var ts = now;

                        TimeSpan prevCpu;
                        DateTime prevTs;
                        bool hasPrev;

                        lock (_lock)
                        {
                            hasPrev = _last.TryGetValue(p.Id, out var prev);

                            if (hasPrev)
                            {
                                prevCpu = prev.cpu;
                                prevTs = prev.ts;
                            }
                            else
                            {
                                prevCpu = TimeSpan.Zero;
                                prevTs = ts;
                            }

                            _last[p.Id] = (cpu, ts);
                        }

                        if (hasPrev)
                        {
                            var dCpu = cpu - prevCpu;
                            var dWall = ts - prevTs;
                            var pct = dWall.TotalSeconds > 0
                                ? (dCpu.TotalSeconds / (dWall.TotalSeconds * _cores)) * 100.0
                                : 0.0;

                            mg.SetValue(
                                Math.Clamp(pct, 0, 100),
                                new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    ["pid"] = p.Id.ToString(),
                                    ["name"] = SafeName(p.ProcessName),
                                    ["status"] = "ok"
                                });

                            added++;
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Process likely exited or handle is no longer valid; emit an error sample and continue.
                        var error = new Dictionary<string, string>
                        {
                            ["status"] = "error",
                            ["reason"] = $"Invalid operation: {ex.Message}"
                        };

                        mg.SetValue(0, error);
                    }
                    catch (Exception ex)
                    {
                        // Emit a concise error and rethrow to be handled by the outer catch for visibility/observability.
                        var error = new Dictionary<string, string>
                        {
                            ["status"] = "error",
                            ["reason"] = Short(ex.Message)
                        };

                        mg.SetValue(0, error);
                        throw;
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }

                // Remove dead processes from the cache.
                var alive = procs.Select(x => x.Id).ToHashSet();

                lock (_lock)
                {
                    var dead = _last.Keys.Where(id2 => !alive.Contains(id2)).ToList();
                    foreach (var id2 in dead)
                    {
                        _last.Remove(id2);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                mg.SetValue(0, new Dictionary<string, string> { ["status"] = "cancelled" });
            }
            catch (Exception ex)
            {
                var error = new Dictionary<string, string>
                {
                    ["status"] = "error",
                    ["reason"] = Short(ex.Message)
                };

                mg.SetValue(0, error);
                throw;
            }

            if (added == 0)
            {
                mg.SetValue(0, new Dictionary<string, string> { ["status"] = "empty" });
            }

            return Task.FromResult<IMetric?>(mg);
        }
        catch (Exception ex)
        {
            // Final safety net: always return a metric indicating failure.
            var mg = _factory.MultiGauge(id, name).WithResetOnGet(true).Build();

            mg.SetValue(0, new Dictionary<string, string>
            {
                ["status"] = "error",
                ["reason"] = Short(ex.Message)
            });

            return Task.FromResult<IMetric?>(mg);
        }
    }
#pragma warning restore CA1031

    /// <summary>
    /// Creates a summary metric via the underlying <see cref="IMetricFactory"/>.
    /// </summary>
    /// <param name="id">The unique metric identifier.</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="quantiles">Quantiles to be configured on the summary. Defaults to <c>{0.5, 0.9, 0.99}</c> when <see langword="null"/>.</param>
    /// <param name="tags">Optional static tags applied to the resulting metric.</param>
    /// <param name="resetOnGet">Whether the metric should be reset on each retrieval (passed to the builder if supported).</param>
    /// <returns>An <see cref="ISummaryMetric"/> instance.</returns>
    /// <remarks>
    /// This is a convenience implementation for <see cref="IMetricCollector.CreateSummary(string, string, IEnumerable{double}, IReadOnlyDictionary{string, string}?, bool)"/> that delegates to the factory.
    /// </remarks>
    ISummaryMetric IMetricCollector.CreateSummary(string id, string name, IEnumerable<double> quantiles, IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
    {
        var q = quantiles?.ToArray() ?? new[] { 0.5, 0.9, 0.99 };
        var sb = _factory.Summary(id, name).WithQuantiles(q);

        if (tags is not null)
        {
            foreach (var kv in tags)
            {
                sb.WithTag(kv.Key, kv.Value);
            }
        }

        return sb.Build();
    }

    /// <summary>
    /// Creates a bucketed histogram metric via the underlying <see cref="IMetricFactory"/>.
    /// </summary>
    /// <param name="id">The unique metric identifier.</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="bucketUpperBounds">Upper bounds (exclusive) for histogram buckets. When <see langword="null"/>, an empty set is used.</param>
    /// <param name="tags">Optional static tags applied to the resulting metric.</param>
    /// <returns>An <see cref="IBucketHistogramMetric"/> instance.</returns>
    /// <remarks>
    /// This is a convenience implementation for <see cref="IMetricCollector.CreateBucketHistogram(string, string, IEnumerable{double}, IReadOnlyDictionary{string, string}?)"/> that delegates to the factory.
    /// </remarks>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
    {
        var bounds = bucketUpperBounds?.ToArray() ?? Array.Empty<double>();
        var hb = _factory.Histogram(id, name).WithBounds(bounds);

        if (tags is not null)
        {
            foreach (var kv in tags)
            {
                hb.WithTag(kv.Key, kv.Value);
            }
        }

        return hb.Build();
    }

    // ---------- Internals ----------

    /// <summary>
    /// Returns a shortened representation of the specified string (up to 160 characters).
    /// </summary>
    /// <param name="s">The string to shorten.</param>
    /// <returns>
    /// <paramref name="s"/> unchanged when its length is ≤ 160; otherwise the first 160 characters.
    /// Returns <see cref="string.Empty"/> when <paramref name="s"/> is <see langword="null"/> or empty.
    /// </returns>
    private static string Short(string s)
    {
        return string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
    }

    /// <summary>
    /// Normalizes a process name for tagging.
    /// </summary>
    /// <param name="n">The raw process name, which may be <see langword="null"/> or whitespace.</param>
    /// <returns>
    /// <c>"unknown"</c> when <paramref name="n"/> is <see langword="null"/> or whitespace; otherwise <paramref name="n"/>.
    /// </returns>
    private static string SafeName(string? n)
    {
        return string.IsNullOrWhiteSpace(n) ? "unknown" : n!;
    }
}
