// <copyright file="AzureAppInsightsClient.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace NetMetric.Export.AzureMonitor.Telemetry;

/// <summary>
/// Provides a concrete <see cref="IAzureMonitorClient"/> implementation backed by
/// Azure Application Insights (<see cref="TelemetryClient"/>).
/// </summary>
/// <remarks>
/// <para>
/// This client wraps <see cref="TelemetryClient"/> to send single-value and aggregate
/// metric telemetry to Azure Monitor / Application Insights. The instance owns the underlying
/// <see cref="TelemetryConfiguration"/> and will dispose it when the client is disposed.
/// </para>
///
/// <para>
/// <b>Initialization:</b> Pass a valid Application Insights connection string to the constructor.
/// The connection string typically has the form:
/// <c>InstrumentationKey=&lt;GUID&gt;;IngestionEndpoint=https://&lt;region&gt;.in.applicationinsights.azure.com/</c>.
/// </para>
///
/// <para><b>Telemetry mapping:</b></para>
/// <list type="bullet">
///   <item>
///     <description><see cref="TrackMetricAsync"/> emits a single <see cref="MetricTelemetry"/>
///     with <c>name</c> and <c>value</c>.</description>
///   </item>
///   <item>
///     <description><see cref="TrackMetricAggregateAsync"/> emits a <see cref="MetricTelemetry"/>
///     whose <see cref="MetricTelemetry.Count"/> is set to the aggregate <c>count</c>. Aggregate fields
///     (min/max/percentiles/sum/buckets/counts) are serialized into <see cref="ISupportProperties.Properties"/>
///     with keys like <c>agg.min</c>, <c>agg.max</c>, <c>agg.p50</c>, <c>buckets</c>, <c>counts</c>.</description>
///   </item>
/// </list>
///
/// <para><b>Thread Safety:</b> All public members are safe for concurrent use.
/// <see cref="TelemetryClient"/> is designed to be reused and is thread-safe for tracking operations.</para>
/// </remarks>
public sealed class AzureAppInsightsClient : IAzureMonitorClient
{
    private readonly TelemetryClient _client;
    private readonly TelemetryConfiguration _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAppInsightsClient"/> class using the provided
    /// Application Insights connection string.
    /// </summary>
    /// <param name="connectionString">The Application Insights connection string used to configure the telemetry pipeline.</param>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is <see langword="null"/>,
    /// empty, or whitespace.</exception>
    /// <remarks>
    /// The constructor creates and owns a new <see cref="TelemetryConfiguration"/> with the given connection string.
    /// The configuration and its resources are disposed when <see cref="DisposeAsync"/> is called.
    /// </remarks>
    public AzureAppInsightsClient(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _config = new TelemetryConfiguration { ConnectionString = connectionString };
        _client = new TelemetryClient(_config);
    }

