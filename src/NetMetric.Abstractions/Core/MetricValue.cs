// <copyright file="MetricValue.cs" company="NetMetric"
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Represents the abstract base type for all metric values.
/// <para>
/// This type is serialized polymorphically and serves as the common contract
/// for different metric representations such as gauges, counters,
/// distributions, summaries, histograms, and multi-sample values.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(GaugeValue), "gauge")]
[JsonDerivedType(typeof(CounterValue), "counter")]
[JsonDerivedType(typeof(DistributionValue), "distribution")]
[JsonDerivedType(typeof(SummaryValue), "summary")]
[JsonDerivedType(typeof(BucketHistogramValue), "histogram")]
[JsonDerivedType(typeof(MultiSampleValue), "multi")]
public abstract record MetricValue;

// ---- Singular value types ----

/// <summary>
/// Represents a single numeric measurement that can vary up or down over time.
/// </summary>
/// <param name="Value">The current gauge value.</param>
[DebuggerDisplay("Gauge: {Value}")]
public sealed record GaugeValue(double Value) : MetricValue;

/// <summary>
/// Represents a monotonically increasing counter value.
/// </summary>
/// <param name="Value">The total accumulated count.</param>
[DebuggerDisplay("Counter: {Value}")]
public sealed record CounterValue(long Value) : MetricValue;

/// <summary>
/// Represents a statistical distribution of observed values,
/// providing common percentiles such as median (p50), p90, and p99.
/// </summary>
/// <param name="Count">The number of observations.</param>
/// <param name="Min">The minimum observed value.</param>
/// <param name="Max">The maximum observed value.</param>
/// <param name="P50">The 50th percentile (median).</param>
/// <param name="P90">The 90th percentile.</param>
/// <param name="P99">The 99th percentile.</param>
[DebuggerDisplay("Dist: n={Count}, min={Min}, p50={P50}, p90={P90}, p99={P99}, max={Max}")]
public sealed record DistributionValue(
    long Count,
    double Min,
    double Max,
    double P50,
    double P90,
    double P99
) : MetricValue;

/// <summary>
/// Represents a statistical summary of observed values with user-defined quantiles.
/// </summary>
/// <param name="Count">The number of observations.</param>
/// <param name="Min">The minimum observed value.</param>
/// <param name="Max">The maximum observed value.</param>
/// <param name="Quantiles">
/// A mapping of requested quantiles (0..1) to their estimated values.
/// For example, 0.95 → 95th percentile estimate.
/// </param>
public sealed record SummaryValue(
    long Count,
    double Min,
    double Max,
    IReadOnlyDictionary<double, double> Quantiles
) : MetricValue;

/// <summary>
/// Represents a histogram of observed values grouped into predefined buckets.
/// </summary>
/// <param name="Count">The total number of recorded observations.</param>
/// <param name="Min">The minimum observed value.</param>
/// <param name="Max">The maximum observed value.</param>
/// <param name="Buckets">The list of bucket boundaries.</param>
/// <param name="Counts">The count of values that fell into each bucket.</param>
/// <param name="Sum">The sum of all observed values.</param>
public sealed record BucketHistogramValue(
    long Count,
    double Min,
    double Max,
    IReadOnlyList<double> Buckets,
    IReadOnlyList<long> Counts,
    double Sum
) : MetricValue;

// ---- Multi-sample types ----

/// <summary>
/// Represents a collection of metric samples, each with its own identity and tags.
/// Useful for reporting multiple related measurements in a single payload.
/// </summary>
/// <param name="Items">The collection of multi-sample items.</param>
public sealed record MultiSampleValue(
    IReadOnlyList<MultiSampleItem> Items
) : MetricValue;

/// <summary>
/// Represents an individual item within a <see cref="MultiSampleValue"/> collection.
/// </summary>
/// <param name="Id">The stable identifier of the sample.</param>
/// <param name="Name">The human-readable name of the sample.</param>
/// <param name="Tags">The set of key-value tags providing additional context.</param>
/// <param name="Value">The metric value associated with this sample.</param>
[DebuggerDisplay("{Id} ({Name}), Tags={{{Tags.Count}}}")]
public sealed record MultiSampleItem(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Tags,
    MetricValue Value
);
