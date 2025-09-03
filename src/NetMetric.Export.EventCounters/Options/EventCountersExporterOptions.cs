// <copyright file="EventCountersExporterOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.EventCounters.Options;

/// <summary>
/// Provides configuration options for the EventCounters-based exporter.
/// These options control how metrics are mapped, named, and exposed through .NET
/// <see cref="System.Diagnostics.Tracing.EventCounter"/> primitives.
/// </summary>
/// <remarks>
/// <para>
/// The exporter internally uses <see cref="System.Diagnostics.Tracing.EventSource"/> and
/// <see cref="System.Diagnostics.Tracing.EventCounter"/> to publish metrics. Tools such as
/// <c>dotnet-counters</c>, <c>dotnet-monitor</c>, PerfView, or Visual Studio's diagnostics
/// tooling can observe the emitted counters under the configured <see cref="SourceName"/>.
/// </para>
/// <para>
/// Be mindful that enabling options which expand the number of counters (for example,
/// tag expansion or per-bucket publication) may increase the total number of active counters
/// and the associated overhead. Consider your tag cardinality and histogram bucket counts
/// when selecting these options.
/// </para>
/// </remarks>
/// <example>
/// The following example shows how to configure and register the exporter options using
/// Microsoft.Extensions.Options:
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using NetMetric.Export.EventCounters.Options;
///
/// var services = new ServiceCollection();
///
/// services.Configure<EventCountersExporterOptions>(o =>
/// {
///     // EventSource name visible to tools such as dotnet-counters
///     o.SourceName = "NetMetric";
///
///     // Append tag key-value pairs to counter names when helpful for disambiguation
///     o.IncludeTagsInCounterNames = false;
///
///     // Publish histogram buckets as individual counters (e.g., le_10, le_100, ...)
///     o.PublishBuckets = false;
///
///     // Publish each item of multi-sample values as separate counters
///     o.PublishMultiSampleItems = false;
///
///     // Cap the number of counters tracked for delta computation
///     o.CounterCacheCapacity = 2048;
///
///     // Cosmetic display units (e.g., "ms", "bytes"); set to null to omit
///     o.DefaultDisplayUnits = "ms";
/// });
/// ]]></code>
/// You can then resolve <c>IOptions&lt;EventCountersExporterOptions&gt;</c> in your exporter
/// or hosting component and apply the configuration.
/// </example>
/// <seealso cref="System.Diagnostics.Tracing.EventSource"/>
/// <seealso cref="System.Diagnostics.Tracing.EventCounter"/>
public sealed class EventCountersExporterOptions
{
    /// <summary>
    /// Gets or sets the <see cref="System.Diagnostics.Tracing.EventSource"/> name under which counters
    /// will be published. Observability tools use this name to discover and subscribe to the exported metrics.
    /// </summary>
    /// <value>
    /// The EventSource name. The default value is <c>"NetMetric"</c>.
    /// </value>
    public string SourceName { get; set; } = "NetMetric";

    /// <summary>
    /// Gets or sets a value indicating whether metric tags should be appended to counter names
    /// for additional disambiguation.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to include the metric name plus tag key-value pairs in the counter name;
    /// <see langword="false"/> to use only the metric name. The default value is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// Enabling this option may significantly increase the number of distinct counters if the metric set
    /// contains high-cardinality tags.
    /// </remarks>
    public bool IncludeTagsInCounterNames { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether each bucket of a histogram (for example, a "BucketHistogram")
    /// should be published as an individual counter.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to emit a distinct counter per bucket (for example, <c>le_10</c>, <c>le_100</c>);
    /// <see langword="false"/> to omit per-bucket counters. The default value is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// Per-bucket publication can provide finer-grained visibility but increases the number of counters and overhead.
    /// </remarks>
    public bool PublishBuckets { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether individual items in a multi-sample metric
    /// (for example, a "MultiSample") should be exposed as separate counters.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to publish each sample item as a distinct counter;
    /// <see langword="false"/> to omit per-item counters. The default value is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// Enabling this option can produce a large number of counters when the number of sample items is high or unbounded.
    /// </remarks>
    public bool PublishMultiSampleItems { get; set; }

    /// <summary>
    /// Gets or sets the maximum capacity of the cache used for tracking cumulative counters
    /// and computing interval deltas.
    /// </summary>
    /// <value>
    /// The maximum number of unique counters that can be tracked. The default value is <c>2048</c>.
    /// </value>
    /// <remarks>
    /// When the capacity is exceeded, the exporter may evict older entries, which can affect the accuracy of
    /// delta computations for counters that are no longer cached.
    /// </remarks>
    public int CounterCacheCapacity { get; set; } = 2048;

    /// <summary>
    /// Gets or sets the default display units for counters.
    /// </summary>
    /// <value>
    /// A string representing the units (for example, <c>"ms"</c> or <c>"bytes"</c>), or <see langword="null"/>
    /// to omit units. This setting is cosmetic and affects only how counters are displayed by tooling.
    /// </value>
    public string? DefaultDisplayUnits { get; set; }
}
