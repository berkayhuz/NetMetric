// <copyright file="StackdriverExporterOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Stackdriver.Internals;

/// <summary>
/// Provides configuration for exporting metrics to Google Cloud Monitoring (Stackdriver)
/// via the <see cref="NetMetric.Export.Stackdriver.Exporters.StackdriverExporter"/>.
/// </summary>
/// <remarks>
/// <para>
/// These options control project identification, monitored resource metadata, batching,
/// retry behavior, and label constraints enforced during export.
/// </para>
/// <para>
/// Most applications configure this type through dependency injection and
/// <c>IOptions&lt;T&gt;</c>. Values are read by the exporter at runtime; unless otherwise noted,
/// they can be changed between process restarts without code changes.
/// </para>
/// <example>
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using Microsoft.Extensions.Options;
/// using NetMetric.Export.Stackdriver.Exporters;
/// using NetMetric.Export.Stackdriver.Internals;
///
/// var services = new ServiceCollection();
///
/// services.Configure<StackdriverExporterOptions>(o =>
/// {
///     o.ProjectId = "my-gcp-project";
///     o.ResourceType = "gce_instance";
///     o.ResourceLabels["instance_id"] = "1234567890";
///     o.ResourceLabels["zone"] = "europe-west1-b";
///
///     o.MetricPrefix = "netmetric";
///     o.EnableCreateDescriptors = true;
///     o.BatchSize = 200;
///
///     // Counters will use this as their cumulative series start time (UTC).
///     o.ProcessStart = () => System.DateTimeOffset.UtcNow;
///
///     o.Retry = new RetryOptions
///     {
///         // Example values; see RetryOptions for details.
///         MaxAttempts = 5
///     };
///
///     o.MaxLabelKeyLength = 100;
///     o.MaxLabelValueLength = 256;
///     o.MaxLabelsPerMetric = 64;
/// });
///
/// services.AddSingleton<StackdriverExporter>();
///
/// var provider = services.BuildServiceProvider();
/// var exporter = provider.GetRequiredService<StackdriverExporter>();
/// // Use exporter...
/// ]]></code>
/// </example>
/// </remarks>
public sealed class StackdriverExporterOptions
{
    /// <summary>
    /// Gets or sets the Google Cloud project ID that receives exported metrics.
    /// </summary>
    /// <remarks>
    /// This value is required and must match an existing Google Cloud project identifier,
    /// for example <c>"my-gcp-project"</c>.
    /// </remarks>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the monitored resource type used by Cloud Monitoring.
    /// </summary>
    /// <remarks>
    /// Common values include <c>"global"</c>, <c>"gce_instance"</c>, and <c>"k8s_container"</c>.
    /// The appropriate value depends on where the application runs and how metrics should be
    /// attributed in Cloud Monitoring.
    /// </remarks>
    public string ResourceType { get; set; } = "global";

    /// <summary>
    /// Gets the set of labels applied to the monitored resource.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dictionary instance is mutable (no public setter) to keep references stable for consumers.
    /// Keys and values may be added or updated at runtime, for example:
    /// <c>ResourceLabels["location"] = "europe-west1"</c>.
    /// </para>
    /// <para>
    /// Required labels depend on <see cref="ResourceType"/>. Missing required labels for known
    /// resource types may prevent metric ingestion.
    /// </para>
    /// </remarks>
    public Dictionary<string, string> ResourceLabels { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the metric name prefix used for custom metric types.
    /// </summary>
    /// <remarks>
    /// The effective metric type is constructed as
    /// <c>custom.googleapis.com/{MetricPrefix}/{metricId}</c>. Keep this prefix short, stable,
    /// and lowercase to produce readable, consistent metric types.
    /// </remarks>
    public string MetricPrefix { get; set; } = "netmetric";

    /// <summary>
    /// Gets or sets a value indicating whether the exporter should attempt to create
    /// metric descriptors automatically for previously unseen metrics.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to create descriptors on demand; otherwise, <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// When disabled, exporting an unseen metric type requires the descriptor to exist beforehand,
    /// otherwise writes may fail.
    /// </remarks>
    public bool EnableCreateDescriptors { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of time series to include in a single write batch.
    /// </summary>
    /// <remarks>
    /// The value must respect Cloud Monitoring API limits. Larger batches improve throughput
    /// but may increase latency and retry costs on failures.
    /// </remarks>
    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// Gets or sets a function that returns the process start time in UTC.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value is used as the <c>startTime</c> for cumulative series (for example, counters).
    /// Provide a stable function that returns the same logical start time across exporter instances in
    /// the same process to avoid counter resets.
    /// </para>
    /// <para>
    /// Defaults to returning <see cref="System.DateTimeOffset"/> in UTC using the current time.
    /// </para>
    /// </remarks>
    public Func<DateTimeOffset> ProcessStart { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets retry configuration applied to write operations.
    /// </summary>
    /// <remarks>
    /// See <see cref="RetryOptions"/> for maximum attempts, backoff, and jitter behavior.
    /// </remarks>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// Gets or sets the maximum allowed length of a label key (after sanitization).
    /// </summary>
    /// <value>Defaults to <c>100</c>.</value>
    /// <remarks>
    /// Keys exceeding this length are truncated to fit the limit. Keep keys concise and stable
    /// to reduce churn in metric cardinality.
    /// </remarks>
    public int MaxLabelKeyLength { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum allowed length of a label value.
    /// </summary>
    /// <value>Defaults to <c>256</c>.</value>
    /// <remarks>
    /// Values exceeding this length are truncated. Consider hashing or abbreviating
    /// high-entropy values to manage cardinality and storage costs.
    /// </remarks>
    public int MaxLabelValueLength { get; set; } = 256;

    /// <summary>
    /// Gets or sets the maximum number of labels permitted per metric.
    /// </summary>
    /// <value>Defaults to <c>64</c>. Set to <see langword="null"/> to disable this limit.</value>
    /// <remarks>
    /// Excess labels may be dropped or cause writes to be rejected by the service. Use only
    /// the labels that add diagnostic value and avoid unbounded, user-supplied dimensions.
    /// </remarks>
    public int? MaxLabelsPerMetric { get; set; } = 64;
}
