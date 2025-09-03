// © NetMetric 2025 - Apache-2.0
// <copyright file="TimeSeriesFactory.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Google.Api;
using Google.Cloud.Monitoring.V3;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace NetMetric.Export.Stackdriver.Internals;

/// <summary>
/// Provides focused, allocation-minimal factory methods for constructing
/// <see cref="TimeSeries"/> instances compatible with Google Cloud Monitoring
/// (Stackdriver).
/// </summary>
/// <remarks>
/// <para>
/// These helpers encapsulate the wire-format details for common metric shapes:
/// <em>gauge</em> (instantaneous), <em>cumulative</em> (monotonic counters),
/// and <em>distribution</em> (histogram-like bucketed counts).
/// They set the appropriate <see cref="Point.Interval"/> and <see cref="TypedValue"/>
/// members for the requested shape.
/// </para>
/// <para>
/// The methods here do <strong>not</strong> create metric descriptors. Ensure the
/// metric type (e.g., <c>custom.googleapis.com/netmetric/requests_total</c>)
/// and its label schema are registered ahead of writes. See
/// <see cref="Metric"/> and <see cref="MetricServiceClient"/> for details.
/// </para>
/// <para><strong>Thread safety:</strong> All factory methods are pure and thread-safe.</para>
/// <para><strong>Validation:</strong> Null label maps are rejected. Other semantic
/// validation (e.g., metric type format, non-null monitored resource, monotonicity
/// of counters) is enforced by the backend service at write time.</para>
/// </remarks>
/// <example>
/// <code><![CDATA[
/// using System;
/// using System.Collections.Generic;
/// using Google.Api;
/// using Google.Cloud.Monitoring.V3;
/// using Google.Protobuf.WellKnownTypes;
///
/// // Build a monitored resource (example: "global")
/// var mr = new MonitoredResource { Type = "global" };
///
/// // Common metric labels
/// var labels = new Dictionary<string, string>
/// {
///     ["service"] = "checkout",
///     ["version"] = "1.2.3"
/// };
///
/// // 1) Gauge (double)
/// var gauge = TimeSeriesFactory.GaugeDouble(
///     metricType: "custom.googleapis.com/netmetric/heap_usage_mb",
///     mr: mr,
///     labels: labels,
///     value: 512.75,
///     ts: DateTimeOffset.UtcNow);
///
/// // 2) Cumulative counter (int64)
/// var counter = TimeSeriesFactory.CumulativeInt64(
///     metricType: "custom.googleapis.com/netmetric/requests_total",
///     mr: mr,
///     labels: labels,
///     value: 12345L,
///     start: DateTimeOffset.UtcNow.AddMinutes(-5),
///     end:   DateTimeOffset.UtcNow);
///
/// // 3) Distribution with explicit buckets
/// var bounds = new double[] { 5, 10, 25, 50, 100 };  // N bounds -> N+1 buckets
/// var bucketCounts = new long[] { 3, 7, 12, 5, 2, 1 };
/// var dist = TimeSeriesFactory.Distribution(
///     metricType: "custom.googleapis.com/netmetric/latency_ms",
///     mr: mr,
///     labels: labels,
///     count: 30,
///     min: 2.0,
///     max: 120.0,
///     bucketBounds: bounds,
///     counts: bucketCounts,
///     ts: DateTimeOffset.UtcNow);
/// ]]></code>
/// </example>
/// <seealso cref="TimeSeries"/>
/// <seealso cref="Metric"/>
/// <seealso cref="Point"/>
/// <seealso cref="TimeInterval"/>
/// <seealso cref="TypedValue"/>
/// <seealso cref="Distribution"/>
internal static class TimeSeriesFactory
{
    /// <summary>
    /// Creates a <em>gauge</em> metric time series that records an instantaneous
    /// <see cref="double"/> value at a specific point in time.
    /// </summary>
    /// <param name="metricType">The fully-qualified metric type
    /// (e.g., <c>custom.googleapis.com/netmetric/heap_usage_mb</c>).</param>
    /// <param name="mr">The monitored resource that produced the metric.</param>
    /// <param name="labels">Metric label key/values to attach to the series.</param>
    /// <param name="value">The observed value.</param>
    /// <param name="ts">The observation timestamp (uses <see cref="TimeInterval.EndTime"/>).</param>
    /// <returns>A <see cref="TimeSeries"/> containing a single <see cref="Point"/> with
    /// <see cref="TypedValue.DoubleValue"/> populated and interval end time set to <paramref name="ts"/>.</returns>
    /// <remarks>
    /// <para>
    /// For gauge metrics, only the interval end time is set. The backend interprets this as
    /// an instantaneous sample.
    /// </para>
    /// <para>
    /// <strong>Note:</strong> <paramref name="mr"/> must reference a valid monitored
    /// resource type and labels expected for that type. Invalid resources or metric
    /// types will be rejected at write time.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="labels"/> is <c>null</c>.</exception>
    public static TimeSeries GaugeDouble(
        string metricType,
        MonitoredResource mr,
        IReadOnlyDictionary<string, string> labels,
        double value,
        DateTimeOffset ts)
        => new TimeSeries
        {
            Metric = CreateMetric(metricType, labels),
            Resource = mr,
            Points =
            {
                new Point
                {
                    Interval = new TimeInterval { EndTime = Timestamp.FromDateTimeOffset(ts) },
                    Value = new TypedValue { DoubleValue = value }
                }
            }
        };

