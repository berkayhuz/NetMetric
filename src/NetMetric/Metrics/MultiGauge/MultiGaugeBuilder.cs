// <copyright file="MultiGaugeBuilder.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.MultiGauge;

/// <summary>
/// Fluent builder for creating <see cref="IMultiGauge"/> instruments, which hold
/// multiple gauge-like siblings (keyed values) under a single metric identity.
/// </summary>
/// <remarks>
/// <para>
/// A <b>multi-gauge</b> is useful when you want to report a set of related instantaneous
/// values in one metric, e.g., per-disk usage, per-endpoint concurrency, or per-queue depth.
/// Each sibling is identified by an internal key (implementation-specific) and carries a value.
/// </para>
/// <para>
/// This builder is typically obtained from <see cref="IMetricFactory.MultiGauge(string, string)"/> and
/// supports the standard fluent metadata configuration inherited from
/// <see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}"/>:
/// <list type="bullet">
///   <item><description><see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithUnit(string)"/></description></item>
///   <item><description><see cref = "NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithDescription(string)" /></description></item>
///   <item><description><see cref="NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithTag(string, string)"/></description></item>
///   <item><description><see cref = "NetMetric.Metrics.Builders.InstrumentBuilderBase{TMetric}.WithTags(System.Action{NetMetric.Abstractions.TagList})" /></description ></item>
/// </list>
/// When done, call <see cref="Build"/> to materialize a thread-safe <see cref="MultiGaugeMetric"/>.
/// </para>
/// <para>
/// Tag precedence during materialization is <b>local</b> &gt; <b>resource</b> &gt; <b>global</b>, followed by
/// sanitization according to <see cref="MetricOptions"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example: per-disk usage with reset-on-get (default)
/// var mg = factory.MultiGauge("system.disk.usage", "Disk Usage")
///     .WithUnit("%")
///     .WithDescription("Per-disk usage percentage")
///     .WithInitialCapacity(128)
///     .WithTag("host", "node-42")
///     .Build();
///
/// mg.AddOrUpdate("disk.c", 73.5);
/// mg.AddOrUpdate("disk.d", 41.2);
///
/// // Reading the snapshot clears siblings when ResetOnGet is true (default)
/// var snapshot = (MultiGaugeValue)mg.GetValue();
/// // snapshot contains ("disk.c", 73.5), ("disk.d", 41.2)
///
/// // After GetValue(), mg is empty again (when ResetOnGet = true)
/// </code>
/// </example>
/// <example>
/// <code>
/// // Example: accumulate values across reads (no reset on get)
/// var mg = factory.MultiGauge("svc.connections", "Active Connections")
///     .WithUnit("count")
///     .WithResetOnGet(false)
///     .Build();
///
/// mg.AddOrUpdate("api", 17);
/// var s1 = (MultiGaugeValue)mg.GetValue();  // still keeps values
/// var s2 = (MultiGaugeValue)mg.GetValue();  // values persist since ResetOnGet=false
/// </code>
/// </example>
internal sealed class MultiGaugeBuilder : InstrumentBuilderBase<IMultiGauge>, IMultiGaugeBuilder
{
    private int _initialCapacity = 64;
    private bool _resetOnGet = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiGaugeBuilder"/> class.
    /// </summary>
    /// <param name="id">Stable unique identifier for the metric (e.g., <c>"system.disk.usage"</c>).</param>
    /// <param name="name">Human-readable name for the metric (e.g., <c>"Disk Usage"</c>).</param>
    /// <param name="options">Metric configuration options (global/resource tags, tag limits, etc.).</param>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="id"/> or <paramref name="name"/> is null or whitespace.
    /// </exception>
    public MultiGaugeBuilder(string id, string name, MetricOptions options)
        : base(id, name, options) { }

    /// <summary>
    /// Suggests the initial capacity for underlying storage of gauge siblings.
    /// </summary>
    /// <param name="capacity">
    /// Initial sibling capacity to preallocate. This is a performance hint and not a hard limit;
    /// the multi-gauge may grow beyond this value as needed.
    /// </param>
    /// <returns>The current <see cref="IMultiGaugeBuilder"/> instance for fluent chaining.</returns>
    /// <remarks>
    /// Large capacities may reduce re-allocations in high-cardinality scenarios at the cost of memory.
    /// </remarks>
    public IMultiGaugeBuilder WithInitialCapacity(int capacity)
    {
        _initialCapacity = capacity;
        return this;
    }

    /// <summary>
    /// Controls whether the multi-gauge clears its values after each snapshot.
    /// </summary>
    /// <param name="reset">
    /// If <c>true</c> (default), calling <c>GetValue()</c> returns the current set of siblings and
    /// then clears them (useful for “report-and-reset” collection). If <c>false</c>, values persist
    /// across reads until explicitly overwritten or removed by the metric implementation.
    /// </param>
    /// <returns>The current <see cref="IMultiGaugeBuilder"/> instance for fluent chaining.</returns>
    public IMultiGaugeBuilder WithResetOnGet(bool reset = true)
    {
        _resetOnGet = reset;
        return this;
    }

    /// <summary>
    /// Finalizes configuration and creates a new <see cref="IMultiGauge"/> instance.
    /// </summary>
    /// <returns>
    /// A thread-safe <see cref="MultiGaugeMetric"/> configured with the selected identifier, name,
    /// merged/sanitized tags, and the specified initial capacity and reset-on-get behavior.
    /// </returns>
    /// <remarks>
    /// The resulting metric typically exposes operations like <c>AddOrUpdate(key, value)</c> and
    /// <c>Remove(key)</c> (exact surface may vary by implementation) and returns a <c>MultiGaugeValue</c>
    /// snapshot from <see cref="MetricBase.GetValue"/>.
    /// </remarks>
    public override IMultiGauge Build()
        => new MultiGaugeMetric(Id, Name, MaterializeTags(), _initialCapacity, _resetOnGet);
}
