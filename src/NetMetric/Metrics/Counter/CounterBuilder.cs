// <copyright file="CounterBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.Counter;

/// <summary>
/// Fluent builder for creating <see cref="ICounterMetric"/> instruments.
/// </summary>
/// <remarks>
/// <para>
/// Instances of this builder are typically obtained from <see cref="IMetricFactory.Counter(string, string)"/>.
/// Use the fluent APIs inherited from <c>InstrumentBuilderBase</c>
/// (<see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithUnit(string)"/>,
/// <see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithDescription(string)"/>,
/// <see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithTag(string, string)"/>,
/// <see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithTags(System.Action{NetMetric.Abstractions.TagList})"/>) to
/// enrich the metric with metadata and dimension tags, then call <see cref="Build"/>.
/// </para>
/// <para>
/// Tag precedence during materialization is <b>local</b> &gt; <b>resource</b> &gt; <b>global</b>,
/// followed by sanitization according to <see cref="MetricOptions"/> (length and count limits).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Create a counter for HTTP requests, enriched with tags and description
/// var requests = factory.Counter("http.server.requests", "HTTP Requests")
///     .WithUnit("count")
///     .WithDescription("Total number of HTTP requests handled by the server")
///     .WithTag("service.name", "orders-api")
///     .WithTags(t => { t.Add("env", "prod"); t.Add("region", "eu-central-1"); })
///     .Build();
///
/// // Use the counter
/// requests.Increment();        // +1
/// requests.Increment(5);       // +5
///
/// // Snapshot (example):
/// var val = (CounterValue)requests.GetValue();
/// Console.WriteLine(val.Value); // -> e.g., 6
/// </code>
/// </example>
internal sealed class CounterBuilder : InstrumentBuilderBase<ICounterMetric>, ICounterBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CounterBuilder"/> class.
    /// </summary>
    /// <param name="id">Stable unique identifier for the metric (e.g., <c>"http.server.requests"</c>).</param>
    /// <param name="name">Human-readable metric name (e.g., <c>"HTTP Requests"</c>).</param>
    /// <param name="options">
    /// Metric configuration options (global/resource tags, tag limits, etc.).
    /// Passed down to materialization and the resulting metric.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> or <paramref name="name"/> is null or whitespace.
    /// </exception>
    public CounterBuilder(string id, string name, MetricOptions options)
        : base(id, name, options) { }

    /// <summary>
    /// Finalizes the configuration and creates a new <see cref="ICounterMetric"/> instance.
    /// </summary>
    /// <returns>
    /// A thread-safe <see cref="CounterMetric"/> configured with the selected identifier,
    /// name, and the merged/sanitized tag set.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Counters represent monotonically increasing counts. Use
    /// <see cref="ICounterMetric.Increment(long)"/> to add non-negative deltas.
    /// Negative increments will throw an exception.
    /// </para>
    /// <para>
    /// The resulting metric exposes <see cref="MetricBase.GetValue"/> returning
    /// a <see cref="CounterValue"/> snapshot.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var counter = factory.Counter("queue.messages.processed", "Processed Messages")
    ///     .WithDescription("Total messages processed by the background worker")
    ///     .WithTag("worker", "ingestion")
    ///     .Build();
    ///
    /// counter.Increment();      // +1
    /// counter.Increment(10);    // +10
    /// </code>
    /// </example>
    public override ICounterMetric Build()
        => new CounterMetric(Id, Name, MaterializeTags());
}
