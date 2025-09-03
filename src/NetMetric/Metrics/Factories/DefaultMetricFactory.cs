// <copyright file="DefaultMetricFactory.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Options;

namespace NetMetric.Metrics.Factories;

/// <summary>
/// Default implementation of <see cref="IMetricFactory"/> that produces
/// all supported metric instrument builders (counter, gauge, histogram, etc.).
/// </summary>
/// <remarks>
/// <para>
/// This factory is designed as the primary entry point for creating metrics in NetMetric.  
/// It is dependency-free except for the injected <see cref="MetricOptions"/>, making it suitable
/// as the default factory registered in DI containers.
/// </para>
/// <para>
/// Each method returns a builder that follows the fluent API pattern.  
/// Builders can be configured with unit, description, tags, and window policies, then finalized
/// by calling <c>.Build()</c> to produce the metric instance.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example: create and use a counter
/// var factory = new DefaultMetricFactory(Options.Create(new MetricOptions()));
/// var counter = factory.Counter("http.requests.total", "HTTP Requests")
///                     .WithUnit("count")
///                     .WithDescription("Total number of HTTP requests received")
///                     .Build();
///
/// counter.Increment();
/// </code>
/// </example>
public sealed class DefaultMetricFactory : IMetricFactory
{
    private readonly MetricOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultMetricFactory"/> class.
    /// </summary>
    /// <param name="options">
    /// Metric options injected via <see cref="IOptions{TOptions}"/>.  
    /// Provides global tags, resource attributes, and tag limits applied to all metrics.
    /// </param>
    public DefaultMetricFactory(IOptions<MetricOptions> options)
    {
        _options = options?.Value ?? new MetricOptions();
    }

    /// <summary>
    /// Creates a new builder for a <see cref="IGauge"/> metric.
    /// </summary>
    /// <param name="id">Stable unique identifier (e.g., <c>"system.cpu.usage"</c>).</param>
    /// <param name="name">Human-readable name (e.g., <c>"CPU Usage"</c>).</param>
    /// <returns>A fluent <see cref="IGaugeBuilder"/> to configure and build the gauge.</returns>
    /// <example>
    /// <code>
    /// var gauge = factory.Gauge("system.memory.usage", "Memory Usage")
    ///                    .WithUnit("bytes")
    ///                    .WithDescription("Current process memory usage")
    ///                    .Build();
    ///
    /// gauge.SetValue(42_000_000);
    /// </code>
    /// </example>
    public IGaugeBuilder Gauge(string id, string name)
        => new GaugeBuilder(id, name, _options);

    /// <summary>
    /// Creates a new builder for a <see cref="ICounterMetric"/>.
    /// </summary>
    /// <param name="id">Stable unique identifier (e.g., <c>"app.errors"</c>).</param>
    /// <param name="name">Human-readable name (e.g., <c>"Application Errors"</c>).</param>
    /// <returns>A fluent <see cref="ICounterBuilder"/> to configure and build the counter.</returns>
    /// <example>
    /// <code>
    /// var counter = factory.Counter("queue.processed", "Processed Messages")
    ///                      .WithDescription("Number of messages successfully processed")
    ///                      .Build();
    ///
    /// counter.Increment();
    /// </code>
    /// </example>
    public ICounterBuilder Counter(string id, string name)
        => new CounterBuilder(id, name, _options);

    /// <summary>
    /// Creates a new builder for a <see cref="ITimerMetric"/>.
    /// </summary>
    /// <param name="id">Stable unique identifier (e.g., <c>"http.request.duration"</c>).</param>
    /// <param name="name">Human-readable name (e.g., <c>"HTTP Request Duration"</c>).</param>
    /// <returns>A fluent <see cref="ITimerBuilder"/> to configure and build the timer.</returns>
    /// <example>
    /// <code>
    /// var timer = factory.Timer("db.query.time", "Database Query Time")
    ///                    .WithHistogramCapacity(4096)
    ///                    .Build();
    ///
    /// using (timer.Start())
    /// {
    ///     ExecuteDatabaseQuery();
    /// }
    /// </code>
    /// </example>
    public ITimerBuilder Timer(string id, string name)
        => new TimerBuilder(id, name, _options);

    /// <summary>
    /// Creates a new builder for a <see cref="ISummaryMetric"/>.
    /// </summary>
    /// <param name="id">Stable unique identifier (e.g., <c>"http.latency"</c>).</param>
    /// <param name="name">Human-readable name (e.g., <c>"HTTP Latency"</c>).</param>
    /// <returns>A fluent <see cref="ISummaryBuilder"/> to configure and build the summary metric.</returns>
    /// <example>
    /// <code>
    /// var summary = factory.Summary("svc.response.time", "Service Response Time")
    ///                      .WithQuantiles(0.5, 0.9, 0.99)
    ///                      .Build();
    ///
    /// summary.Record(123);
    /// </code>
    /// </example>
    public ISummaryBuilder Summary(string id, string name)
        => new SummaryBuilder(id, name, _options);

    /// <summary>
    /// Creates a new builder for a bucketed <see cref="IBucketHistogramMetric"/>.
    /// </summary>
    /// <param name="id">Stable unique identifier (e.g., <c>"cache.lookup.duration"</c>).</param>
    /// <param name="name">Human-readable name (e.g., <c>"Cache Lookup Duration"</c>).</param>
    /// <returns>A fluent <see cref="IBucketHistogramBuilder"/> to configure and build the histogram metric.</returns>
    /// <example>
    /// <code>
    /// var hist = factory.Histogram("http.request.size", "HTTP Request Size")
    ///                   .Linear(0, 100, 10)   // buckets: 0-1000 bytes
    ///                   .Build();
    ///
    /// hist.Observe(350);
    /// </code>
    /// </example>
    public IBucketHistogramBuilder Histogram(string id, string name)
        => new BucketHistogramBuilder(id, name, _options);

    /// <summary>
    /// Creates a new builder for a <see cref="IMultiGauge"/> metric.
    /// </summary>
    /// <param name="id">Stable unique identifier (e.g., <c>"disk.usage"</c>).</param>
    /// <param name="name">Human-readable name (e.g., <c>"Disk Usage"</c>).</param>
    /// <returns>A fluent <see cref="IMultiGaugeBuilder"/> to configure and build the multi-gauge metric.</returns>
    /// <example>
    /// <code>
    /// var mg = factory.MultiGauge("system.disk.usage", "Disk Usage")
    ///                 .WithInitialCapacity(128)
    ///                 .Build();
    ///
    /// // Collect per-disk usage
    /// mg.AddSibling("disk.c", "Disk C:", 70.5);
    /// mg.AddSibling("disk.d", "Disk D:", 42.1);
    /// </code>
    /// </example>
    public IMultiGaugeBuilder MultiGauge(string id, string name)
        => new MultiGaugeBuilder(id, name, _options);
}