    /// <summary>
    /// Creates a <em>cumulative</em> (monotonic) counter time series with an
    /// <see cref="long"/> sample value over a start–end interval.
    /// </summary>
    /// <param name="metricType">The fully-qualified metric type
    /// (e.g., <c>custom.googleapis.com/netmetric/requests_total</c>).</param>
    /// <param name="mr">The monitored resource that produced the metric.</param>
    /// <param name="labels">Metric label key/values to attach to the series.</param>
    /// <param name="value">The cumulative counter value at <paramref name="end"/>.</param>
    /// <param name="start">Inclusive start of the cumulative interval.</param>
    /// <param name="end">End of the cumulative interval (must be ≥ <paramref name="start"/>).</param>
    /// <returns>A <see cref="TimeSeries"/> containing a single <see cref="Point"/> with
    /// <see cref="TypedValue.Int64Value"/> populated and both
    /// <see cref="TimeInterval.StartTime"/> and <see cref="TimeInterval.EndTime"/> set.</returns>
    /// <remarks>
    /// <para>
    /// The provided <paramref name="value"/> should be the total count accumulated
    /// from <paramref name="start"/> to <paramref name="end"/> and should never
    /// decrease across successive writes for the same series key (metric type + labels + resource).
    /// </para>
    /// <para>
    /// Many backends expect non-overlapping, forward-moving intervals for cumulative
    /// series. Using overlapping or decreasing intervals may result in rejected writes
    /// or skewed rate calculations.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="labels"/> is <c>null</c>.</exception>
    public static TimeSeries CumulativeInt64(
        string metricType,
        MonitoredResource mr,
        IReadOnlyDictionary<string, string> labels,
        long value,
        DateTimeOffset start,
        DateTimeOffset end)
        => new TimeSeries
        {
            Metric = CreateMetric(metricType, labels),
            Resource = mr,
            Points =
            {
                new Point
                {
                    Interval = new TimeInterval
                    {
                        StartTime = Timestamp.FromDateTimeOffset(start),
                        EndTime   = Timestamp.FromDateTimeOffset(end)
                    },
                    Value = new TypedValue { Int64Value = value }
                }
            }
        };