    /// <summary>
    /// Tracks a single numeric metric with optional tags, unit, and description.
    /// </summary>
    /// <param name="name">The metric name as it should appear in Azure Monitor.</param>
    /// <param name="value">The numeric value to record.</param>
    /// <param name="tags">Optional dimension tags to attach as custom properties (key/value strings).</param>
    /// <param name="unit">Optional unit label (for example, <c>ms</c>, <c>count</c>, <c>%</c>).</param>
    /// <param name="description">Optional human-readable description of the metric.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete. This method is non-blocking and
    /// returns a completed <see cref="ValueTask"/>; the token is provided for signature consistency.</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    /// <remarks>
    /// <para>
    /// The method constructs a <see cref="MetricTelemetry"/> and forwards it to <see cref="TelemetryClient.TrackMetric(MetricTelemetry)"/>.
    /// Tags are written into <see cref="ISupportProperties.Properties"/> and the optional <paramref name="unit"/> and
    /// <paramref name="description"/> are stored under the keys <c>unit</c> and <c>desc</c>.
    /// </para>
    /// <para>
    /// This call is fire-and-forget with respect to network I/O. Use <see cref="FlushAsync"/> to request that buffered
    /// telemetry be transmitted.
    /// </para>
    /// </remarks>
    public ValueTask TrackMetricAsync(
        string name, double value, IReadOnlyDictionary<string, string> tags,
        string? unit, string? description, CancellationToken ct = default)
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

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Tracks an aggregate (pre-computed) metric distribution with count, range, optional percentiles and sum,
    /// and optional histogram data (bucket boundaries and per-bucket counts).
    /// </summary>
    /// <param name="name">The metric name as it should appear in Azure Monitor.</param>
    /// <param name="count">Total number of observations contributing to the aggregate.</param>
    /// <param name="min">Minimum observed value.</param>
    /// <param name="max">Maximum observed value.</param>
    /// <param name="p50">Optional 50th percentile (median).</param>
    /// <param name="p90">Optional 90th percentile.</param>
    /// <param name="p99">Optional 99th percentile.</param>
    /// <param name="sum">Optional sum of all observations (useful for deriving average).</param>
    /// <param name="buckets">Optional bucket boundary values (ordered ascending) describing a histogram layout.</param>
    /// <param name="counts">Optional per-bucket observation counts aligned with <paramref name="buckets"/>.</param>
    /// <param name="tags">Optional dimension tags to attach as custom properties.</param>
    /// <param name="unit">Optional unit label (for example, <c>ms</c>, <c>bytes</c>).</param>
    /// <param name="description">Optional human-readable description of the metric.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete. This method is non-blocking and
    /// returns a completed <see cref="ValueTask"/>; the token is provided for signature consistency.</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    /// <remarks>
    /// <para>
    /// The method encodes aggregate fields into <see cref="ISupportProperties.Properties"/> using invariant culture:
    /// <list type="table">
    ///   <listheader>
    ///     <term>Property Key</term><description>Value</description>
    ///   </listheader>
    ///   <item><term><c>agg.min</c></term><description><paramref name="min"/></description></item>
    ///   <item><term><c>agg.max</c></term><description><paramref name="max"/></description></item>
    ///   <item><term><c>agg.p50</c></term><description><paramref name="p50"/></description></item>
    ///   <item><term><c>agg.p90</c></term><description><paramref name="p90"/></description></item>
    ///   <item><term><c>agg.p99</c></term><description><paramref name="p99"/></description></item>
    ///   <item><term><c>agg.sum</c></term><description><paramref name="sum"/></description></item>
    ///   <item><term><c>buckets</c></term><description>Comma-separated <paramref name="buckets"/></description></item>
    ///   <item><term><c>counts</c></term><description>Comma-separated <paramref name="counts"/></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <see cref="MetricTelemetry.Count"/> is set to <paramref name="count"/> to indicate the sample size. Consumers can derive
    /// averages as <c>sum / count</c> when <paramref name="sum"/> is provided.
    /// </para>
    /// </remarks>
    public ValueTask TrackMetricAggregateAsync(
        string name, long count, double min, double max,
        double? p50, double? p90, double? p99, double? sum,
        IReadOnlyList<double>? buckets, IReadOnlyList<long>? counts,
        IReadOnlyDictionary<string, string> tags,
        string? unit, string? description, CancellationToken ct = default)
    {
        var ci = CultureInfo.InvariantCulture;
        var mt = new MetricTelemetry(name, count);

        mt.Properties["agg.min"] = min.ToString(ci);
        mt.Properties["agg.max"] = max.ToString(ci);

        if (p50.HasValue)
        {
            mt.Properties["agg.p50"] = p50.Value.ToString(ci);
        }
        if (p90.HasValue)
        {
            mt.Properties["agg.p90"] = p90.Value.ToString(ci);
        }
        if (p99.HasValue)
        {
            mt.Properties["agg.p99"] = p99.Value.ToString(ci);
        }
        if (sum.HasValue)
        {
            mt.Properties["agg.sum"] = sum.Value.ToString(ci);
        }
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

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Requests an immediate flush of any buffered telemetry to the ingestion endpoint.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete. This method is non-blocking and
    /// returns a completed <see cref="ValueTask"/>; the token is provided for signature consistency.</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="TelemetryClient.Flush()"/> is invoked to drain in-memory buffers. Flushing is best-effort and returns
    /// before network transmission has necessarily completed. If you need stronger delivery guarantees at shutdown,
    /// call <see cref="FlushAsync"/> and then wait a brief moment to allow the channel to send, or integrate with your
    /// host's graceful shutdown pipeline.
    /// </para>
    /// </remarks>
    public ValueTask FlushAsync(CancellationToken ct = default)
    {
        _client.Flush();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Flushes pending telemetry and disposes the underlying <see cref="TelemetryConfiguration"/>.
    /// </summary>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    /// <remarks>
    /// <para>
    /// This method attempts to flush pending telemetry and then disposes the configuration resources. Any
    /// <see cref="InvalidOperationException"/> thrown by <see cref="TelemetryClient.Flush()"/> is swallowed deliberately,
    /// as flushing during teardown can race with the underlying pipeline.
    /// </para>
    /// <para>
    /// After disposal, the instance must not be used.
    /// </para>
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        try
        {
            _client.Flush();
        }
        catch (InvalidOperationException)
        {
            // Intentionally ignored to avoid surfacing teardown races.
        }
        _config.Dispose();
        return ValueTask.CompletedTask;
    }
}
