// <copyright file="ISignalRMetrics.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SignalR.Abstractions;

/// <summary>
/// Defines the contract for collecting SignalR-related metrics within the NetMetric framework.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose</b><br/>
/// This interface describes the metrics surface exposed by SignalR instrumentation. Implementations
/// (e.g., a production <c>SignalRMetricSet</c> or a testing-friendly no-op) can forward these measurements
/// to your preferred telemetry pipeline.
/// </para>
/// <para>
/// <b>Scope</b><br/>
/// Metrics cover: connection lifecycle (active totals, disconnect reasons, lifetimes), negotiation attempts,
/// hub method invocations (duration and outcomes), streaming item counts, payload sizes, groups and users,
/// authentication outcomes, and error occurrences.
/// </para>
/// <para>
/// <b>Thread Safety</b><br/>
/// Implementations are expected to be thread-safe; methods may be invoked concurrently by multiple hubs
/// and connections.
/// </para>
/// <para>
/// <b>Tagging model (typical)</b><br/>
/// Unless stated otherwise:
/// <list type="bullet">
///   <item><description><c>hub</c>: Hub class name (e.g., <c>ChatHub</c>).</description></item>
///   <item><description><c>transport</c>: Connection transport (e.g., <c>ws</c>, <c>sse</c>, <c>lp</c>, or <c>unknown</c>).</description></item>
///   <item><description><c>method</c>: Hub method name (e.g., <c>SendMessage</c>).</description></item>
///   <item><description><c>direction</c>: <c>in</c> (client→server) or <c>out</c> (server→client).</description></item>
///   <item><description><c>reason</c>: Disconnect reason (e.g., <c>normal</c>, <c>error</c>).</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Usage</b><br/>
/// Most applications do not call this interface directly. Instead, NetMetric’s middleware and filters
/// (e.g., a hub filter for method timing and a middleware for negotiation) call these methods
/// at the appropriate points in the SignalR lifecycle.
/// </para>
/// </remarks>
public interface ISignalRMetrics
{
    /// <summary>
    /// Increments the active connection gauge and the cumulative connection counter.
    /// </summary>
    /// <param name="hub">The hub name (typically the hub class name).</param>
    /// <param name="transport">The transport type (e.g., <c>ws</c>, <c>sse</c>, <c>lp</c>, or <c>unknown</c>).</param>
    /// <remarks>
    /// Tags: <c>{hub, transport}</c>. The gauge reflects active connections; the counter records total connections opened.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Called when a connection becomes active:
    /// metrics.IncConnection(hub: "ChatHub", transport: "ws");
    /// ]]></code>
    /// </example>
    void IncConnection(string hub, string transport);

    /// <summary>
    /// Decrements the active connection gauge and increments the disconnection counter with the specified reason.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="transport">The transport type.</param>
    /// <param name="reason">The disconnection reason (framework- or app-defined, e.g., <c>normal</c>, <c>error</c>).</param>
    /// <remarks>
    /// Tags: <c>{hub, transport, reason}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Called when a connection ends normally:
    /// metrics.DecConnection("ChatHub", "ws", reason: "normal");
    /// ]]></code>
    /// </example>
    void DecConnection(string hub, string transport, string reason);

    /// <summary>
    /// Records the lifetime duration of a connection.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="transport">The transport type.</param>
    /// <param name="reason">The reason the connection ended (e.g., <c>normal</c>, <c>error</c>).</param>
    /// <param name="duration">The duration of the connection.</param>
    /// <remarks>
    /// Histogram unit: milliseconds. Tags: <c>{hub, transport, reason}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// metrics.ObserveConnectionDuration("ChatHub", "ws", "normal", duration: TimeSpan.FromSeconds(45));
    /// ]]></code>
    /// </example>
    void ObserveConnectionDuration(string hub, string transport, string reason, TimeSpan duration);

    /// <summary>
    /// Records a negotiation attempt, optionally with duration and fallback status.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="chosenTransport">The chosen transport (e.g., <c>ws</c>), if known; otherwise <see langword="null"/>.</param>
    /// <param name="duration">The duration of the negotiation, if measured.</param>
    /// <param name="fallback">Whether a transport fallback occurred; <see langword="null"/> if unknown.</param>
    /// <remarks>
    /// Counter tags: <c>{hub, transport, fallback?}</c>. Duration histogram unit: milliseconds.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Successful negotiation to WebSockets in ~12 ms:
    /// metrics.Negotiated("ChatHub", chosenTransport: "ws", duration: TimeSpan.FromMilliseconds(12), fallback: false);
    /// ]]></code>
    /// </example>
    void Negotiated(string hub, string? chosenTransport, TimeSpan? duration = null, bool? fallback = null);

