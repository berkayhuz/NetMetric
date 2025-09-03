// <copyright file="IAzureMonitorClient.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.AzureMonitor.Abstractions;

/// <summary>
/// Defines an asynchronous contract for sending metric telemetry to Azure Monitor,
/// including single-sample metrics and aggregate metrics with distribution details,
/// and for flushing any buffered telemetry.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are expected to be thread-safe and efficient under high-throughput scenarios.
/// Methods accept a <see cref="System.Threading.CancellationToken"/> to allow cooperative cancellation.
/// </para>
/// <para>
/// Metric identity is formed primarily by the metric name and the associated tags.
/// Tag sets should be stable and low-cardinality to avoid cardinality explosions in backends.
/// </para>
/// <para>
/// This interface does not prescribe validation rules, but typical implementations may
/// reject null or empty metric names, or inconsistent aggregate values (e.g., <c>min &gt; max</c>).
/// See the documentation on each method for recommended constraints.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Resolve from DI:
/// var client = serviceProvider.GetRequiredService<IAzureMonitorClient>();
///
/// // Track a single gauge-like value:
/// await client.TrackMetricAsync(
///     name: "app.requests.active",
///     value: 42,
///     tags: new Dictionary<string, string> { ["service"] = "checkout", ["env"] = "prod" },
///     unit: "count",
///     description: "Number of in-flight HTTP requests",
///     ct: cancellationToken);
///
/// // Track an aggregate (e.g., a 1-minute window):
/// await client.TrackMetricAggregateAsync(
///     name: "app.request.duration",
///     count: 1200,
///     min: 0.001,
///     max: 2.345,
///     p50: 0.040,
///     p90: 0.120,
///     p99: 0.850,
///     sum: 56.7,
///     buckets: new double[] { 0.01, 0.05, 0.1, 0.5, 1.0, 2.5 },
///     counts:  new long[]   {   80,  300, 400, 350,  60,   10 },
///     tags: new Dictionary<string, string> { ["service"] = "checkout", ["env"] = "prod" },
///     unit: "s",
///     description: "HTTP request latency (1-minute rollup)",
///     ct: cancellationToken);
///
/// // Ensure low-latency delivery when shutting down:
/// await client.FlushAsync(ct: cancellationToken);
/// ]]></code>
/// </example>
public interface IAzureMonitorClient : IAsyncDisposable
{
    /// <summary>
    /// Tracks a single-sample metric value with optional dimensional tags, unit, and description.
    /// </summary>
    /// <param name="name">
    /// The metric name. Recommended: use dotted namespaces (e.g., <c>app.requests.active</c>).
    /// Must be non-empty and reasonably short.
    /// </param>
    /// <param name="value">
    /// The numeric metric value (e.g., a gauge or counter delta). Implementations may impose
    /// domain-specific constraints (e.g., counters should be non-negative).
    /// </param>
    /// <param name="tags">
    /// A read-only dictionary of tag key/value pairs to associate with the metric. Keys and values should be ASCII,
    /// stable, and low-cardinality. Pass an empty dictionary if no tags are needed.
    /// </param>
    /// <param name="unit">
    /// Optional unit (e.g., <c>count</c>, <c>ms</c>, <c>s</c>, <c>bytes</c>). If <see langword="null"/>,
    /// the backend's default or an implementation-specific unit may apply.
    /// </param>
    /// <param name="description">
    /// Optional human-readable description for diagnostics and dashboards. Not used for identity.
    /// </param>
    /// <param name="ct">
    /// A token to observe for cancellation. If signaled before or during the operation, the returned task
    /// may complete in a canceled state.
    /// </param>
    /// <returns>
    /// A <see cref="System.Threading.Tasks.ValueTask"/> that represents the asynchronous operation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Typical validations (implementation-dependent):
    /// </para>
    /// <list type="bullet">
    /// <item><description><paramref name="name"/> must not be <see langword="null"/> or empty.</description></item>
    /// <item><description><paramref name="tags"/> must not be <see langword="null"/> (use an empty dictionary).</description></item>
    /// <item><description>Tag keys/values may be trimmed or rejected if they exceed implementation limits.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="System.ArgumentNullException">
    /// May be thrown if <paramref name="name"/> or <paramref name="tags"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown if <paramref name="ct"/> is canceled before or during the operation.
    /// </exception>
    ValueTask TrackMetricAsync(
        string name,
        double value,
        IReadOnlyDictionary<string, string> tags,
        string? unit,
        string? description,
        CancellationToken ct = default);

