// <copyright file="AzureMonitorExporterOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Collections.ObjectModel;

namespace NetMetric.Export.AzureMonitor.Options;

/// <summary>
/// Provides configuration for exporting metrics and telemetry to Azure Monitor.
/// </summary>
/// <remarks>
/// <para>
/// This options object controls metric naming and tagging policies, batching and backpressure behavior,
/// retry strategy, shutdown semantics, and whether to emit self-health metrics. It is typically consumed via
/// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> or
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> and applied by the Azure Monitor exporter
/// and background sender components.
/// </para>
/// <para>
/// Reasonable defaults are provided to get started quickly. For high-throughput scenarios you may increase
/// <see cref="MaxQueueLength"/> and <see cref="MaxBatchSize"/> while tuning retry delays to match the expected
/// transient failure profile of the destination.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Program.cs (generic host)
/// builder.Services.Configure<AzureMonitorExporterOptions>(o =>
/// {
///     o.ConnectionString = builder.Configuration["AzureMonitor:ConnectionString"];
///     o.NamePrefix = "netmetric.";
///     o.SanitizeMetricNames = true;
///     o.MaxQueueLength = 100_000;
///     o.MaxBatchSize = 1000;
///     o.FlushInterval = TimeSpan.FromSeconds(2);
///     o.QueueFullPolicy = AzureMonitorExporterOptions.DropPolicy.DropOldest;
///
///     // Tag governance
///     o.TagAllowList = new[] { "env", "service", "region" };
///     o.TagBlockList!.Add("secret");
///
///     // Retry/backoff
///     o.MaxRetryAttempts = 5;
///     o.BaseDelay = TimeSpan.FromMilliseconds(200);
///     o.MaxDelay = TimeSpan.FromSeconds(5);
///
///     // Self-metrics
///     o.EnableSelfMetrics = true;
///     o.SelfMetricPrefix = "netmetric.azuremonitor.";
///     o.SelfMetricsAllow!.Add("queue.length");
///     o.SelfMetricsBlock.Add("internal.debug-only");
/// });
/// ]]></code>
/// </example>
/// <example>
/// <code language="json"><![CDATA[
/// // appsettings.json
/// {
///   "AzureMonitor": {
///     "ConnectionString": "InstrumentationKey=...;IngestionEndpoint=https://<region>.in.applicationinsights.azure.com/"
///   },
///   "AzureMonitorExporterOptions": {
///     "NamePrefix": "netmetric.",
///     "SanitizeMetricNames": true,
///     "MaxTagKeyLength": 64,
///     "MaxTagValueLength": 256,
///     "MaxTagsPerMetric": 20,
///     "MaxQueueLength": 50000,
///     "MaxBatchSize": 1000,
///     "FlushInterval": "00:00:02",
///     "EmptyQueueDelay": "00:00:00.2000000",
///     "FlushOnExport": true,
///     "QueueFullPolicy": "DropOldest",
///     "MaxRetryAttempts": 5,
///     "BaseDelay": "00:00:00.2000000",
///     "MaxDelay": "00:00:05",
///     "ShutdownDrainTimeout": "00:00:02",
///     "EnableSelfMetrics": true,
///     "SelfMetricPrefix": "netmetric.azuremonitor."
///   }
/// }
/// ]]></code>
/// </example>
public sealed class AzureMonitorExporterOptions
{
    /// <summary>
    /// Gets or sets the Azure Monitor (Application Insights) connection string used by the exporter.
    /// </summary>
    /// <value>
    /// A connection string in the form expected by Application Insights, for example:
    /// <c>InstrumentationKey=&lt;key&gt;;IngestionEndpoint=https://&lt;region&gt;.in.applicationinsights.azure.com/</c>.
    /// </value>
    /// <remarks>
    /// If not provided, the exporter will not be able to send telemetry and will typically fail fast at startup.
    /// </remarks>
    public string? ConnectionString { get; set; }

    // Metric name & tag policies

    /// <summary>
    /// Gets or sets the prefix automatically prepended to exported metric names.
    /// </summary>
    /// <value>
    /// The metric name prefix. Defaults to <c>"netmetric."</c>.
    /// </value>
    /// <remarks>
    /// Use a consistent prefix to group metrics by product or subsystem and to avoid name collisions.
    /// </remarks>
    public string? NamePrefix { get; set; } = "netmetric.";