    /// <summary>
    /// Records a hub method invocation, including execution time and outcome.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="method">The hub method name.</param>
    /// <param name="elapsed">The execution duration.</param>
    /// <param name="ok"><see langword="true"/> if the invocation succeeded; otherwise <see langword="false"/>.</param>
    /// <remarks>
    /// Duration histogram tags: <c>{hub, method}</c>. Outcome counter tags: <c>{hub, method, outcome=ok|error}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var sw = Stopwatch.StartNew();
    /// await hub.SendMessage(user, message);
    /// sw.Stop();
    /// metrics.ObserveMethod("ChatHub", "SendMessage", sw.Elapsed, ok: true);
    /// ]]></code>
    /// </example>
    void ObserveMethod(string hub, string method, TimeSpan elapsed, bool ok);

    /// <summary>
    /// Increments the counter for items sent or received in hub streaming methods.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="method">The streaming hub method name.</param>
    /// <param name="outbound">
    /// <see langword="true"/> for server-to-client streaming; <see langword="false"/> for client-to-server streaming.
    /// </param>
    /// <remarks>
    /// Tags: <c>{hub, method, direction=in|out}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Server pushes one streamed item to clients:
    /// metrics.ObserveStreamItem("TelemetryHub", "StreamMetrics", outbound: true);
    /// ]]></code>
    /// </example>
    void ObserveStreamItem(string hub, string method, bool outbound);

    /// <summary>
    /// Records the size of a hub message.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="method">The hub method name.</param>
    /// <param name="direction">The direction (<c>in</c> for client→server, <c>out</c> for server→client).</param>
    /// <param name="bytes">The message size in bytes.</param>
    /// <remarks>
    /// Histogram unit: bytes. Tags: <c>{hub, method, direction}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Record a 3.1 KB incoming payload for SendMessage:
    /// metrics.ObserveMessageSize("ChatHub", "SendMessage", direction: "in", bytes: 3172);
    /// ]]></code>
    /// </example>
    void ObserveMessageSize(string hub, string method, string direction, int bytes);

    /// <summary>
    /// Increments the counter for a client being added to a group.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="group">The group name.</param>
    /// <remarks>
    /// Tags: <c>{hub, group}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// metrics.GroupAdded("ChatHub", "moderators");
    /// ]]></code>
    /// </example>
    void GroupAdded(string hub, string group);

    /// <summary>
    /// Increments the counter for a client being removed from a group.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="group">The group name.</param>
    /// <remarks>
    /// Tags: <c>{hub, group}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// metrics.GroupRemoved("ChatHub", "moderators");
    /// ]]></code>
    /// </example>
    void GroupRemoved(string hub, string group);

    /// <summary>
    /// Increments the counter for a message sent to a group.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="group">The group name.</param>
    /// <remarks>
    /// Tags: <c>{hub, group}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// metrics.GroupSent("ChatHub", "moderators");
    /// ]]></code>
    /// </example>
    void GroupSent(string hub, string group);

    /// <summary>
    /// Sets the gauge value representing the number of active users for a hub.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="count">The current number of active users.</param>
    /// <remarks>
    /// Tag: <c>{hub}</c>. The gauge is an absolute value (it is not incremented/decremented).
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Periodically publish the currently connected unique user count:
    /// metrics.UserActiveGauge("ChatHub", count: 128);
    /// ]]></code>
    /// </example>
    void UserActiveGauge(string hub, long count);

    /// <summary>
    /// Increments the authentication outcome counter for a hub.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="outcome">The authentication outcome (e.g., <c>success</c>, <c>failure</c>).</param>
    /// <param name="policy">The optional authorization policy or scheme name.</param>
    /// <remarks>
    /// Tags: <c>{hub, outcome[, policy]}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// metrics.AuthOutcome("ChatHub", outcome: "success", policy: "DefaultPolicy");
    /// ]]></code>
    /// </example>
    void AuthOutcome(string hub, string outcome, string? policy = null);

    /// <summary>
    /// Records an error occurrence during a SignalR operation.
    /// </summary>
    /// <param name="hub">The hub name.</param>
    /// <param name="scope">The scope or operation where the error occurred (e.g., <c>negotiate</c>, <c>method</c>).</param>
    /// <param name="exceptionType">The CLR exception type name (e.g., <c>OperationCanceledException</c>).</param>
    /// <remarks>
    /// Tags: <c>{hub, scope, exception_type}</c>. Implementations may truncate or omit the exception type
    /// to control cardinality.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// try
    /// {
    ///     await hub.SendMessage(user, msg, ct);
    /// }
    /// catch (OperationCanceledException)
    /// {
    ///     metrics.ObserveError("ChatHub", scope: "method", exceptionType: nameof(OperationCanceledException));
    ///     throw;
    /// }
    /// ]]></code>
    /// </example>
    void ObserveError(string hub, string scope, string exceptionType);
}
