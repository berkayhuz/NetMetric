// <copyright file="DefaultSignalRMetrics.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SignalR.Internal;

/// <summary>
/// Provides the default, no-operation (no-op) implementation of <see cref="ISignalRMetrics"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose</b><br/>
/// This implementation satisfies the <see cref="ISignalRMetrics"/> contract without recording
/// or exporting any metrics. It is suitable when metrics are disabled, not yet configured,
/// or intentionally suppressed in test environments.
/// </para>
/// <para>
/// <b>Behavior</b><br/>
/// All interface methods are implemented as empty bodies. Invoking them has no side effects
/// and incurs negligible overhead (no allocations, no I/O).
/// </para>
/// <para>
/// <b>Thread Safety</b><br/>
/// The type is stateless and therefore inherently thread-safe and reentrant.
/// </para>
/// <para>
/// <b>Usage</b><br/>
/// Register as a fallback so consumers can depend on <see cref="ISignalRMetrics"/>
/// without conditional logic:
/// </para>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Program.cs / Startup.cs
/// services.AddSingleton<ISignalRMetrics, DefaultSignalRMetrics>();
/// ]]></code>
/// </example>
/// </remarks>
/// <seealso cref="ISignalRMetrics"/>
public sealed class DefaultSignalRMetrics : ISignalRMetrics
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultSignalRMetrics"/> class.
    /// </summary>
    public DefaultSignalRMetrics() { }

    /// <inheritdoc/>
    public void IncConnection(string hub, string transport) { }

    /// <inheritdoc/>
    public void DecConnection(string hub, string transport, string reason) { }

    /// <inheritdoc/>
    public void ObserveConnectionDuration(string hub, string transport, string reason, TimeSpan duration) { }

    /// <inheritdoc/>
    public void Negotiated(string hub, string? chosenTransport, TimeSpan? duration = null, bool? fallback = null) { }

    /// <inheritdoc/>
    public void ObserveMethod(string hub, string method, TimeSpan elapsed, bool ok) { }

    /// <inheritdoc/>
    public void ObserveStreamItem(string hub, string method, bool outbound) { }

    /// <inheritdoc/>
    public void ObserveMessageSize(string hub, string method, string direction, int bytes) { }

    /// <inheritdoc/>
    public void GroupAdded(string hub, string group) { }

    /// <inheritdoc/>
    public void GroupRemoved(string hub, string group) { }

    /// <inheritdoc/>
    public void GroupSent(string hub, string group) { }

    /// <inheritdoc/>
    public void UserActiveGauge(string hub, long count) { }

    /// <inheritdoc/>
    public void AuthOutcome(string hub, string outcome, string? policy = null) { }

    /// <summary>
    /// Records an error occurrence for a hub/scope/exception-type triple.
    /// </summary>
    /// <param name="hub">The hub where the error occurred.</param>
    /// <param name="scope">The logical scope (e.g., <c>"negotiate"</c>, <c>"method"</c>).</param>
    /// <param name="exceptionType">The CLR exception type name (e.g., <c>OperationCanceledException</c>).</param>
    /// <remarks>
    /// In this no-op implementation, the call is ignored and no metrics are emitted.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// try
    /// {
    ///     await next();
    /// }
    /// catch (OperationCanceledException)
    /// {
    ///     _metrics.ObserveError(hub: "ChatHub", scope: "method", exceptionType: nameof(OperationCanceledException));
    ///     throw;
    /// }
    /// ]]></code>
    /// </example>
    public void ObserveError(string hub, string scope, string exceptionType) { }

    /// <summary>
    /// Backward-compatibility shim for legacy callers that used <c>Error</c>.
    /// Forwards to <see cref="ObserveError(string, string, string)"/>; no metrics are recorded.
    /// </summary>
    /// <param name="hub">The hub where the error occurred.</param>
    /// <param name="scope">The logical scope (e.g., <c>"negotiate"</c>, <c>"invoke"</c>).</param>
    /// <param name="exceptionType">The CLR exception type name.</param>
    /// <remarks>
    /// Prefer <see cref="ObserveError(string, string, string)"/> in new code.
    /// </remarks>
    [Obsolete("Use ObserveError(hub, scope, exceptionType) instead.", error: false)]
    public void Error(string hub, string scope, string exceptionType)
        => ObserveError(hub, scope, exceptionType);
}
