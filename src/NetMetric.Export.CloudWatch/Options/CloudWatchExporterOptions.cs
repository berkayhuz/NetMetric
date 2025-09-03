// <copyright file="CloudWatchExporterOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Amazon;

namespace NetMetric.Export.CloudWatch.Options;

/// <summary>
/// Provides configuration for exporting metrics to Amazon CloudWatch.
/// </summary>
/// <remarks>
/// <para>
/// These options control batching, dimension limits, storage resolution, retry behavior, and the target
/// AWS region/namespace used by the exporter. They are designed to align with CloudWatch service
/// constraints and good operational defaults.
/// </para>
/// <para>
/// <b>Service limits</b>
/// </para>
/// <list type="bullet">
///   <item><description>Max 20 metric data items per <c>PutMetricData</c> call.</description></item>
///   <item><description>Max 10 dimensions per metric datum.</description></item>
///   <item><description>Supported storage resolutions: 60 seconds (standard) and 1 second (high-resolution).</description></item>
/// </list>
/// <para>
/// <b>Thread-safety</b>
/// </para>
/// <para>
/// This type is a simple options container and is not inherently thread-safe for mutation. Configure
/// it during application startup (e.g., via options binding) and treat instances as immutable at runtime.
/// </para>
/// <para>
/// <b>Typical usage</b>
/// </para>
/// <example>
/// The following shows how to register and configure the exporter in an ASP.NET Core application:
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.Configuration;
/// using Microsoft.Extensions.DependencyInjection;
/// using NetMetric.Export.CloudWatch.Options;
/// using Amazon;
///
/// var builder = WebApplication.CreateBuilder(args);
///
/// // Bind from configuration (e.g., appsettings.json: "NetMetric:CloudWatch")
/// builder.Services.Configure<CloudWatchExporterOptions>(
///     builder.Configuration.GetSection("NetMetric:CloudWatch"));
///
/// // Or configure programmatically:
/// builder.Services.PostConfigure<CloudWatchExporterOptions>(o =>
/// {
///     o.Namespace = "MyCompany.MyService";
///     o.MaxBatchSize = 20;
///     o.MaxDimensions = 10;
///     o.StorageResolution = 60;     // or 1 for high-resolution
///     o.Region = RegionEndpoint.USEast1;
///     o.FlattenMultiSample = true;
///     o.ApproximateSumWhenMissing = true;
///     o.MaxRetries = 3;
///     o.RetryBaseDelayMs = 200;
/// });
///
/// var app = builder.Build();
/// app.Run();
/// ]]></code>
/// </example>
/// </remarks>
public sealed class CloudWatchExporterOptions
{
    /// <summary>
    /// Gets or sets the CloudWatch namespace under which metrics will be published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value appears in the CloudWatch console to logically group related metrics.
    /// Choose a stable, product-oriented name (for example, <c>NetMetric</c>, <c>MyCompany.MyService</c>).
    /// </para>
    /// <para>
    /// <b>Required.</b> The exporter will not attempt to publish metrics if this value is null or whitespace.
    /// </para>
    /// </remarks>
    /// <value>
    /// The CloudWatch namespace string. Defaults to <c>"NetMetric"</c>.
    /// </value>
    public string Namespace { get; set; } = "NetMetric";

    /// <summary>
    /// Gets or sets the maximum number of metric data items to include in a single publish call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CloudWatch imposes a limit of 20 metric data items per <c>PutMetricData</c> request. The exporter will
    /// batch metrics up to this size to reduce API calls while staying within service limits.
    /// </para>
    /// <para>
    /// <b>Valid range:</b> <c>1</c>–<c>20</c>. Values greater than 20 will be clamped to 20 by the exporter.
    /// </para>
    /// </remarks>
    /// <value>Defaults to <c>20</c>.</value>
    public int MaxBatchSize { get; set; } = 20;

