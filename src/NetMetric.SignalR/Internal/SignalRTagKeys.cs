// <copyright file="SignalRTagKeys.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SignalR.Internal;

/// <summary>
/// Defines standardized tag (label) keys used across SignalR metrics emitted by NetMetric.
/// </summary>
/// <remarks>
/// <para>
/// Tags act as stable dimensions attached to metric instruments (counters, gauges, histograms)
/// so that you can filter, group, and aggregate telemetry consistently across hubs, methods,
/// transports, and outcomes.
/// </para>
/// <para>
/// <b>Conventions</b><br/>
/// Tag keys are lower-case, ASCII, and short (<c>hub</c>, <c>method</c>, <c>transport</c>, etc.).
/// Tag values should also be short and normalized when possible (e.g., <c>ws</c>, <c>sse</c>, <c>lp</c>).
/// </para>
/// <para>
/// <b>Example</b>
/// <code language="csharp"><![CDATA[
/// // Build a counter with stable tags:
/// var counter = factory.Counter("signalr.hub.messages.total", "Total hub messages")
///     .WithTags(t => {
///         t.Add(SignalRTagKeys.Hub, "ChatHub");
///         t.Add(SignalRTagKeys.Method, "SendMessage");
///         t.Add(SignalRTagKeys.Outcome, "ok");
///     })
///     .Build();
///
/// counter.Increment();
/// ]]></code>
/// </para>
/// </remarks>
internal static class SignalRTagKeys
{
    /// <summary>
    /// Tag key for the SignalR hub name.
    /// </summary>
    /// <remarks>
    /// Example values: <c>ChatHub</c>, <c>NotificationsHub</c>.
    /// </remarks>
    public const string Hub = "hub";

    /// <summary>
    /// Tag key for the invoked hub method name.
    /// </summary>
    /// <remarks>
    /// Example values: <c>SendMessage</c>, <c>JoinRoom</c>.
    /// </remarks>
    public const string Method = "method";

    /// <summary>
    /// Tag key for an operation outcome.
    /// </summary>
    /// <remarks>
    /// Typical values: <c>ok</c>, <c>error</c>.
    /// </remarks>
    public const string Outcome = "outcome";

    /// <summary>
    /// Tag key for message or stream direction.
    /// </summary>
    /// <remarks>
    /// Typical values: <c>in</c> (client → server), <c>out</c> (server → client).
    /// </remarks>
    public const string Direction = "direction";

    /// <summary>
    /// Tag key for the connection transport protocol.
    /// </summary>
    /// <remarks>
    /// Typical values: <c>ws</c> (WebSockets), <c>sse</c> (Server-Sent Events),
    /// <c>lp</c> (Long Polling), <c>unknown</c>.
    /// </remarks>
    public const string Transport = "transport";

    /// <summary>
    /// Tag key for a disconnect reason.
    /// </summary>
    /// <remarks>
    /// Typical values: <c>normal</c>, <c>timeout</c>, <c>error</c>, <c>server_shutdown</c>.
    /// </remarks>
    public const string Reason = "reason";

    /// <summary>
    /// Tag key for a SignalR group name.
    /// </summary>
    /// <remarks>
    /// Example values: <c>room-42</c>, <c>admins</c>.
    /// </remarks>
    public const string Group = "group";

    /// <summary>
    /// Tag key indicating whether a negotiation fell back to a less-preferred transport.
    /// </summary>
    /// <remarks>
    /// Values: <see langword="true"/> or <see langword="false"/>.
    /// </remarks>
    public const string Fallback = "fallback";

    /// <summary>
    /// Tag key for the name of the authentication/authorization policy in effect.
    /// </summary>
    /// <remarks>
    /// Example values: <c>DefaultPolicy</c>, <c>RequireAuthenticatedUser</c>, <c>MyCustomPolicy</c>.
    /// </remarks>
    public const string Policy = "policy";

    /// <summary>
    /// Tag key for a high-level scope in which an operation or error occurred.
    /// </summary>
    /// <remarks>
    /// Example values: <c>negotiate</c>, <c>connect</c>, <c>method</c>, <c>group</c>, <c>stream</c>.
    /// </remarks>
    public const string Scope = "scope";

    /// <summary>
    /// Tag key for the CLR exception type name observed when recording an error.
    /// </summary>
    /// <remarks>
    /// Example values: <c>OperationCanceledException</c>, <c>HubException</c>.
    /// </remarks>
    public const string ExceptionType = "exception_type";
}

/// <summary>
/// Immutable key that uniquely identifies a (hub, transport) pair for instrument caching.
/// </summary>
/// <param name="Hub">The SignalR hub name.</param>
/// <param name="Transport">The normalized transport tag value (e.g., <c>ws</c>, <c>sse</c>, <c>lp</c>, <c>unknown</c>).</param>
/// <remarks>
/// Used as a dictionary key for gauges/counters that are partitioned by hub and transport.
/// </remarks>
internal readonly record struct HubTransportKey(string Hub, string Transport);

/// <summary>
/// Immutable key that uniquely identifies a (hub, transport, reason) triple for disconnect metrics.
/// </summary>
/// <param name="Hub">The SignalR hub name.</param>
/// <param name="Transport">The normalized transport tag value.</param>
/// <param name="Reason">The disconnect reason (e.g., <c>normal</c>, <c>error</c>).</param>
/// <remarks>
/// Used to partition disconnect counters and connection-duration histograms.
/// </remarks>
internal readonly record struct HubTransportReasonKey(string Hub, string Transport, string Reason);

/// <summary>
/// Immutable key that uniquely identifies a (hub, method) pair for method-level metrics.
/// </summary>
/// <param name="Hub">The SignalR hub name.</param>
/// <param name="Method">The hub method name.</param>
/// <remarks>
/// Used to cache method-duration histograms and message counters per hub/method.
/// </remarks>
internal readonly record struct HubMethodKey(string Hub, string Method);

/// <summary>
/// Immutable key that uniquely identifies a (hub, method, outcome) triple for outcome counters.
/// </summary>
/// <param name="Hub">The SignalR hub name.</param>
/// <param name="Method">The hub method name.</param>
/// <param name="Outcome">The outcome value (typically <c>ok</c> or <c>error</c>).</param>
/// <remarks>
/// Used to cache total message counters partitioned by success/failure.
/// </remarks>
internal readonly record struct HubMethodOutcomeKey(string Hub, string Method, string Outcome);

/// <summary>
/// Immutable key that uniquely identifies a (hub, method, direction) triple for size histograms.
/// </summary>
/// <param name="Hub">The SignalR hub name.</param>
/// <param name="Method">The hub method name.</param>
/// <param name="Direction">The direction value (e.g., <c>in</c> for client→server, <c>out</c> for server→client).</param>
/// <remarks>
/// Used to cache message-size histograms partitioned by hub, method, and direction.
/// </remarks>
internal readonly record struct HubMethodDirectionKey(string Hub, string Method, string Direction);
