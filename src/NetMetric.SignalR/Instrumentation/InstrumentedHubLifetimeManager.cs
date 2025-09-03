// <copyright file="InstrumentedHubLifetimeManager.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.SignalR;

namespace NetMetric.SignalR.Instrumentation;

/// <summary>
/// Provides an instrumented decorator around <see cref="HubLifetimeManager{THub}"/> that observes
/// selected SignalR group operations (e.g., <c>AddToGroupAsync</c>, <c>RemoveFromGroupAsync</c>, <c>SendGroupAsync</c>)
/// and emits metrics through an <see cref="ISignalRMetrics"/> implementation.
/// </summary>
/// <typeparam name="THub">
/// The concrete hub type handled by the lifetime manager. Must derive from <see cref="Hub"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>What this class does</b><br/>
/// This decorator forwards all operations to an "inner" <see cref="HubLifetimeManager{THub}"/>, and—before delegating—
/// increments counters or gauges that represent group membership changes and group message sends.
/// The emitted metrics are tagged at least with <c>hub = typeof(<typeparamref name="THub"/>).Name</c> and, where applicable,
/// <c>group = groupName</c>, so they can be consumed by the NetMetric pipeline and exported to your observability stack.
/// </para>
/// <para>
/// <b>Thread-safety</b><br/>
/// The class does not maintain any mutable state of its own; it relies on the provided <see cref="ISignalRMetrics"/>
/// implementation to be thread-safe. Instances of <see cref="HubLifetimeManager{THub}"/> are typically used concurrently
/// by the SignalR runtime, so your metrics implementation should be safe for concurrent use.
/// </para>
/// <para>
/// <b>Performance</b><br/>
/// The metric calls are intentionally minimal (single counter/gauge increments) and are performed before delegating to
/// the inner manager. If your metrics sink is high-latency, consider buffering or sampling in the implementation of
/// <see cref="ISignalRMetrics"/> rather than modifying this decorator.
/// </para>
/// <para>
/// <b>Usage</b><br/>
/// Register this decorator in the DI container to wrap the default lifetime manager:
/// <code language="csharp"><![CDATA[
/// services.AddSignalR();
/// services.AddSingleton<ISignalRMetrics, DefaultSignalRMetrics>(); // or your concrete metrics
/// services.Decorate<HubLifetimeManager<MyHub>, InstrumentedHubLifetimeManager<MyHub>>();
/// ]]></code>
/// Alternatively (with open generics, e.g., Scrutor):
/// <code language="csharp"><![CDATA[
/// services.TryDecorate(typeof(HubLifetimeManager<>), typeof(InstrumentedHubLifetimeManager<>));
/// ]]></code>
/// </para>
/// </remarks>
/// <seealso cref="HubLifetimeManager{THub}"/>
/// <seealso cref="ISignalRMetrics"/>
/// <seealso cref="SignalRHubFilter"/>
/// <seealso cref="SignalRMetricsOptions"/>
public sealed class InstrumentedHubLifetimeManager<THub> : HubLifetimeManager<THub> where THub : Hub
{
    private readonly HubLifetimeManager<THub> _inner;
    private readonly ISignalRMetrics _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstrumentedHubLifetimeManager{THub}"/> class.
    /// </summary>
    /// <param name="inner">The underlying <see cref="HubLifetimeManager{THub}"/> to which calls will be delegated.</param>
    /// <param name="metrics">The metrics sink used to record group operations.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="inner"/> or <paramref name="metrics"/> is <see langword="null"/>.
    /// </exception>
    public InstrumentedHubLifetimeManager(HubLifetimeManager<THub> inner, ISignalRMetrics metrics)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <summary>
    /// Adds the specified connection to a SignalR group and records a <c>GroupAdded</c> metric
    /// tagged with the current hub name and the target group.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="groupName">The destination group name.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous add-to-group operation.</returns>
    /// <remarks>
    /// Emits <see cref="ISignalRMetrics.GroupAdded(string, string)"/> with <c>hub = typeof(<typeparamref name="THub"/>).Name</c>
    /// before delegating to the inner lifetime manager.
    /// </remarks>
    public override Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        _metrics.GroupAdded(typeof(THub).Name, groupName);
        return _inner.AddToGroupAsync(connectionId, groupName, cancellationToken);
    }

    /// <summary>
    /// Removes the specified connection from a SignalR group and records a <c>GroupRemoved</c> metric
    /// tagged with the current hub name and the target group.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="groupName">The source group name.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous remove-from-group operation.</returns>
    /// <remarks>
    /// Emits <see cref="ISignalRMetrics.GroupRemoved(string, string)"/> with <c>hub = typeof(<typeparamref name="THub"/>).Name</c>
    /// before delegating to the inner lifetime manager.
    /// </remarks>
    public override Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        _metrics.GroupRemoved(typeof(THub).Name, groupName);
        return _inner.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
    }

    /// <summary>
    /// Sends a hub method invocation to all connections in the specified group and records a <c>GroupSent</c> metric.
    /// </summary>
    /// <param name="groupName">The target group.</param>
    /// <param name="methodName">The hub method name to invoke on clients.</param>
    /// <param name="args">The hub method arguments.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Emits <see cref="ISignalRMetrics.GroupSent(string, string)"/> with <c>hub = typeof(<typeparamref name="THub"/>).Name</c>
    /// before delegating to the inner lifetime manager.
    /// </remarks>
    public override Task SendGroupAsync(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default)
    {
        _metrics.GroupSent(typeof(THub).Name, groupName);
        return _inner.SendGroupAsync(groupName, methodName, args, cancellationToken);
    }

    /// <summary>
    /// Sends a hub method invocation to all connections in the specified groups.
    /// </summary>
    /// <param name="groupNames">The target groups.</param>
    /// <param name="methodName">The hub method name to invoke on clients.</param>
    /// <param name="args">The hub method arguments.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// This method does not emit additional group metrics beyond what is recorded in
    /// <see cref="SendGroupAsync(string, string, object?[], CancellationToken)"/>; if you require per-group metrics for
    /// multi-group sends, consider instrumenting at a higher level.
    /// </remarks>
    public override Task SendGroupsAsync(IReadOnlyList<string> groupNames, string methodName, object?[] args, CancellationToken cancellationToken = default)
        => _inner.SendGroupsAsync(groupNames, methodName, args, cancellationToken);

    /// <summary>
    /// Sends a hub method invocation to a single user (identified by user ID).
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="methodName">The hub method name to invoke on clients.</param>
    /// <param name="args">The hub method arguments.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    public override Task SendUserAsync(string userId, string methodName, object?[] args, CancellationToken cancellationToken = default)
        => _inner.SendUserAsync(userId, methodName, args, cancellationToken);

    /// <summary>
    /// Sends a hub method invocation to all connections except the specified set.
    /// </summary>
    /// <param name="methodName">The hub method name to invoke on clients.</param>
    /// <param name="args">The hub method arguments.</param>
    /// <param name="excludedConnectionIds">Connections to exclude from the send.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    public override Task SendAllExceptAsync(string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default)
        => _inner.SendAllExceptAsync(methodName, args, excludedConnectionIds, cancellationToken);

    /// <summary>
    /// Notifies the lifetime manager that a new connection has been established.
    /// </summary>
    /// <param name="connection">The connection context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// This decorator does not emit connection-level metrics here; such metrics are typically produced by
    /// <see cref="SignalRHubFilter"/> (e.g., <c>IncConnection</c>/<c>DecConnection</c>) or middleware during negotiation.
    /// </remarks>
    public override Task OnConnectedAsync(HubConnectionContext connection)
        => _inner.OnConnectedAsync(connection);

    /// <summary>
    /// Notifies the lifetime manager that a connection has been terminated.
    /// </summary>
    /// <param name="connection">The connection context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// This decorator does not emit connection-level metrics here; see <see cref="SignalRHubFilter"/> for lifecycle metrics.
    /// </remarks>
    public override Task OnDisconnectedAsync(HubConnectionContext connection)
        => _inner.OnDisconnectedAsync(connection);

    /// <summary>
    /// Sends a hub method invocation to all connections.
    /// </summary>
    /// <param name="methodName">The hub method name to invoke on clients.</param>
    /// <param name="args">The hub method arguments.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    public override Task SendAllAsync(string methodName, object?[] args, CancellationToken cancellationToken = default)
        => _inner.SendAllAsync(methodName, args, cancellationToken);

    /// <summary>
    /// Sends a hub method invocation to a single connection.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="methodName">The hub method name to invoke on clients.</param>
    /// <param name="args">The hub method arguments.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    public override Task SendConnectionAsync(string connectionId, string methodName, object?[] args, CancellationToken cancellationToken = default)
        => _inner.SendConnectionAsync(connectionId, methodName, args, cancellationToken);

    /// <summary>
    /// Sends a hub method invocation to multiple specific connections.
    /// </summary>
    /// <param name="connectionIds">The target connections.</param>
    /// <param name="methodName">The hub method name to invoke on clients.</param>
    /// <param name="args">The hub method arguments.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    public override Task SendConnectionsAsync(IReadOnlyList<string> connectionIds, string methodName, object?[] args, CancellationToken cancellationToken = default)
        => _inner.SendConnectionsAsync(connectionIds, methodName, args, cancellationToken);

    /// <summary>
    /// Sends a hub method invocation to a group, excluding the specified set of connections.
    /// </summary>
    /// <param name="groupName">The target group.</param>
    /// <param name="methodName">The hub method name to invoke on clients.</param>
    /// <param name="args">The hub method arguments.</param>
    /// <param name="excludedConnectionIds">Connections to exclude from the send.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    public override Task SendGroupExceptAsync(string groupName, string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default)
        => _inner.SendGroupExceptAsync(groupName, methodName, args, excludedConnectionIds, cancellationToken);

    /// <summary>
    /// Sends a hub method invocation to multiple users.
    /// </summary>
    /// <param name="userIds">The target users.</param>
    /// <param name="methodName">The hub method name to invoke on clients.</param>
    /// <param name="args">The hub method arguments.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    public override Task SendUsersAsync(IReadOnlyList<string> userIds, string methodName, object?[] args, CancellationToken cancellationToken = default)
        => _inner.SendUsersAsync(userIds, methodName, args, cancellationToken);
}
