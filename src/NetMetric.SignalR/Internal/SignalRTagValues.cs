// <copyright file="SignalRTagValues.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SignalR.Internal;

/// <summary>
/// Provides the canonical tag <em>values</em> used by SignalR metrics within NetMetric.
/// These values are paired with keys defined in <see cref="SignalRTagKeys"/> to ensure
/// consistent dimensional data (e.g., <c>outcome</c>, <c>direction</c>, <c>transport</c>).
/// </summary>
/// <remarks>
/// <para>
/// Using constants avoids typos and cardinality drift in metric tags. For example,
/// prefer <see cref="Ok"/> instead of the raw string <c>"ok"</c>.
/// </para>
/// <para>
/// Typical usage is through <c>ISignalRMetrics</c> implementations (e.g., <c>SignalRMetricSet</c>)
/// which record counters, gauges, and histograms with stable tag schemas.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Example: record inbound payload size for a hub method
/// _metrics.ObserveMessageSize(hub: "ChatHub",
///                             method: "SendMessage",
///                             direction: SignalRTagValues.DirIn,
///                             bytes: payloadLength);
///
/// // Example: record hub-method outcomes (ok/error)
/// _metrics.ObserveMethod(hub: "ChatHub",
///                        method: "SendMessage",
///                        elapsed: sw.Elapsed,
///                        ok: true); // Outcome is derived as SignalRTagValues.Ok
/// ]]></code>
/// </example>
/// <seealso cref="SignalRTagKeys"/>
internal static class SignalRTagValues
{
    /// <summary>
    /// Tag value indicating a successful operation outcome.
    /// Intended to be used with <see cref="SignalRTagKeys.Outcome"/>.
    /// </summary>
    public const string Ok = "ok";

    /// <summary>
    /// Tag value indicating a failed operation outcome.
    /// Intended to be used with <see cref="SignalRTagKeys.Outcome"/>.
    /// </summary>
    public const string Error = "error";

    /// <summary>
    /// Tag value indicating inbound message or stream direction (client → server).
    /// Intended to be used with <see cref="SignalRTagKeys.Direction"/>.
    /// </summary>
    public const string DirIn = "in";

    /// <summary>
    /// Tag value indicating outbound message or stream direction (server → client).
    /// Intended to be used with <see cref="SignalRTagKeys.Direction"/>.
    /// </summary>
    public const string DirOut = "out";

    /// <summary>
    /// Tag value for the WebSockets transport (abbreviated).
    /// Intended to be used with <see cref="SignalRTagKeys.Transport"/>.
    /// </summary>
    /// <remarks>
    /// Some components normalize framework transport names (e.g., <c>WebSockets</c> → <c>"ws"</c>)
    /// to ensure stable tag values across platforms and versions.
    /// </remarks>
    public const string TWs = "ws";

    /// <summary>
    /// Tag value for the Server-Sent Events transport (abbreviated).
    /// Intended to be used with <see cref="SignalRTagKeys.Transport"/>.
    /// </summary>
    public const string TSse = "sse";

    /// <summary>
    /// Tag value for the Long Polling transport (abbreviated).
    /// Intended to be used with <see cref="SignalRTagKeys.Transport"/>.
    /// </summary>
    public const string TLp = "lp";

    /// <summary>
    /// Tag value for an unknown or unclassified transport.
    /// Intended to be used with <see cref="SignalRTagKeys.Transport"/>.
    /// </summary>
    public const string TUnknown = "unknown";
}
