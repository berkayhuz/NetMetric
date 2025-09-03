// <copyright file="QuicOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Quic.Internal;

/// <summary>
/// Configuration options for QUIC EventCounter metrics collection.
/// Controls sampling intervals, fallback behavior, cardinality limits, logging, and more.
/// </summary>
public sealed class QuicOptions
{
    /// <summary>
    /// Interval in seconds at which EventCounters should be sampled.
    /// </summary>
    public int SamplingIntervalSec { get; set; } = 1;

    /// <summary>
    /// Whether fallback publishing is enabled for unknown EventCounters.
    /// These are recorded as multi-gauge metrics.
    /// </summary>
    public bool EnableFallback { get; set; } = true;

    /// <summary>
    /// Maximum number of fallback time series to allow.
    /// Once exceeded, further fallback metrics are dropped.
    /// </summary>
    public int MaxFallbackSeries { get; set; } = 200;

    /// <summary>
    /// Maximum length of the normalized metric name used in fallback identifiers.
    /// </summary>
    public int MaxFallbackNameLength { get; set; } = 80;

    /// <summary>
    /// List of allowed EventSource provider names to listen to.
    /// Default includes Microsoft/managed QUIC implementations.
    /// </summary>
    public IReadOnlyList<string> AllowedProviders { get; init; }
        = new List<string> { "Microsoft-Quic", "MsQuic", "System.Net.Quic" };

    /// <summary>
    /// Throttle window in seconds for error log messages to avoid flooding.
    /// </summary>
    public int LogThrottleSeconds { get; set; } = 10;

    /// <summary>
    /// Whether internal self-diagnostic metrics (e.g., listener status, unknown counters) should be published.
    /// </summary>
    public bool EnableSelfMetrics { get; set; } = true;

    /// <summary>
    /// If true, non-ASCII characters are ignored during name normalization for performance reasons.
    /// </summary>
    public bool NormalizeAsciiOnly { get; set; } = true;

    /// <summary>
    /// Returns the list of allowed providers as an efficient frozen set.
    /// Used internally for fast lookup.
    /// </summary>
    internal FrozenSet<string> AllowedProvidersFrozen => AllowedProviders.ToFrozenSet(StringComparer.Ordinal);
}