    /// <summary>
    /// Creates a <em>distribution</em> (histogram) time series using explicit bucket
    /// boundaries and bucket counts, optionally including observed range metadata.
    /// </summary>
    /// <param name="metricType">The fully-qualified metric type
    /// (e.g., <c>custom.googleapis.com/netmetric/latency_ms</c>).</param>
    /// <param name="mr">The monitored resource that produced the metric.</param>
    /// <param name="labels">Metric label key/values to attach to the series.</param>
    /// <param name="count">Total number of observations across all buckets.</param>
    /// <param name="min">Minimum observed value (used when <paramref name="count"/> &gt; 0).</param>
    /// <param name="max">Maximum observed value (used when <paramref name="count"/> &gt; 0).</param>
    /// <param name="bucketBounds">
    /// The ordered list of explicit bucket upper bounds (length = <c>N</c>), producing <c>N+1</c> buckets.
    /// </param>
    /// <param name="counts">
    /// The per-bucket counts (length must equal <c>bucketBounds.Count + 1</c>).
    /// The final bucket captures all values &gt;= the last bound.
    /// </param>
    /// <param name="ts">The observation timestamp (uses <see cref="TimeInterval.EndTime"/>).</param>
    /// <returns>
    /// A <see cref="TimeSeries"/> whose <see cref="TypedValue.DistributionValue"/> contains
    /// <see cref="Distribution.Types.BucketOptions"/> with explicit bounds and the provided counts.
    /// </returns>
    /// <remarks>
    /// <para>
    /// When <paramref name="count"/> is positive, <see cref="Distribution.Range"/> is set
    /// using <paramref name="min"/> and <paramref name="max"/>. If you track the sum of values,
    /// you may also compute the mean (sum / count) and set <c>Distribution.Mean</c> prior to sending.
    /// </para>
    /// <para>
    /// Bounds must be strictly increasing as required by the service. Length mismatches or
    /// unsorted bounds will result in argument or backend validation errors.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="counts"/> or <paramref name="bucketBounds"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="counts"/> length is not <c>bucketBounds.Count + 1</c>.
    /// </exception>
    public static TimeSeries Distribution(
        string metricType,
        MonitoredResource mr,
        IReadOnlyDictionary<string, string> labels,
        long count,
        double min,
        double max,
        IReadOnlyList<double> bucketBounds,
        IReadOnlyList<long> counts,
        DateTimeOffset ts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(bucketBounds);

        if (counts.Count != bucketBounds.Count + 1)
            throw new ArgumentException("BucketCounts length must equal Bounds length + 1.", nameof(counts));

        var dist = new Distribution
        {
            Count = count,
            BucketOptions = new Distribution.Types.BucketOptions
            {
                ExplicitBuckets = new Distribution.Types.BucketOptions.Types.Explicit
                {
                    Bounds = { bucketBounds }
                }
            },
            BucketCounts = { counts }
        };

        if (count > 0)
        {
            dist.Range = new Distribution.Types.Range { Min = min, Max = max };
            // If you also track the sum of observations, consider: dist.Mean = sum / count;
        }

        return new TimeSeries
        {
            Metric = CreateMetric(metricType, labels),
            Resource = mr,
            Points =
            {
                new Point
                {
                    Interval = new TimeInterval { EndTime = Timestamp.FromDateTimeOffset(ts) },
                    Value = new TypedValue { DistributionValue = dist }
                }
            }
        };
    }

    /// <summary>
    /// Constructs a <see cref="Metric"/> with the given type and label map.
    /// </summary>
    /// <param name="metricType">Fully-qualified metric type to assign to <see cref="Metric.Type"/>.</param>
    /// <param name="labels">Label key/values to be copied to <see cref="Metric.Labels"/>.</param>
    /// <returns>A <see cref="Metric"/> instance with labels populated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="labels"/> is <c>null</c>.</exception>
    private static Metric CreateMetric(string metricType, IReadOnlyDictionary<string, string> labels)
    {
        var metric = new Metric { Type = metricType };
        CopyLabels(metric.Labels, labels);
        return metric;
    }

    /// <summary>
    /// Copies all entries from a read-only label dictionary into a target
    /// <see cref="MapField{TKey,TValue}"/>. Null values are converted to <see cref="string.Empty"/>.
    /// </summary>
    /// <param name="target">The destination label map (e.g., <see cref="Metric.Labels"/>).</param>
    /// <param name="src">The source label dictionary.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="target"/> or <paramref name="src"/> is <c>null</c>.
    /// </exception>
    private static void CopyLabels(
        MapField<string, string> target,
        IReadOnlyDictionary<string, string> src)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(src);
        if (src.Count == 0) return;

        if (src is IDictionary<string, string> dict)
        {
            target.Add(dict);
            return;
        }

        foreach (var kv in src)
            target.Add(kv.Key, kv.Value ?? string.Empty);
    }
}