    /// <summary>
    /// Gets or sets the maximum number of dimensions allowed per metric datum.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each CloudWatch metric datum can include up to 10 dimensions. Dimensions enable filtering and
    /// slicing in dashboards and alarms, but excessive cardinality can increase cost and reduce
    /// query performance. Prefer a small, well-defined set of dimensions.
    /// </para>
    /// <para>
    /// <b>Valid range:</b> <c>0</c>–<c>10</c>. Values greater than 10 will be clamped to 10 by the exporter.
    /// </para>
    /// </remarks>
    /// <value>Defaults to <c>10</c>.</value>
    public int MaxDimensions { get; set; } = 10;

    /// <summary>
    /// Gets or sets the storage resolution, in seconds, for emitted metrics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <c>60</c> for standard-resolution metrics (one-minute granularity) or <c>1</c> for high-resolution
    /// metrics (one-second granularity). High-resolution metrics incur additional cost but are useful for
    /// low-latency alerting and fine-grained dashboards.
    /// </para>
    /// <para>
    /// <b>Valid values:</b> <c>60</c> (standard) or <c>1</c> (high-resolution).
    /// </para>
    /// </remarks>
    /// <value>Defaults to <c>60</c>.</value>
    public int StorageResolution { get; set; } = 60;

    /// <summary>
    /// Gets or sets the AWS region to which metrics will be published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see langword="null"/>, the exporter relies on the default AWS SDK region resolution chain
    /// (environment variables, shared config/credentials files, EC2/ECS metadata, etc.).
    /// </para>
    /// <para>
    /// Specify a concrete region (for example, <see cref="Amazon.RegionEndpoint.USEast1"/>) to override the default.
    /// </para>
    /// </remarks>
    /// <value>
    /// An optional <see cref="Amazon.RegionEndpoint"/> instance indicating the target region. Defaults to <see langword="null"/>.
    /// </value>
    public RegionEndpoint? Region { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether multi-sample values should be flattened into individual data points.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see langword="true"/>, the exporter expands a multi-sample measurement into multiple CloudWatch
    /// data points. When <see langword="false"/>, the exporter emits a single aggregated datum (for example,
    /// using <c>Minimum</c>, <c>Maximum</c>, <c>SampleCount</c>, and <c>Sum</c>) where appropriate.
    /// </para>
    /// <para>
    /// Flattening can improve the fidelity of percentile-based dashboards but may increase request volume.
    /// </para>
    /// </remarks>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool FlattenMultiSample { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to approximate the sum when it is missing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some aggregated measurements may not include a precise <c>Sum</c>. When enabled, the exporter approximates
    /// it using heuristics such as <c>p50 × count</c> or <c>(min + max) / 2 × count</c>. This can help CloudWatch
    /// compute accurate statistics from partial inputs, at the expense of approximation error.
    /// </para>
    /// <para>
    /// Disable this if you require strictly exact aggregates.
    /// </para>
    /// </remarks>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool ApproximateSumWhenMissing { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for transient, retryable errors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applies to failures such as throttling and 5xx responses. Retries use exponential backoff with jitter
    /// (see <see cref="RetryBaseDelayMs"/>). Set to <c>0</c> to disable retries.
    /// </para>
    /// <para>
    /// <b>Valid range:</b> <c>0</c>–<c>10</c> is typical; higher values may increase latency and duplicate cost.
    /// </para>
    /// </remarks>
    /// <value>Defaults to <c>3</c>.</value>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay, in milliseconds, used to compute exponential backoff between retries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exporter multiplies this base by an exponential factor per attempt and applies jitter to reduce
    /// thundering herds. For example, with a base of 200 ms and three retries, typical delays might be
    /// approximately 200 ms, 400 ms, and 800 ms (plus jitter).
    /// </para>
    /// <para>
    /// <b>Valid range:</b> Any non-negative integer. Values under 50 ms may result in aggressive retry behavior.
    /// </para>
    /// </remarks>
    /// <value>Defaults to <c>200</c> (milliseconds).</value>
    public int RetryBaseDelayMs { get; set; } = 200;
}
