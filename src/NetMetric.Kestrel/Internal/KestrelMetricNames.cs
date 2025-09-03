// <copyright file="KestrelMetricNames.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kestrel.Internal;

/// <summary>
/// Defines canonical metric name constants for Kestrel server instrumentation.
/// </summary>
/// <remarks>
/// <para>
/// The names defined here follow NetMetric conventions and map directly to gauges,
/// counters, and histograms created by <see cref="KestrelMetricSet"/>.  
/// These metrics cover connection lifecycle, protocol-specific stream activity, and
/// TLS handshake performance.
/// </para>
/// <para>
/// Consistent use of these constants ensures compatibility with dashboards,
/// alerting rules, and long-term historical analysis.
/// </para>
/// </remarks>
/// <example>
/// Example: Creating a gauge for active connections with protocol and transport tags:
/// <code language="csharp"><![CDATA[
/// var gauge = factory.Gauge(KestrelMetricNames.ConnectionsActive, "Active connections")
///     .WithTags(t => {
///         t.Add(KestrelTagKeys.Protocol, "h2");
///         t.Add(KestrelTagKeys.Transport, "tcp");
///     })
///     .Build();
/// ]]></code>
/// </example>
internal static class KestrelMetricNames
{
    /// <summary>
    /// Current number of active Kestrel connections.  
    /// <para>Type: <c>gauge</c></para>  
    /// Labels: <c>{protocol = h1|h2|h3, transport = tcp|quic}</c>
    /// </summary>
    public const string ConnectionsActive = "kestrel.connections.active";

    /// <summary>
    /// Total number of Kestrel connections established since process start.  
    /// <para>Type: <c>counter</c></para>  
    /// Labels: <c>{protocol, transport}</c>
    /// </summary>
    public const string ConnectionsTotal = "kestrel.connections.total";

    /// <summary>
    /// Number of connection resets observed.  
    /// <para>Type: <c>counter</c></para>  
    /// Labels: <c>{protocol?, reason}</c>
    /// </summary>
    public const string ConnectionResets = "kestrel.connection.resets";

    /// <summary>
    /// Number of connection-level errors encountered.  
    /// <para>Type: <c>counter</c></para>  
    /// Labels: <c>{reason}</c>
    /// </summary>
    public const string ConnectionErrors = "kestrel.connection.errors";

    /// <summary>
    /// Number of keep-alive timeouts that occurred on connections.  
    /// <para>Type: <c>counter</c></para>
    /// </summary>
    public const string KeepAliveTimeouts = "kestrel.keepalive.timeouts";

    /// <summary>
    /// Duration of TLS handshakes in milliseconds.  
    /// <para>Type: <c>histogram</c></para>
    /// </summary>
    public const string TlsHandshakeMs = "kestrel.tls.handshake.duration";

    /// <summary>
    /// Current number of active HTTP/2 streams.  
    /// <para>Type: <c>gauge</c></para>
    /// </summary>
    public const string H2StreamsActive = "kestrel.http2.streams.active";

    /// <summary>
    /// Total number of HTTP/2 streams created since process start.  
    /// <para>Type: <c>counter</c></para>
    /// </summary>
    public const string H2StreamsTotal = "kestrel.http2.streams.total";

    /// <summary>
    /// Current number of active HTTP/3 connections.  
    /// <para>Type: <c>gauge</c></para>
    /// </summary>
    public const string H3ConnectionsActive = "kestrel.http3.connections.active";

    /// <summary>
    /// Current number of active HTTP/3 streams.  
    /// <para>Type: <c>gauge</c></para>
    /// </summary>
    public const string H3StreamsActive = "kestrel.http3.streams.active";

    /// <summary>
    /// Total number of HTTP/3 streams created since process start.  
    /// <para>Type: <c>counter</c></para>
    /// </summary>
    public const string H3StreamsTotal = "kestrel.http3.streams.total";
}
