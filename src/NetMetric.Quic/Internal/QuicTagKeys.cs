// <copyright file="QuicTagKeys.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Quic.Internal;

/// <summary>
/// Defines standardized tag (label) keys used when publishing fallback QUIC
/// <c>EventCounter</c> metrics (i.e., counters that do not map to a first-class instrument).
/// </summary>
/// <remarks>
/// <para>
/// These keys are attached as dimensions to the multi-gauge series emitted by
/// <see cref="QuicMetricSet.PublishEventCounter(string, string, string, double)"/>.
/// They allow downstream backends to group and filter unknown counters without
/// exploding metric cardinality.
/// </para>
/// <para>
/// Typical usage:
/// </para>
/// <example>
/// <code>
/// var tags = new Dictionary&lt;string, string&gt;
/// {
///     [QuicTagKeys.Source] = "MsQuic",
///     [QuicTagKeys.Name]   = "smoothed-rtt",
///     [QuicTagKeys.Unit]   = "ms",
/// };
/// multiGauge.AddSibling(
///     id: "quic.eventcounter.gauge.1A2B3C4D",
///     name: "QUIC smoothed-rtt",
///     value: 12.3,
///     tags: tags);
/// </code>
/// </example>
/// <para>
/// <b>Thread-safety:</b> These are constant string keys and therefore thread-safe.
/// </para>
/// </remarks>
internal static class QuicTagKeys
{
    /// <summary>
    /// Tag key for the source provider that emitted the original <c>EventCounter</c>,
    /// typically equal to <c>EventSource.Name</c> (e.g., <c>"MsQuic"</c> or <c>"System.Net.Quic"</c>).
    /// </summary>
    /// <remarks>
    /// Helps distinguish the same counter name coming from different providers.
    /// </remarks>
    /// <example>
    /// <code>
    /// tags[QuicTagKeys.Source] = "System.Net.Quic";
    /// </code>
    /// </example>
    public const string Source = "source";

    /// <summary>
    /// Tag key for the raw (uninferred) counter name as emitted by the provider.
    /// This preserves the original identifier for diagnostics and correlation.
    /// </summary>
    /// <remarks>
    /// Use this to retain human-readable names (e.g., <c>"smoothed-rtt"</c>,
    /// <c>"congestion-window"</c>), even if the exported series ID is normalized and hashed.
    /// </remarks>
    /// <example>
    /// <code>
    /// tags[QuicTagKeys.Name] = "congestion-window";
    /// </code>
    /// </example>
    public const string Name = "name";

    /// <summary>
    /// Tag key for the unit associated with the counter value (e.g., <c>"ms"</c>, <c>"bytes"</c>, <c>"count"</c>).
    /// </summary>
    /// <remarks>
    /// Including the unit as a tag avoids ambiguity across heterogeneous counters and
    /// makes UI formatting and conversions easier in observability backends.
    /// </remarks>
    /// <example>
    /// <code>
    /// tags[QuicTagKeys.Unit] = "bytes";
    /// </code>
    /// </example>
    public const string Unit = "unit";
}
