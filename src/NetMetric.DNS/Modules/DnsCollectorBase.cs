// <copyright file="DnsCollectorBase.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.DNS.Modules;

/// <summary>
/// Provides a reusable base for DNS-related metric collectors.
/// </summary>
/// <remarks>
/// <para>
/// This abstract base wires common concerns shared by DNS collectors:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>Access to an <see cref="IMetricFactory"/> to build metrics in a consistent manner.</description>
///   </item>
///   <item>
///     <description>Access to <see cref="Options"/> to read DNS-specific configuration (probe hostnames, timeouts, etc.).</description>
///   </item>
///   <item>
///     <description>Helper implementations of <see cref="IMetricCollector.CreateSummary"/> and
///     <see cref="IMetricCollector.CreateBucketHistogram"/> so derived collectors can focus on collection logic.</description>
///   </item>
/// </list>
/// <para>
/// Derive from this type and implement <see cref="CollectAsync(CancellationToken)"/> with your
/// concrete DNS probing logic (e.g., A/AAAA record resolution latency, NXDOMAIN rate, etc.).
/// </para>
/// </remarks>
/// <seealso cref="IMetricCollector"/>
internal abstract class DnsCollectorBase : IMetricCollector
{
    /// <summary>
    /// Default quantile set used when the caller does not provide one.
    /// </summary>
    /// <remarks>
    /// Kept as a <see langword="static readonly"/> array to avoid repeated allocations on hot paths.
    /// </remarks>
    private static readonly double[] DefaultQuantiles = new[] { 0.5, 0.9, 0.99 };

    /// <summary>
    /// Gets the metric factory used to create summary and histogram metrics.
    /// </summary>
    /// <remarks>
    /// Injected via the constructor and guaranteed to be non-<see langword="null"/>.
    /// </remarks>
    protected IMetricFactory Factory { get; }

    /// <summary>
    /// Gets the DNS collector options (probe hosts, resolve timeouts, etc.).
    /// </summary>
    /// <remarks>
    /// Injected via the constructor and guaranteed to be non-<see langword="null"/>.
    /// </remarks>
    protected Options.DnsOptions Options { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsCollectorBase"/> class.
    /// </summary>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to build metrics.</param>
    /// <param name="options">DNS-related configuration for the collector.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="factory"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    protected DnsCollectorBase(IMetricFactory factory, Options.DnsOptions options)
    {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Collects DNS metrics asynchronously.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A task that, when completed, yields the produced metric, or <see langword="null"/> when no metric is available
    /// for the current cycle (e.g., the probe is intentionally skipped).
    /// </returns>
    /// <remarks>
    /// Implementations should be resilient to transient DNS failures and prefer returning <see langword="null"/>
    /// when appropriate rather than throwing, unless an unrecoverable error occurs.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// internal sealed class DnsARecordLatencyCollector : DnsCollectorBase
    /// {
    ///     public DnsARecordLatencyCollector(IMetricFactory factory, Options.DnsOptions options)
    ///         : base(factory, options) { }
    ///
    ///     public override async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    ///     {
    ///         // Resolve A record(s), measure duration, and publish as a summary or histogram.
    ///         // Return the metric instance or null if nothing to report.
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    public abstract Task<IMetric?> CollectAsync(CancellationToken ct = default);

    // ---------------------------------------------------------------------
    // Explicit IMetricCollector helper implementations
    // ---------------------------------------------------------------------

    /// <summary>
    /// Creates a summary metric using the provided quantiles and optional tags.
    /// </summary>
    /// <param name="id">A unique identifier for the metric (stable across process restarts).</param>
    /// <param name="name">A human-readable metric name suitable for query/visualization.</param>
    /// <param name="quantiles">
    /// The quantiles to publish (e.g., 0.5, 0.9, 0.99). If <see langword="null"/>, defaults to
    /// <c>{ 0.5, 0.9, 0.99 }</c>.
    /// </param>
    /// <param name="tags">Optional key/value tags to attach to the metric series.</param>
    /// <param name="resetOnGet">
    /// Reserved parameter for future use. The current builder does not support per-read reset semantics.
    /// </param>
    /// <returns>An <see cref="ISummaryMetric"/> instance built via <see cref="IMetricFactory"/>.</returns>
    /// <remarks>
    /// This is an explicit interface implementation to keep the public surface of the base class minimal.
    /// </remarks>
    ISummaryMetric IMetricCollector.CreateSummary(
        string id,
        string name,
        IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags,
        bool resetOnGet)
    {
        var q = quantiles as double[] ?? quantiles?.ToArray() ?? DefaultQuantiles;

        var sb = Factory
            .Summary(id, name)
            .WithQuantiles(q);

        // Note: resetOnGet is currently ignored because ISummaryBuilder does not expose such capability.

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
    /// Creates a bucketed histogram metric using the provided upper bounds and optional tags.
    /// </summary>
    /// <param name="id">A unique identifier for the metric (stable across process restarts).</param>
    /// <param name="name">A human-readable metric name suitable for query/visualization.</param>
    /// <param name="bucketUpperBounds">
    /// Upper bounds for histogram buckets. If <see langword="null"/>, an empty bound set is used.
    /// </param>
    /// <param name="tags">Optional key/value tags to attach to the metric series.</param>
    /// <returns>An <see cref="IBucketHistogramMetric"/> instance built via <see cref="IMetricFactory"/>.</returns>
    /// <remarks>
    /// Bounds are materialized with <see cref="Enumerable.ToArray{TSource}(IEnumerable{TSource})"/>
    /// to ensure the builder receives a stable snapshot.
    /// </remarks>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id,
        string name,
        IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
    {
        var bounds = (bucketUpperBounds ?? Array.Empty<double>()).ToArray();

        var hb = Factory
            .Histogram(id, name)
            .WithBounds(bounds);

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
