// <copyright file="CpuCoreCountCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Collects the number of <em>logical</em> CPU cores visible to the current process and exposes it as a gauge metric.
/// </summary>
/// <remarks>
/// <para>
/// This collector reads <see cref="Environment.ProcessorCount"/>, which reflects the logical processors that are available to the
/// process at runtime (e.g., after CPU affinity constraints or container limits are applied).
/// </para>
/// <para>
/// The collector emits a single gauge metric with the identifier <c>cpu.cores.logical</c> and name
/// <c>"Logical CPU Core Count"</c>. On success the metric is tagged with <c>status=ok</c> and set to the
/// detected core count. If the operation is canceled, it emits the same metric with <c>status=cancelled</c> and value <c>0</c>.
/// If an unexpected error occurs, it emits <c>status=error</c>, adds a short <c>reason</c> tag, and sets the value to <c>0</c>.
/// </para>
/// <para>
/// <strong>Thread-safety:</strong> The collector itself is stateless and therefore thread-safe. The underlying metric factory
/// is expected to be safe for concurrent use according to its own contract.
/// </para>
/// <para>
/// <strong>Performance:</strong> Collection performs a single property read and a gauge update. The operation is O(1) and
/// suitable for frequent polling.
/// </para>
/// </remarks>
/// <example>
/// The following example registers and runs the collector, then reads the emitted gauge value:
/// <code language="csharp">
/// IMetricFactory factory = /* obtain from DI */;
/// var collector = new CpuCoreCountCollector(factory);
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
/// var metric = await collector.CollectAsync(cts.Token).ConfigureAwait(false);
/// // metric is a gauge with id "cpu.cores.logical"; value equals Environment.ProcessorCount on success
/// </code>
/// </example>
/// <seealso cref="Environment.ProcessorCount"/>
/// <seealso cref="IMetricFactory"/>
public sealed class CpuCoreCountCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuCoreCountCollector"/> class.
    /// </summary>
    /// <param name="factory">The factory used to create metric instances.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public CpuCoreCountCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

#pragma warning disable CA1031
    /// <summary>
    /// Collects the logical CPU core count asynchronously and emits it as a gauge metric.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A task that returns the produced <see cref="IMetric"/> instance (a gauge with id <c>cpu.cores.logical</c>),
    /// or <see langword="null"/> if the factory declines to build the metric.
    /// </returns>
    /// <remarks>
    /// On success, the gauge value is set to <see cref="Environment.ProcessorCount"/> and tagged with <c>status=ok</c>.
    /// On cancellation, the gauge is emitted with <c>status=cancelled</c> and value <c>0</c>.
    /// On error, the gauge is emitted with <c>status=error</c>, a short <c>reason</c> tag, and value <c>0</c>.
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        const string id = "cpu.cores.logical";
        const string name = "Logical CPU Core Count";

        try
        {
            ct.ThrowIfCancellationRequested();

            var gb = _factory.Gauge(id, name).WithTag("status", "ok");
            var g = gb.Build();
            g.SetValue(Environment.ProcessorCount);
            return Task.FromResult<IMetric?>(g);
        }
        catch (OperationCanceledException)
        {
            var g = _factory.Gauge(id, name).WithTag("status", "cancelled").Build();
            g.SetValue(0);
            return Task.FromResult<IMetric?>(g);
        }
        catch (Exception ex)
        {
            var g = _factory
                .Gauge(id, name)
                .WithTag("status", "error")
                .WithTag("reason", Short(ex.Message))
                .Build();

            g.SetValue(0);
            return Task.FromResult<IMetric?>(g);

            // NOTE: Any code after the return is unreachable by design.
        }

        static string Short(string s)
        {
            return string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
        }
    }
#pragma warning restore CA1031
    // ---- Explicit IMetricCollector helper methods ----

    /// <summary>
    /// Creates a summary metric builder preconfigured with the provided quantiles and optional tags, then builds it.
    /// </summary>
    /// <param name="id">The metric identifier (e.g., <c>cpu.collect.latency_ms</c>).</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="quantiles">The quantiles to export (e.g., <c>0.5</c>, <c>0.9</c>, <c>0.99</c>). If <see langword="null"/>, defaults to <c>[0.5, 0.9, 0.99]</c>.</param>
    /// <param name="tags">Optional key/value tags to associate with the metric builder.</param>
    /// <param name="resetOnGet">When <see langword="true"/>, indicates a reset-on-read semantic. This parameter is accepted for API symmetry but ignored by this implementation.</param>
    /// <returns>A constructed <see cref="ISummaryMetric"/> instance.</returns>
    /// <example>
    /// <code language="csharp">
    /// var summary = collector.CreateSummary(
    ///     id: "cpu.collect.duration_ms",
    ///     name: "CPU Collect Duration",
    ///     quantiles: new[] { 0.5, 0.9, 0.99 },
    ///     tags: new Dictionary&lt;string,string&gt; { ["collector"] = "cpu.corecount" },
    ///     resetOnGet: false);
    /// summary.Observe(4.2);
    /// </code>
    /// </example>
    public ISummaryMetric CreateSummary(string id, string name, IEnumerable<double> quantiles, IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
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
    /// Creates a bucketed histogram metric builder with the provided upper bounds and optional tags, then builds it.
    /// </summary>
    /// <param name="id">The metric identifier (e.g., <c>cpu.collect.requests</c>).</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="bucketUpperBounds">A non-decreasing sequence of inclusive upper bounds for buckets. If <see langword="null"/>, an empty histogram is created.</param>
    /// <param name="tags">Optional key/value tags to associate with the metric builder.</param>
    /// <returns>A constructed <see cref="IBucketHistogramMetric"/> instance.</returns>
    /// <example>
    /// <code language="csharp">
    /// var hist = collector.CreateBucketHistogram(
    ///     id: "cpu.sample.bytes",
    ///     name: "CPU Sample Size",
    ///     bucketUpperBounds: new[] { 128d, 256d, 512d, 1024d },
    ///     tags: new Dictionary&lt;string,string&gt; { ["unit"] = "bytes" });
    /// hist.Observe(300);
    /// </code>
    /// </example>
    public IBucketHistogramMetric CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
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
}