    /// <summary>
    /// Tracks an aggregate metric with distribution statistics (count, extrema, percentiles, sum) and optional histogram buckets.
    /// </summary>
    /// <param name="name">The aggregate metric name. Must be non-empty.</param>
    /// <param name="count">The total number of samples represented by this aggregate. Recommended: <c>&gt;= 0</c>.</param>
    /// <param name="min">The minimum observed value in the window.</param>
    /// <param name="max">The maximum observed value in the window. Should be <c>&gt;=</c> <paramref name="min"/>.</param>
    /// <param name="p50">Optional 50th percentile. If supplied, should lie within <paramref name="min"/> and <paramref name="max"/>.</param>
    /// <param name="p90">Optional 90th percentile.</param>
    /// <param name="p99">Optional 99th percentile.</param>
    /// <param name="sum">Optional sum of all samples in the window. If supplied, should be consistent with <paramref name="count"/>.</param>
    /// <param name="buckets">
    /// Optional monotonically increasing bucket boundaries used to describe a histogram (e.g., <c>[0.01, 0.05, 0.1, ...]</c>).
    /// May be <see langword="null"/> if histogram data is not available.
    /// </param>
    /// <param name="counts">
    /// Optional per-bucket counts aligned with <paramref name="buckets"/>; lengths should match when both are provided.
    /// May be <see langword="null"/>.
    /// </param>
    /// <param name="tags">A read-only dictionary of dimensional tags. Use an empty dictionary if not needed.</param>
    /// <param name="unit">Optional unit (e.g., <c>s</c>, <c>ms</c>, <c>bytes</c>, <c>count</c>).</param>
    /// <param name="description">Optional human-readable description for diagnostics and dashboards.</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>A <see cref="System.Threading.Tasks.ValueTask"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// Typical consistency checks (implementation-dependent):
    /// </para>
    /// <list type="bullet">
    /// <item><description><paramref name="count"/> should be non-negative; zero is allowed.</description></item>
    /// <item><description><paramref name="min"/> should be less than or equal to <paramref name="max"/> when <paramref name="count"/> &gt; 0.</description></item>
    /// <item><description>When provided, <paramref name="sum"/> should be consistent with the distribution and <paramref name="count"/>.</description></item>
    /// <item><description>If both <paramref name="buckets"/> and <paramref name="counts"/> are provided, their lengths should match.</description></item>
    /// <item><description>Percentiles (if provided) should lie within <paramref name="min"/> and <paramref name="max"/>.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="System.ArgumentNullException">
    /// May be thrown if <paramref name="name"/> or <paramref name="tags"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.ArgumentException">
    /// May be thrown if <paramref name="buckets"/> and <paramref name="counts"/> lengths differ when both are provided,
    /// or if <paramref name="name"/> is empty.
    /// </exception>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// May be thrown if <paramref name="count"/> is negative, or if <paramref name="min"/> &gt; <paramref name="max"/> for non-zero counts.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown if <paramref name="ct"/> is canceled before or during the operation.
    /// </exception>
    ValueTask TrackMetricAggregateAsync(
        string name,
        long count,
        double min,
        double max,
        double? p50,
        double? p90,
        double? p99,
        double? sum,
        IReadOnlyList<double>? buckets,
        IReadOnlyList<long>? counts,
        IReadOnlyDictionary<string, string> tags,
        string? unit,
        string? description,
        CancellationToken ct = default);

    /// <summary>
    /// Flushes any buffered telemetry to Azure Monitor.
    /// </summary>
    /// <param name="ct">
    /// A token to observe for cancellation during the flush operation.
    /// </param>
    /// <returns>
    /// A <see cref="System.Threading.Tasks.ValueTask"/> that completes when in-flight telemetry is persisted
    /// (subject to the implementation’s buffering semantics).
    /// </returns>
    /// <remarks>
    /// <para>
    /// Call this method before application shutdown to reduce data loss and latency.
    /// Implementations should attempt to drain internal buffers and send any pending telemetry.
    /// </para>
    /// </remarks>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown if <paramref name="ct"/> is canceled before or during the operation.
    /// </exception>
    ValueTask FlushAsync(CancellationToken ct = default);
}
