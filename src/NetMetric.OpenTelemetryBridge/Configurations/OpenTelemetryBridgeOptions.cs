// <copyright file="OpenTelemetryBridgeOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.OpenTelemetryBridge.Mapper;

namespace NetMetric.OpenTelemetryBridge.Configurations;

/// <summary>
/// Configuration options for the OpenTelemetry bridge that exports NetMetric values
/// through an OpenTelemetry <c>Meter</c> and compatible exporters.
/// </summary>
/// <remarks>
/// <para>
/// These options control meter identity (name and version), tag-to-attribute mapping,
/// how summary quantiles are represented, limits for gauge series cardinality,
/// optional metric-name sanitization, and error reporting callbacks.
/// </para>
/// <para>
/// The options are immutable after construction (via init-only setters) and are typically
/// supplied through dependency injection using <c>IOptions&lt;OpenTelemetryBridgeOptions&gt;</c>.
/// </para>
/// <example>
/// The following example shows a minimal setup that keeps the defaults:
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using NetMetric.OpenTelemetryBridge;
/// using NetMetric.OpenTelemetryBridge.Configurations;
///
/// var services = new ServiceCollection();
///
/// // Register the bridge and keep default options.
/// services.AddOpenTelemetryBridge(opts =>
/// {
///     // All defaults are acceptable for small apps.
/// });
/// ]]></code>
/// </example>
/// <example>
/// The following example customizes quantile export, gauge cardinality,
/// and provides an error callback:
/// <code language="csharp"><![CDATA[
/// services.AddOpenTelemetryBridge(opts =>
/// {
///     opts.MeterName = "MyCompany.App";
///     opts.MeterVersion = "2.3.0";
///
///     // Export summary quantiles as attributes on a single metric.
///     opts.SummaryQuantiles = OpenTelemetryBridgeOptions.QuantileExportMode.AsAttributes;
///
///     // Limit per-ID gauge series to control cardinality.
///     opts.MaxGaugeSeriesPerId = 100;
///
///     // Keep OTel-friendly metric names.
///     opts.SanitizeMetricNames = true;
///
///     // Provide a custom attribute mapper if needed.
///     opts.AttributeMapper = new DefaultAttributeMapper(
///         keyPrefix: "nm.", toLowerInvariant: true);
///
///     // Observe export errors.
///     opts.OnExportError = ex => Console.Error.WriteLine(ex);
/// });
/// ]]></code>
/// </example>
/// </remarks>
/// <threadsafety>
/// This type is immutable after initialization and is safe to share across threads.
/// However, any delegates assigned to <see cref="OnExportError"/> must themselves be thread-safe.
/// </threadsafety>
/// <seealso cref="DefaultAttributeMapper"/>
public sealed class OpenTelemetryBridgeOptions
{
    /// <summary>
    /// Gets the OpenTelemetry meter name used by the bridge.
    /// </summary>
    /// <value>
    /// Defaults to <c>"NetMetric.Bridge"</c>. Use a stable, product- or component-level name
    /// to facilitate analysis and correlation.
    /// </value>
    public string MeterName { get; init; } = "NetMetric.Bridge";

    /// <summary>
    /// Gets the OpenTelemetry meter version string.
    /// </summary>
    /// <value>
    /// Defaults to <c>"1.0.0"</c>. Consider aligning this with your component's version to
    /// ease troubleshooting and attribution.
    /// </value>
    public string? MeterVersion { get; init; } = "1.0.0";

    /// <summary>
    /// Gets the strategy for exporting quantiles derived from <see cref="SummaryValue"/>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="QuantileExportMode.AsGauges"/>: each configured quantile is exported
    /// as a separate gauge metric.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="QuantileExportMode.AsAttributes"/>: quantiles are attached as attributes
    /// on a single metric, reducing the number of time series at the expense of attribute size.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    public QuantileExportMode SummaryQuantiles { get; init; } = QuantileExportMode.AsGauges;

    /// <summary>
    /// Gets the attribute key used when exporting multi-sample identifiers.
    /// </summary>
    /// <value>Defaults to <c>"nm.id"</c>.</value>
    public string MultiSampleIdKey { get; init; } = "nm.id";

    /// <summary>
    /// Gets the attribute key used when exporting multi-sample names.
    /// </summary>
    /// <value>Defaults to <c>"nm.name"</c>.</value>
    public string MultiSampleNameKey { get; init; } = "nm.name";

    /// <summary>
    /// Gets the mapper used to convert NetMetric tags into OpenTelemetry attributes.
    /// </summary>
    /// <remarks>
    /// Uses the concrete <see cref="DefaultAttributeMapper"/> for performance (avoids virtual dispatch).
    /// Replace with a customized mapper if you need to rename or filter attributes.
    /// </remarks>
    public DefaultAttributeMapper AttributeMapper { get; init; } = new DefaultAttributeMapper();

    /// <summary>
    /// Gets the maximum number of gauge series to retain per metric identifier.
    /// </summary>
    /// <value>
    /// Defaults to <c>200</c>. This limit prevents unbounded cardinality growth
    /// for gauges with high-dimensional label sets.
    /// </value>
    /// <remarks>
    /// When the limit is reached, the bridge may drop additional unseen series
    /// for the same metric identifier to protect resource usage.
    /// </remarks>
    public int MaxGaugeSeriesPerId { get; init; } = 200;

    /// <summary>
    /// Gets a value indicating whether metric names are sanitized to meet typical
    /// OpenTelemetry naming requirements.
    /// </summary>
    /// <value>
    /// Defaults to <c>true</c>. When enabled, invalid characters are removed or replaced,
    /// and names are ensured to start with an alphanumeric character.
    /// </value>
    public bool SanitizeMetricNames { get; init; } = true;

    /// <summary>
    /// Gets a callback that is invoked when an exception occurs during export.
    /// </summary>
    /// <remarks>
    /// The bridge catches and forwards non-fatal export exceptions to this delegate.
    /// Implementations should be non-throwing and thread-safe; consider lightweight logging
    /// to avoid feedback loops in high-error scenarios.
    /// </remarks>
    public Action<Exception>? OnExportError { get; init; }

    /// <summary>
    /// Determines how summary quantiles are exported to OpenTelemetry.
    /// </summary>
    public enum QuantileExportMode
    {
        /// <summary>
        /// Export quantiles as separate gauge metrics.
        /// </summary>
        AsGauges,

        /// <summary>
        /// Export quantiles as attributes attached to a single metric.
        /// </summary>
        AsAttributes
    }
}