    /// <summary>
    /// Gets or sets a value indicating whether metric names should be sanitized (e.g., replacing invalid characters).
    /// </summary>
    /// <value>
    /// <see langword="true"/> to sanitize metric names; otherwise, <see langword="false"/>. Defaults to <see langword="true"/>.
    /// </value>
    /// <remarks>
    /// Azure Monitor imposes restrictions on metric names. When enabled, the exporter normalizes names to conform
    /// to those constraints (for example, replacing whitespace or illegal punctuation).
    /// </remarks>
    public bool SanitizeMetricNames { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum allowed length for tag (dimension) keys.
    /// </summary>
    /// <value>
    /// An integer number of characters. Defaults to <c>64</c>.
    /// </value>
    /// <remarks>
    /// Keys longer than this value are truncated or rejected by the exporter depending on implementation details.
    /// </remarks>
    public int MaxTagKeyLength { get; set; } = 64;

    /// <summary>
    /// Gets or sets the maximum allowed length for tag (dimension) values.
    /// </summary>
    /// <value>
    /// An integer number of characters. Defaults to <c>256</c>.
    /// </value>
    /// <remarks>
    /// Values longer than this value are truncated or rejected by the exporter depending on implementation details.
    /// </remarks>
    public int MaxTagValueLength { get; set; } = 256;

    /// <summary>
    /// Gets or sets the maximum number of tags (dimensions) permitted per metric.
    /// </summary>
    /// <value>
    /// The maximum dimension count. Defaults to <c>20</c>.
    /// </value>
    /// <remarks>
    /// Reducing this value can help control cardinality and ingestion cost. When set, excess tags
    /// are ignored according to the exporter’s selection policy.
    /// </remarks>
    public int? MaxTagsPerMetric { get; set; } = 20;

    /// <summary>
    /// Gets or sets an allow-list of tag keys that are permitted on outgoing metrics.
    /// </summary>
    /// <value>
    /// A read-only list of tag keys; when non-null, only keys present in this list are emitted.
    /// </value>
    /// <remarks>
    /// <para>
    /// Use this to enforce a strict schema. When specified, any tag with a key not in the list is dropped.
    /// </para>
    /// <para>
    /// The setter defensively copies the input into an internal collection to prevent external mutation.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string>? TagAllowList
    {
        get => _tagAllowList;
        set => _tagAllowList = value != null ? new Collection<string>(value.ToList()) : null;
    }

    private Collection<string>? _tagAllowList;

    /// <summary>
    /// Gets the block-list of tag keys to exclude from outgoing metrics.
    /// </summary>
    /// <value>
    /// A mutable list of tag keys that should never be emitted. Defaults to an empty list.
    /// </value>
    /// <remarks>
    /// When both <see cref="TagAllowList"/> and <see cref="TagBlockList"/> are specified,
    /// the allow-list is applied first and the block-list removes any remaining disallowed keys.
    /// </remarks>
    public IList<string>? TagBlockList { get; } = new List<string>();

    // Batching & backpressure

    /// <summary>
    /// Gets or sets the maximum number of telemetry items buffered in memory.
    /// </summary>
    /// <value>
    /// The queue capacity. Defaults to <c>50,000</c>.
    /// </value>
    /// <remarks>
    /// When the queue is full, items are dropped according to <see cref="QueueFullPolicy"/>.
    /// Increase this value for bursty workloads at the cost of higher memory usage.
    /// </remarks>
    public int MaxQueueLength { get; set; } = 50_000;

    /// <summary>
    /// Gets or sets the maximum number of telemetry items sent in a single batch.
    /// </summary>
    /// <value>
    /// The batch size. Defaults to <c>1,000</c>.
    /// </value>
    /// <remarks>
    /// Larger batches improve throughput but may increase latency and memory pressure.
    /// </remarks>
    public int MaxBatchSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the time interval between periodic flushes of the queue to Azure Monitor.
    /// </summary>
    /// <value>
    /// The flush cadence. Defaults to <c>00:00:02</c> (2 seconds).
    /// </value>
    /// <remarks>
    /// This setting controls the background sender’s pacing when data is flowing.
    /// </remarks>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the delay applied by the background sender when the queue is empty.
    /// </summary>
    /// <value>
    /// The idle delay. Defaults to <c>00:00:00.2000000</c> (200 ms).
    /// </value>
    /// <remarks>
    /// Reducing this value lowers send latency for sporadic workloads at the cost of more wake-ups.
    /// </remarks>
    public TimeSpan EmptyQueueDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets a value indicating whether the exporter should trigger a queue flush on each explicit export call.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to flush on export; otherwise, <see langword="false"/>. Defaults to <see langword="true"/>.
    /// </value>
    /// <remarks>
    /// Leave enabled when the exporter is used in request/response paths that expect timely delivery.
    /// Disable to rely solely on the background sender cadence for higher throughput.
    /// </remarks>
    public bool FlushOnExport { get; set; } = true;

    /// <summary>
    /// Gets or sets the policy applied when <see cref="MaxQueueLength"/> is reached.
    /// </summary>
    /// <value>
    /// One of <see cref="DropPolicy.DropOldest"/> (default) or <see cref="DropPolicy.DropNewest"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// <see cref="DropPolicy.DropOldest"/> preserves the most recent telemetry under sustained pressure.
    /// <see cref="DropPolicy.DropNewest"/> preserves historical continuity at the expense of latest events.
    /// </para>
    /// Choose the policy that best matches your diagnostic needs.
    /// </remarks>
    public DropPolicy QueueFullPolicy { get; set; } = DropPolicy.DropOldest;

    // Retry

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for a failed batch.
    /// </summary>
    /// <value>
    /// An integer count of attempts (including the first attempt). Defaults to <c>5</c>.
    /// </value>
    /// <remarks>
    /// Retries use exponential backoff with jitter bounded by <see cref="BaseDelay"/> and <see cref="MaxDelay"/>.
    /// </remarks>
    public int MaxRetryAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the initial (base) delay used for exponential backoff on retry.
    /// </summary>
    /// <value>
    /// The starting delay. Defaults to <c>00:00:00.2000000</c> (200 ms).
    /// </value>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets the maximum backoff delay allowed between retry attempts.
    /// </summary>
    /// <value>
    /// The upper bound on delay. Defaults to <c>00:00:05</c> (5 seconds).
    /// </value>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    // Shutdown

    /// <summary>
    /// Gets or sets the maximum time allowed for draining the in-memory queue during shutdown.
    /// </summary>
    /// <value>
    /// The drain timeout. Defaults to <c>00:00:02</c> (2 seconds).
    /// </value>
    /// <remarks>
    /// On application stop, the sender attempts to flush remaining items until this timeout elapses.
    /// Unsent items may be dropped afterward to allow a timely shutdown.
    /// </remarks>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(2);

    // Self-metrics

    /// <summary>
    /// Gets or sets a value indicating whether the exporter emits self-health metrics (e.g., queue length, send counts).
    /// </summary>
    /// <value>
    /// <see langword="true"/> to emit self-metrics; otherwise, <see langword="false"/>. Defaults to <see langword="true"/>.
    /// </value>
    /// <remarks>
    /// Self-metrics are useful for alerting on exporter health and throughput. They incur minimal overhead.
    /// </remarks>
    public bool EnableSelfMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets the prefix applied to all self-metric names.
    /// </summary>
    /// <value>
    /// The self-metric name prefix. Defaults to <c>"netmetric.azuremonitor."</c>.
    /// </value>
    /// <remarks>
    /// The values in <see cref="SelfMetricsAllow"/> and <see cref="SelfMetricsBlock"/> refer to metric
    /// names <em>without</em> this prefix.
    /// </remarks>
    public string SelfMetricPrefix { get; set; } = "netmetric.azuremonitor.";

    /// <summary>
    /// Gets the allow-list of self-metric names (excluding the prefix). When specified, only listed names are emitted.
    /// </summary>
    /// <value>
    /// A mutable list of bare self-metric names; defaults to <see langword="null"/> (no allow-list).
    /// </value>
    /// <remarks>
    /// Use to reduce noise to a curated subset, for example: <c>"queue.length"</c>, <c>"batch.size"</c>.
    /// </remarks>
    public IList<string>? SelfMetricsAllow { get; } = new List<string>();

    /// <summary>
    /// Gets the block-list of self-metric names (excluding the prefix) that should not be emitted.
    /// </summary>
    /// <value>
    /// A mutable list of bare self-metric names. Defaults to an empty list.
    /// </value>
    /// <remarks>
    /// Applied after <see cref="SelfMetricsAllow"/> (if any) to remove unwanted names.
    /// </remarks>
    public IList<string> SelfMetricsBlock { get; } = new List<string>();

    /// <summary>
    /// Specifies how the in-memory queue behaves when it reaches its capacity.
    /// </summary>
    /// <remarks>
    /// See <see cref="QueueFullPolicy"/> for the active selection.
    /// </remarks>
    public enum DropPolicy
    {
        /// <summary>
        /// Drop the oldest items first, preserving the most recent telemetry under sustained pressure.
        /// </summary>
        DropOldest,

        /// <summary>
        /// Drop the newest items first, preserving earlier telemetry at the expense of latest events.
        /// </summary>
        DropNewest
    }
}
