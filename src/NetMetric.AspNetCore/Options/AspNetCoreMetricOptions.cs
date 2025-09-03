// <copyright file="AspNetCoreMetricOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AspNetCore.Options;

/// <summary>
/// Provides configuration options for ASP.NET Core metrics instrumentation.
/// </summary>
/// <remarks>
/// <para>
/// These options control histogram bucket boundaries, sampling behavior,
/// route cardinality protection, and enable/disable flags for various
/// ASP.NET Core MVC pipeline stage timings.
/// </para>
/// <para>
/// Instances are typically registered in dependency injection and consumed
/// by middleware, filters, and binders in the NetMetric ASP.NET Core package.
/// </para>
/// <para><strong>Units:</strong> Duration buckets are expressed in <em>milliseconds</em>;
/// size buckets are expressed in <em>bytes</em>.
/// </para>
/// </remarks>
/// <example>
/// Register and configure in DI:
/// <code>
/// builder.Services.Configure&lt;AspNetCoreMetricOptions&gt;(opt =>
/// {
///     opt.SamplingRate = 0.25; // sample 25%
///     opt.MaxRouteCardinality = 300;
///     opt.OtherRouteLabel = "__other__";
///     opt.EnableModelBindingTiming = true;
///     opt.EnableAuthorizationDecisionTiming = false; // disable if not needed
/// });
/// </code>
/// </example>
/// <seealso cref="NetMetric.AspNetCore.Middleware.RequestMetricsMiddleware"/>
/// <seealso cref="NetMetric.AspNetCore.Internal.MvcMetricSet"/>
/// <seealso cref="NetMetric.AspNetCore.Internal.RequestMetricSet"/>
public sealed class AspNetCoreMetricOptions
{
    /// <summary>
    /// Histogram bucket boundaries (in milliseconds) used for latency/duration metrics.
    /// </summary>
    /// <value>
    /// Defaults to a wide distribution suitable for web latencies (0.5&#160;ms up to 30&#160;000&#160;ms).
    /// </value>
    /// <remarks>
    /// The list should be <em>sorted ascending</em> and contain non-negative values.
    /// </remarks>
    public IReadOnlyList<double> DurationBucketsMs { get; init; } =
        ImmutableArray.Create(0.5, 1, 2, 4, 8, 15, 30, 60, 120, 250, 500, 1000, 2000, 4000, 8000, 15000, 30000);

    /// <summary>
    /// Histogram bucket boundaries (in bytes) used for request and response size metrics.
    /// </summary>
    /// <value>
    /// Defaults from 0 up to 8&#160;388&#160;608 bytes (≈8&#160;MB).
    /// </value>
    /// <remarks>
    /// The list should be <em>sorted ascending</em> and contain non-negative values.
    /// </remarks>
    public IReadOnlyList<double> SizeBucketsBytes { get; init; } =
        ImmutableArray.Create(0d, 512, 1_024, 2_048, 4_096, 8_192, 16_384, 32_768, 65_536, 131_072, 262_144, 524_288, 1_048_576, 2_097_152, 4_194_304, 8_388_608);

    /// <summary>
    /// Maximum number of distinct route values allowed for metrics (cardinality limit).
    /// Routes exceeding this limit are grouped under <see cref="OtherRouteLabel"/>.
    /// </summary>
    /// <value>Defaults to 200.</value>
    /// <remarks>
    /// Applies to normalized route templates (e.g., <c>/api/items/{id}</c>), not raw paths.
    /// </remarks>
    public int MaxRouteCardinality { get; init; } = 200;

    /// <summary>
    /// Label name used to represent "other" when the route cardinality limit is exceeded.
    /// </summary>
    /// <value>Defaults to <c>"__other__"</c>.</value>
    public string OtherRouteLabel { get; init; } = "__other__";

    /// <summary>
    /// Sampling rate for metrics collection in the range of [0, 1].
    /// A value of <c>1.0</c> means all requests are measured.
    /// </summary>
    /// <value>Defaults to 1.0 (no sampling).</value>
    /// <remarks>
    /// Values &lt; 1.0 reduce overhead by probabilistically skipping measurements.
    /// </remarks>
    public double SamplingRate { get; init; } = 1.0;

    /// <summary>
    /// Optional base tags to apply to all generated metrics.
    /// </summary>
    /// <value>
    /// A read-only dictionary of key/value tag pairs; may be <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// Common examples include deployment environment (<c>env</c>) or service metadata (<c>service</c>, <c>version</c>).
    /// </remarks>
    public IReadOnlyDictionary<string, string>? BaseTags { get; init; }

    /// <summary>
    /// Enables or disables timing for model binding stage.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool EnableModelBindingTiming { get; init; } = true;

    /// <summary>
    /// Enables or disables timing for action execution stage.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool EnableActionTiming { get; init; } = true;

    /// <summary>
    /// Enables or disables timing for authorization stage.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool EnableAuthorizationTiming { get; init; } = true;

    /// <summary>
    /// Enables or disables timing for resource filter stage.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool EnableResourceTiming { get; init; } = true;

    /// <summary>
    /// Enables or disables timing for exception filter stage.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool EnableExceptionTiming { get; init; } = true;

    /// <summary>
    /// Enables or disables timing for authorization decision evaluation stage.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    /// <remarks>
    /// This is distinct from the MVC authorization filter stage and is intended for deeper
    /// policy/requirement evaluation timing if instrumented.
    /// </remarks>
    public bool EnableAuthorizationDecisionTiming { get; init; } = true;
}
