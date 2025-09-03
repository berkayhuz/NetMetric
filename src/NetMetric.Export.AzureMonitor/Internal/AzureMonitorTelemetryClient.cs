// <copyright file="AzureMonitorTelemetryClient.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Globalization;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace NetMetric.Export.AzureMonitor.Internal;

/// <summary>
/// Provides a thin, reliable wrapper over <see cref="TelemetryClient"/> for sending metric
/// telemetry directly to Azure Monitor / Application Insights.
/// </summary>
/// <remarks>
/// <para>
/// This implementation focuses on metric emission scenarios and intentionally keeps
/// the API surface small and explicit. It supports:
/// </para>
/// <list type="bullet">
///   <item><description>Emitting single metrics with tags and optional unit/description.</description></item>
///   <item><description>Emitting aggregate metrics (min/max/percentiles/sum and optional bucketed histograms).</description></item>
///   <item><description>Sending pre-constructed <see cref="MetricTelemetry"/> instances.</description></item>
///   <item><description>Explicit flushing and graceful disposal.</description></item>
/// </list>
/// <para><b>Thread safety:</b> This type is safe to use concurrently from multiple threads. The underlying
/// <see cref="TelemetryClient"/> is designed for concurrent use and this wrapper does not maintain mutation-sensitive state.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Configure TelemetryClient (e.g., via DI in ASP.NET Core)
/// var telemetryClient = new TelemetryClient(new TelemetryConfiguration("<instrumentation-key-or-connection-string>"));
///
/// // Wrap it with AzureMonitorTelemetryClient
/// var directClient = new AzureMonitorTelemetryClient(telemetryClient);
///
/// // 1) Track a simple gauge metric with tags
/// await directClient.TrackMetricAsync(
///     name: "netmetric.export.queue_length",
///     value: 42,
///     tags: new Dictionary<string, string> { ["queue"] = "azure-monitor", ["env"] = "prod" },
///     unit: "items",
///     description: "Approximate items waiting in the exporter queue");
///
/// // 2) Track an aggregate metric with optional percentiles and histogram buckets
/// await directClient.TrackMetricAggregateAsync(
///     name: "netmetric.export.batch_size",
///     count: 200,
///     min: 1,
///     max: 250,
///     p50: 80,
///     p90: 180,
///     p99: 240,
///     sum: 12000,
///     buckets: new double[] { 10, 50, 100, 200, 300 },
///     counts:  new long[]   {  5, 60,  90,  40,   5 },
///     tags: new Dictionary<string, string> { ["component"] = "sender-worker" },
///     unit: "items",
///     description: "Distribution of batch sizes observed by the sender");
///
/// // 3) Track a pre-constructed MetricTelemetry
/// var mt = new MetricTelemetry("netmetric.export.duration_ms", 123.4);
/// mt.Properties["phase"] = "flush";
/// await directClient.TrackMetricTelemetryAsync(mt);
///
/// // Flush pending telemetry (non-blocking; see remarks)
/// await directClient.FlushAsync();
///
/// // Dispose gracefully
/// await directClient.DisposeAsync();
/// ]]></code>
/// </example>
/// <seealso cref="TelemetryClient"/>
internal sealed class AzureMonitorTelemetryClient : IAzureMonitorDirectClient
{
    private readonly TelemetryClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureMonitorTelemetryClient"/> class.
    /// </summary>
    /// <param name="client">A non-null <see cref="TelemetryClient"/> used to emit metrics.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public AzureMonitorTelemetryClient(TelemetryClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Tracks a single metric value with optional tags, unit, and description.
    /// </summary>
    /// <param name="name">The metric name. Use a stable, dot-separated name (e.g., <c>netmetric.export.queue_length</c>).</param>
    /// <param name="value">The metric value to record.</param>
    /// <param name="tags">Optional key/value pairs added as <see cref="MetricTelemetry.Properties"/> for filtering and slicing.</param>
    /// <param name="unit">Optional unit (e.g., <c>ms</c>, <c>items</c>, <c>bytes</c>). Stored as property <c>unit</c>.</param>
    /// <param name="description">Optional human-readable description. Stored as property <c>desc</c>.</param>
    /// <param name="ct">A cancellation token. Currently advisory only; the operation completes synchronously.</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    /// <remarks>
    /// <para>
    /// The metric is sent by creating a <see cref="MetricTelemetry"/> and forwarding it to
    /// <see cref="TelemetryClient.TrackMetric(MetricTelemetry)"/>. Tags are added to
    /// <see cref="MetricTelemetry.Properties"/>.
    /// </para>
    /// <para>
    /// <b>Namespacing best practice:</b> Prefer a consistent naming scheme (e.g., <c>company.product.area.metric_name</c>)
    /// to simplify discovery and dashboards.
    /// </para>
    /// </remarks>
    public ValueTask TrackMetricAsync(
        string name,
        double value,
        IReadOnlyDictionary<string, string> tags,
        string? unit,
        string? description,
        CancellationToken ct = default)
    {
        var mt = new MetricTelemetry(name, value);

        if (tags is { Count: > 0 })
        {
            foreach (var kv in tags)
            {
                mt.Properties[kv.Key] = kv.Value ?? string.Empty;
            }
        }

        if (!string.IsNullOrWhiteSpace(unit))
        {
            mt.Properties["unit"] = unit!;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            mt.Properties["desc"] = description!;
        }

        _client.TrackMetric(mt);

        _ = ct; // advisory only in current implementation
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Tracks an aggregate metric with basic statistics (min/max) and optional percentiles, sum,
    /// and histogram buckets with counts.
    /// </summary>
    /// <param name="name">The aggregate metric name.</param>
    /// <param name="count">The number of samples represented by the aggregate.</param>
    /// <param name="min">The minimum observed value.</param>
    /// <param name="max">The maximum observed value.</param>
    /// <param name="p50">Optional 50th percentile (median) value.</param>
    /// <param name="p90">Optional 90th percentile value.</param>
    /// <param name="p99">Optional 99th percentile value.</param>
    /// <param name="sum">Optional sum of all observed values.</param>
    /// <param name="buckets">
    /// Optional ascending bucket boundaries (e.g., <c>[10,50,100,200]</c>). If provided, should align with <paramref name="counts"/>.
    /// Serialized to the <c>buckets</c> property as a comma-separated list using invariant culture.
    /// </param>
    /// <param name="counts">
    /// Optional per-bucket sample counts (length should match <paramref name="buckets"/>).
    /// Serialized to the <c>counts</c> property as a comma-separated list.
    /// </param>
    /// <param name="tags">Optional key/value tags attached to the aggregate for filtering.</param>
    /// <param name="unit">Optional unit of measurement (stored as <c>unit</c>).</param>
    /// <param name="description">Optional description (stored as <c>desc</c>).</param>
    /// <param name="ct">A cancellation token. Currently advisory only; the operation completes synchronously.</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    /// <remarks>
    /// <para>
    /// The Azure Monitor data model does not provide a first-class aggregate histogram for custom metrics.
    /// To preserve fidelity while remaining queryable, this method stores aggregate fields in
    /// <see cref="MetricTelemetry.Properties"/> using the following keys:
    /// </para>
    /// <list type="table">
    ///   <listheader>
    ///     <term>Key</term><description>Meaning</description>
    ///   </listheader>
    ///   <item><term><c>agg.min</c></term><description>Minimum value</description></item>
    ///   <item><term><c>agg.max</c></term><description>Maximum value</description></item>
    ///   <item><term><c>agg.p50</c></term><description>50th percentile (if provided)</description></item>
    ///   <item><term><c>agg.p90</c></term><description>90th percentile (if provided)</description></item>
    ///   <item><term><c>agg.p99</c></term><description>99th percentile (if provided)</description></item>
    ///   <item><term><c>agg.sum</c></term><description>Sum of samples (if provided)</description></item>
    ///   <item><term><c>buckets</c></term><description>Comma-separated bucket boundaries</description></item>
    ///   <item><term><c>counts</c></term><description>Comma-separated per-bucket counts</description></item>
    ///   <item><term><c>unit</c></term><description>Unit string</description></item>
    ///   <item><term><c>desc</c></term><description>Description string</description></item>
    /// </list>
    /// <para>
    /// <b>Validation note:</b> This method does not enforce length equality for <paramref name="buckets"/> and
    /// <paramref name="counts"/> at runtime. For accurate analysis, ensure both are provided and of equal length.
    /// </para>
    /// </remarks>
    public ValueTask TrackMetricAggregateAsync(
        string name, long count, double min, double max,
        double? p50, double? p90, double? p99, double? sum,
        IReadOnlyList<double>? buckets, IReadOnlyList<long>? counts,
        IReadOnlyDictionary<string, string> tags,
        string? unit, string? description,
        CancellationToken ct = default)
    {
        var ci = CultureInfo.InvariantCulture;
        var mt = new MetricTelemetry(name, count);

        mt.Properties["agg.min"] = min.ToString(ci);
        mt.Properties["agg.max"] = max.ToString(ci);

        if (p50.HasValue) mt.Properties["agg.p50"] = p50.Value.ToString(ci);
        if (p90.HasValue) mt.Properties["agg.p90"] = p90.Value.ToString(ci);
        if (p99.HasValue) mt.Properties["agg.p99"] = p99.Value.ToString(ci);
        if (sum.HasValue) mt.Properties["agg.sum"] = sum.Value.ToString(ci);

        if (buckets is { Count: > 0 })
        {
            mt.Properties["buckets"] = string.Join(",", buckets);
        }

        if (counts is { Count: > 0 })
        {
            mt.Properties["counts"] = string.Join(",", counts);
        }

        if (tags is { Count: > 0 })
        {
            foreach (var kv in tags)
            {
                mt.Properties[kv.Key] = kv.Value ?? string.Empty;
            }
        }

        if (!string.IsNullOrWhiteSpace(unit))
        {
            mt.Properties["unit"] = unit!;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            mt.Properties["desc"] = description!;
        }

        _client.TrackMetric(mt);

        _ = ct; // advisory only in current implementation
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sends a pre-constructed <see cref="MetricTelemetry"/> instance.
    /// </summary>
    /// <param name="telemetry">The metric telemetry to emit. Must not be <see langword="null"/>.</param>
    /// <param name="ct">A cancellation token. Currently advisory only; the operation completes synchronously.</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="telemetry"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Use this overload when you have advanced formatting or sampling already applied to the telemetry.
    /// </remarks>
    public ValueTask TrackMetricTelemetryAsync(MetricTelemetry telemetry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        _client.TrackMetric(telemetry);

        _ = ct; // advisory only in current implementation
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Attempts to flush any buffered telemetry to the ingestion service.
    /// </summary>
    /// <param name="ct">A cancellation token. Currently advisory only; <see cref="TelemetryClient.Flush"/> is synchronous.</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="TelemetryClient.Flush"/> initiates sending of buffered items. Depending on the configured channel,
    /// it may return before all items are fully persisted or transmitted. If you need stronger delivery guarantees at
    /// process shutdown, consider adding a short delay after flushing.
    /// </para>
    /// </remarks>
    public ValueTask FlushAsync(CancellationToken ct = default)
    {
        _client.Flush();
        _ = ct;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Flushes pending telemetry and completes disposal.
    /// </summary>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    /// <remarks>
    /// Any exception thrown by <see cref="TelemetryClient.Flush"/> during disposal is swallowed to avoid masking
    /// shutdown paths. Use <see cref="FlushAsync(System.Threading.CancellationToken)"/> earlier if you need
    /// to observe flush errors.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        try
        {
            _client.Flush();
        }
        catch (InvalidOperationException)
        {
            // Swallow to keep disposal resilient.
        }

        return ValueTask.CompletedTask;
    }
}
