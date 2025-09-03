// <copyright file="SignalRMetricNames.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SignalR.Internal;

/// <summary>
/// Provides the canonical metric name constants used by SignalR instrumentation
/// within the NetMetric framework.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose</b><br/>
/// These constants define the stable, dotted metric identifiers emitted by
/// <see cref="ISignalRMetrics"/> implementations (for example, a concrete
/// <c>SignalRMetricSet</c>). They are intended to be consumed by exporters,
/// dashboards, alert rules, and other monitoring backends without requiring
/// string duplication across the codebase.
/// </para>
///
/// <para>
/// <b>Naming convention</b><br/>
/// Names follow a hierarchical dotted scheme prefixed with <c>signalr.</c>.
/// The remainder conveys the entity and aggregation kind (for example,
/// <c>.total</c>, <c>.duration</c>, <c>.active</c>, <c>.size</c>).
/// </para>
///
/// <para>
/// <b>Tagging model</b><br/>
/// Every metric is designed to be recorded with a stable set of tags (dimensions)
/// such as <c>hub</c>, <c>method</c>, <c>transport</c>, <c>group</c>, and so on
/// (see <see cref="SignalRTagKeys"/> and <see cref="SignalRTagValues"/>).
/// Consumers should rely on the metric <i>name + tag schema</i> combination when
/// querying or visualizing data.
/// </para>
///
/// <para>
/// <b>Usage</b><br/>
/// Always reference these constants instead of hard-coding strings. This avoids
/// typos and ensures consistency across producers and consumers.
/// </para>
///
/// <example>
/// The following example shows how an <see cref="ISignalRMetrics"/> implementation
/// might declare instruments using the standardized names:
/// <code language="csharp"><![CDATA[
/// // Using a hypothetical IMetricFactory
/// var gauge = factory.Gauge(SignalRMetricNames.ConnectionGauge, "SignalR active connections")
///                    .WithTags(t => { t.Add(SignalRTagKeys.Hub, hub); t.Add(SignalRTagKeys.Transport, transport); })
///                    .Build();
///
/// var hist = factory.Histogram(SignalRMetricNames.MethodDuration, "Hub method duration (ms)")
///                   .WithUnit("ms")
///                   .WithTags(t => { t.Add(SignalRTagKeys.Hub, hub); t.Add(SignalRTagKeys.Method, method); })
///                   .Build();
/// ]]></code>
/// </example>
/// </remarks>
/// <seealso cref="ISignalRMetrics"/>
/// <seealso cref="SignalRTagKeys"/>
/// <seealso cref="SignalRTagValues"/>
internal static class SignalRMetricNames
{
    /// <summary>
    /// Gauge: current number of active SignalR connections.
    /// </summary>
    /// <remarks>Tags: <c>{hub, transport}</c>.</remarks>
    public const string ConnectionGauge = "signalr.connections.active";

    /// <summary>
    /// Counter: cumulative total number of SignalR connections established since process start.
    /// </summary>
    /// <remarks>Tags: <c>{hub, transport}</c>.</remarks>
    public const string ConnectionsTotal = "signalr.connections.total";

    /// <summary>
    /// Counter: cumulative total number of disconnections observed.
    /// </summary>
    /// <remarks>Tags: <c>{hub, transport, reason}</c>.</remarks>
    public const string DisconnectsTotal = "signalr.disconnects.total";

    /// <summary>
    /// Histogram: lifetime duration of SignalR connections, measured in milliseconds.
    /// </summary>
    /// <remarks>Tags: <c>{hub, transport, reason}</c>; unit: <c>ms</c>.</remarks>
    public const string ConnectionDuration = "signalr.connection.duration";

    /// <summary>
    /// Counter: cumulative total number of negotiation attempts.
    /// </summary>
    /// <remarks>Tags: <c>{hub, transport, fallback}</c>.</remarks>
    public const string NegotiationsTotal = "signalr.negotiations.total";

    /// <summary>
    /// Histogram: duration of negotiation requests, measured in milliseconds.
    /// </summary>
    /// <remarks>Tags: <c>{hub, transport, fallback}</c>; unit: <c>ms</c>.</remarks>
    public const string NegotiationDur = "signalr.negotiation.duration";

    /// <summary>
    /// Histogram: duration of hub method invocations, measured in milliseconds.
    /// </summary>
    /// <remarks>Tags: <c>{hub, method}</c>; unit: <c>ms</c>.</remarks>
    public const string MethodDuration = "signalr.hub.method.duration";

    /// <summary>
    /// Counter: cumulative total number of hub messages (both successful and failed).
    /// </summary>
    /// <remarks>Tags: <c>{hub, method, outcome}</c> where <c>outcome ∈ {ok, error}</c>.</remarks>
    public const string MessagesTotal = "signalr.hub.messages.total";

    /// <summary>
    /// Histogram: distribution of hub message payload sizes, measured in bytes.
    /// </summary>
    /// <remarks>Tags: <c>{hub, method, direction}</c> where <c>direction ∈ {in, out}</c>; unit: <c>bytes</c>.</remarks>
    public const string MessageSize = "signalr.message.size";

    /// <summary>
    /// Counter: cumulative total number of group add operations.
    /// </summary>
    /// <remarks>Tags: <c>{hub, group}</c>.</remarks>
    public const string GroupsAddTotal = "signalr.groups.add.total";

    /// <summary>
    /// Counter: cumulative total number of group remove operations.
    /// </summary>
    /// <remarks>Tags: <c>{hub, group}</c>.</remarks>
    public const string GroupsRemoveTotal = "signalr.groups.remove.total";

    /// <summary>
    /// Counter: cumulative total number of group message sends.
    /// </summary>
    /// <remarks>Tags: <c>{hub, group}</c>.</remarks>
    public const string GroupsSendTotal = "signalr.groups.send.total";

    /// <summary>
    /// Gauge: current number of active groups (groups with ≥ 1 member).
    /// </summary>
    /// <remarks>Tags: <c>{hub}</c>.</remarks>
    public const string GroupsActiveGauge = "signalr.groups.active";

    /// <summary>
    /// Gauge: current number of active users connected to a hub.
    /// </summary>
    /// <remarks>Tags: <c>{hub}</c>.</remarks>
    public const string UsersActiveGauge = "signalr.users.active";

    /// <summary>
    /// Counter: cumulative total number of items sent/received in hub streaming methods.
    /// </summary>
    /// <remarks>Tags: <c>{hub, method, direction}</c> where <c>direction ∈ {in, out}</c>.</remarks>
    public const string StreamItemsTotal = "signalr.stream.items.total";

    /// <summary>
    /// Counter: cumulative total number of errors encountered in hub operations.
    /// </summary>
    /// <remarks>Tags: <c>{hub, scope, exception_type}</c>.</remarks>
    public const string ErrorsTotal = "signalr.errors.total";

    /// <summary>
    /// Counter: cumulative total number of authentication outcomes.
    /// </summary>
    /// <remarks>Tags: <c>{hub, outcome, policy}</c> (for example, <c>outcome ∈ {success, failure}</c>).</remarks>
    public const string AuthOutcomeTotal = "signalr.auth.outcome.total";
}
