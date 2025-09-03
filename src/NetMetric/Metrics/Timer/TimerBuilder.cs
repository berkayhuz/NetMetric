// <copyright file="TimerBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.Timer;

/// <summary>
/// Fluent builder for constructing <see cref="ITimerMetric"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// A timer metric is a specialized histogram designed for recording durations.  
/// Internally, it stores recent samples in a sliding window buffer of configurable capacity,
/// enabling computation of distribution statistics such as min, max, median (p50), p90, and p99.
/// </para>
/// <para>
/// This builder is typically obtained from <see cref="IMetricFactory.Timer(string, string)"/> and
/// supports fluent configuration for:
/// <list type="bullet">
///   <item><description><see cref="InstrumentBuilderBase{TMetric}.WithUnit(string)"/> (e.g., "ms")</description></item>
///   <item><description><see cref="InstrumentBuilderBase{TMetric}.WithDescription(string)"/></description></item>
///   <item><description><see cref="InstrumentBuilderBase{TMetric}.WithTag(string, string)"/></description></item>
///   <item><description><see cref="WithHistogramCapacity(int)"/> to adjust the sliding window size</description></item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example: track database query duration with a timer
/// var timer = factory.Timer("db.query.time", "Database Query Duration")
///     .WithUnit("ms")
///     .WithDescription("Latency distribution of database queries")
///     .WithHistogramCapacity(4096)   // retain up to 4096 samples
///     .Build();
///
/// using (timer.Start())  // returns an IDisposable/handle measuring elapsed time
/// {
///     ExecuteQuery();
/// }
///
/// var snapshot = (DistributionValue)timer.GetValue();
/// Console.WriteLine($"count={snapshot.Count} p90={snapshot.P90}ms");
/// </code>
/// </example>
internal sealed class TimerBuilder : InstrumentBuilderBase<ITimerMetric>, ITimerBuilder
{
    private int _capacity = 2048;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimerBuilder"/> class.
    /// </summary>
    /// <param name="id">Stable unique identifier for the timer (e.g., <c>"http.request.duration"</c>).</param>
    /// <param name="name">Human-readable name of the timer (e.g., <c>"HTTP Request Duration"</c>).</param>
    /// <param name="options">Metric options (tags, tag limits, global/resource attributes).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> or <paramref name="name"/> is null or whitespace.</exception>
    public TimerBuilder(string id, string name, MetricOptions options)
        : base(id, name, options) { }

    /// <summary>
    /// Configures the maximum number of samples retained by the timer’s histogram window.
    /// </summary>
    /// <param name="capacity">Maximum sample count to retain (default: 2048, minimum enforced internally).</param>
    /// <returns>The same <see cref="ITimerBuilder"/> instance for fluent chaining.</returns>
    /// <remarks>
    /// Larger capacities improve percentile accuracy at the cost of more memory and sorting overhead.  
    /// Values less than the internal minimum (256) are clamped.
    /// </remarks>
    public ITimerBuilder WithHistogramCapacity(int capacity)
    {
        _capacity = capacity;
        return this;
    }

    /// <summary>
    /// Finalizes configuration and creates a new <see cref="ITimerMetric"/> instance.
    /// </summary>
    /// <returns>
    /// A <see cref="TimerMetric"/> configured with the identifier, name, tags,
    /// and sliding window capacity specified via <see cref="WithHistogramCapacity"/>.
    /// </returns>
    /// <example>
    /// <code>
    /// var timer = factory.Timer("svc.processing.time", "Processing Time")
    ///                    .WithHistogramCapacity(1024)
    ///                    .Build();
    ///
    /// using (timer.Start())
    /// {
    ///     DoWork();
    /// }
    /// </code>
    /// </example>
    public override ITimerMetric Build()
        => new TimerMetric(Id, Name, MaterializeTags(), _capacity);
}
