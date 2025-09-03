// <copyright file="KestrelTagKeys.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kestrel.Internal;

/// <summary>
/// Defines constant tag (label) keys used for Kestrel server metrics.
/// </summary>
/// <remarks>
/// <para>
/// Tag keys represent the dimension names that are attached to
/// metrics for filtering, grouping, and aggregating in observability backends
/// (e.g., Prometheus, OpenTelemetry-compatible exporters).
/// </para>
/// <para>
/// Typical usage in metric construction:
/// <code language="csharp"><![CDATA[
/// factory.Gauge(KestrelMetricNames.ConnectionsActive, "Active connections")
///        .WithTags(tags =>
///        {
///            tags.Add(KestrelTagKeys.Protocol, "h2");
///            tags.Add(KestrelTagKeys.Transport, "tcp");
///        })
///        .Build();
/// ]]></code>
/// </para>
/// </remarks>
internal static class KestrelTagKeys
{
    /// <summary>
    /// Tag key representing the application protocol used by Kestrel.
    /// </summary>
    /// <value>
    /// Common values include:
    /// <list type="bullet">
    ///   <item><description><c>h1</c> — HTTP/1.x</description></item>
    ///   <item><description><c>h2</c> — HTTP/2</description></item>
    ///   <item><description><c>h3</c> — HTTP/3</description></item>
    /// </list>
    /// </value>
    public const string Protocol = "protocol";

    /// <summary>
    /// Tag key representing the transport mechanism underlying the connection.
    /// </summary>
    /// <value>
    /// Common values include:
    /// <list type="bullet">
    ///   <item><description><c>tcp</c> — Transmission Control Protocol (used for HTTP/1.x and HTTP/2).</description></item>
    ///   <item><description><c>quic</c> — QUIC transport (used for HTTP/3).</description></item>
    /// </list>
    /// </value>
    public const string Transport = "transport";

    /// <summary>
    /// Tag key representing the reason or category associated with the metric.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The meaning of the value depends on the context:
    /// <list type="bullet">
    ///   <item><description>For connection resets: <c>reset</c></description></item>
    ///   <item><description>For errors: <c>bad_request</c>, <c>app_error</c></description></item>
    ///   <item><description>For termination causes: implementation-specific reason codes.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public const string Reason = "reason";
}
