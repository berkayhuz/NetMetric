// <copyright file="SummaryBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

#pragma warning disable CS0649

namespace NetMetric.Metrics.Summary;

/// <summary>
/// Fluent builder for creating <see cref="ISummaryMetric"/> instruments with optional quantile configuration.
/// </summary>
/// <remarks>
/// <para>
/// A summary metric estimates distribution statistics over a stream of values.
/// Unlike histograms (which bucket values), summaries track user-selected quantiles
/// (e.g., median, p90, p99) using algorithms such as P².  
/// </para>
/// <para>
/// This builder is obtained via <see cref="IMetricFactory.Summary(string, string)"/> and supports
/// standard configuration from <see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}"/>:
/// <list type="bullet">
///   <item><description><see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithUnit(string)"/></description></item>
///   <item><description><see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithDescription(string)"/></description></item>
///   <item><description><see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithTag(string, string)"/></description></item>
///   <item><description><see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithTags(System.Action{NetMetric.Abstractions.TagList})"/></description></item>
/// </list>
/// Call <see cref="WithQuantiles"/> to choose which quantiles to compute.  
/// Finally, call <see cref="Build"/> to create a <see cref="SummaryMetric"/> instance.
/// </para>
/// <para>
/// If no explicit quantiles are configured, a default set <c>{0.5, 0.9, 0.99}</c> is used.  
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example: summary for request latency with custom quantiles
/// var summary = factory.Summary("http.latency", "HTTP Latency")
///     .WithUnit("ms")
///     .WithDescription("Distribution of HTTP request latencies")
///     .WithQuantiles(0.5, 0.95, 0.99)   // track median, p95, and p99
///     .WithTag("service", "checkout-api")
///     .Build();
///
/// summary.Record(123);
/// summary.Record(250);
///
/// var snapshot = (SummaryValue)summary.GetValue();
/// Console.WriteLine($"Count={snapshot.Count} p95={snapshot.Quantiles[0.95]}");
/// </code>
/// </example>
internal sealed class SummaryBuilder : InstrumentBuilderBase<ISummaryMetric>, ISummaryBuilder
{
    /// <summary>
    /// Default quantiles tracked when none are explicitly configured.
    /// </summary>
    private static readonly double[] DefaultQuantiles = new[] { 0.5, 0.9, 0.99 };

    private double[]? _quantiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="SummaryBuilder"/> class.
    /// </summary>
    /// <param name="id">Stable unique identifier for the summary metric (e.g., <c>"http.latency"</c>).</param>
    /// <param name="name">Human-readable name for the metric (e.g., <c>"HTTP Latency"</c>).</param>
    /// <param name="options">Optional metric configuration (global tags, resource attributes, tag limits).</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="id"/> or <paramref name="name"/> is null or whitespace.</exception>
    public SummaryBuilder(string id, string name, MetricOptions options) : base(id, name, options) { }

    /// <summary>
    /// Specifies the quantiles to be tracked by the summary metric.
    /// </summary>
    /// <param name="quantiles">One or more quantiles in the interval (0,1), e.g., <c>0.5</c>, <c>0.9</c>.</param>
    /// <returns>The current builder instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException">
    /// May be thrown later in <see cref="SummaryMetric"/> construction if invalid quantiles are provided.
    /// </exception>
    /// <remarks>
    /// Duplicate quantiles are allowed but redundant. If no call is made to <see cref="WithQuantiles"/>,
    /// the default quantiles {0.5, 0.9, 0.99} are used.
    /// </remarks>
    public ISummaryBuilder WithQuantiles(params double[] quantiles)
    {
        _quantiles = quantiles;
        return this;
    }

    /// <summary>
    /// Finalizes the configuration and creates a new <see cref="ISummaryMetric"/>.
    /// </summary>
    /// <returns>A fully constructed <see cref="SummaryMetric"/>.</returns>
    /// <remarks>
    /// The windowing policy is resolved as follows:
    /// <list type="bullet">
    ///   <item><description>If <see cref="InstrumentBuilderBase{TMetric}.Window"/> is already a <see cref="MetricWindowPolicy"/>, use it.</description></item>
    ///   <item><description>If it is a tumbling-window placeholder, construct a concrete tumbling policy from its period.</description></item>
    ///   <item><description>Otherwise, fall back to cumulative mode.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var s = factory.Summary("db.query.duration", "DB Query Duration")
    ///                .WithQuantiles(0.5, 0.9, 0.99)
    ///                .WithWindow(MetricWindowPolicy.Tumbling(TimeSpan.FromMinutes(1)))
    ///                .Build();
    ///
    /// s.Record(45);
    /// s.Record(82);
    /// </code>
    /// </example>
    public override ISummaryMetric Build()
    {
        var tags = MaterializeTags();
        var window = Window is MetricWindowPolicy mp
            ? mp
            : Window is { Kind: MetricWindowKind.Tumbling } w
                ? MetricWindowPolicy.Tumbling(w.Period)
                : MetricWindowPolicy.Cumulative;

        return new SummaryMetric(Id, Name, _quantiles ?? DefaultQuantiles, tags, window, clock: null);
    }
}
