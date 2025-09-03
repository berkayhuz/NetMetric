// <copyright file="NetworkTimingOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Network.Configuration;

/// <summary>
/// Represents the configuration options for network timing metrics, such as HTTP client request durations.
/// This class allows configuring the metric ID and display name prefixes, query string inclusion, server timing parsing, and more.
/// </summary>
public sealed class NetworkTimingOptions
{
    /// <summary>
    /// Gets or sets the metric ID prefix used in the network timing metrics (e.g., "http.client").
    /// </summary>
    public string MetricIdPrefix { get; init; } = "http.client";

    /// <summary>
    /// Gets or sets the metric display name prefix used in the network timing metrics (e.g., "HTTP Client").
    /// </summary>
    public string MetricNamePrefix { get; init; } = "HTTP Client";

    /// <summary>
    /// Gets or sets a value indicating whether to include the query string as part of the path tag.
    /// If true, the query string will be included in the path for metrics (e.g., "http.client.path").
    /// </summary>
    public bool IncludeQueryString { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether to parse the <c>Server-Timing</c> header and publish the sub-metrics.
    /// If true, the server's <c>Server-Timing</c> header will be parsed and metrics for individual timing items will be recorded.
    /// </summary>
    public bool ParseServerTiming { get; init; } = true;

    /// <summary>
    /// Gets or sets the maximum length of the path. If the path length exceeds this value, it will be truncated.
    /// This is useful to avoid excessively long paths being stored as part of the metric.
    /// </summary>
    public int? MaxPathLength { get; init; } = 256;

    /// <summary>
    /// Gets or sets a value indicating whether to include the download size (in bytes) as a tag.
    /// If true, the total response bytes will be recorded as part of the metric tags.
    /// </summary>
    public bool TagResponseBytes { get; init; } = true;
}
