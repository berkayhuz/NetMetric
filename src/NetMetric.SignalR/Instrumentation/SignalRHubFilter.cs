// <copyright file="SignalRHubFilter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.SignalR;

namespace NetMetric.SignalR.Instrumentation;

/// <summary>
/// A global <see cref="IHubFilter"/> that instruments SignalR hubs by:
/// <list type="bullet">
///   <item><description>Timing hub method invocations,</description></item>
///   <item><description>Tracking connection lifecycle events (connect/disconnect), and</description></item>
///   <item><description>Optionally measuring the size of incoming hub method arguments.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Error handling:</b> Only <see cref="OperationCanceledException"/> and <see cref="HubException"/> are explicitly
/// caught to record an error metric and then rethrown. Any other exceptions that occur inside the hub method pipeline
/// are not suppressed by this filter and will propagate to the caller. This design avoids the "catch-all" anti-pattern
/// and keeps exception semantics intact.
/// </para>
/// <para>
/// <b>Metrics:</b> Implementations of <see cref="ISignalRMetrics"/> are expected to observe:
/// <list type="bullet">
///   <item><description><c>ObserveMethod(hub, method, duration, ok)</c> — duration and success flag per invocation,</description></item>
///   <item><description><c>ObserveError(hub, scope, exceptionType)</c> — error classification for known exception types,</description></item>
///   <item><description><c>IncConnection/DecConnection</c> — connection counts by hub and transport,</description></item>
///   <item><description><c>ObserveConnectionDuration</c> — total connection lifetime,</description></item>
///   <item><description><c>ObserveMessageSize</c> — optional payload size metric for inbound arguments.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Payload size:</b> When <see cref="SignalRMetricsOptions.EnableMessageSize"/> is enabled, the filter serializes the
/// incoming <see cref="HubInvocationContext.HubMethodArguments"/> to UTF-8 JSON only to compute the byte length. If the
/// serialization fails, the exception currently propagates (no size metric is emitted for that invocation).
/// </para>
/// </remarks>
/// <seealso cref="IHubFilter"/>
/// <seealso cref="ISignalRMetrics"/>
/// <seealso cref="SignalRMetricsOptions"/>
public sealed class SignalRHubFilter : IHubFilter
{
    private readonly ISignalRMetrics _metrics;
    private readonly SignalRMetricsOptions _opt;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRHubFilter"/> class.
    /// </summary>
    /// <param name="metrics">The metrics publisher used to record SignalR instrumentation data.</param>
    /// <param name="opt">Behavioral options that control what this filter measures.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="metrics"/> or <paramref name="opt"/> is <see langword="null"/>.
    /// </exception>
    public SignalRHubFilter(ISignalRMetrics metrics, SignalRMetricsOptions opt)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _opt = opt ?? throw new ArgumentNullException(nameof(opt));
    }

    /// <summary>
    /// Invokes the next hub method delegate while measuring duration, success, and (optionally) inbound payload size.
    /// </summary>
    /// <param name="invocationContext">The invocation context describing the target hub, method, and arguments.</param>
    /// <param name="next">The next delegate in the hub method invocation pipeline.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> producing the hub method result (if any).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="invocationContext"/> or <paramref name="next"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the operation is canceled; the exception is observed for metrics and then rethrown.
    /// </exception>
    /// <exception cref="HubException">
    /// Thrown by SignalR to indicate a hub-related error; the exception is observed for metrics and then rethrown.
    /// </exception>
    /// <remarks>
    /// <para>
    /// If <see cref="SignalRMetricsOptions.EnableMessageSize"/> is <see langword="true"/>, this method attempts to serialize
    /// <see cref="HubInvocationContext.HubMethodArguments"/> using <see cref="JsonSerializer"/> purely to determine the byte
    /// length and emit <c>ObserveMessageSize</c>. Any serialization error will propagate to the caller.
    /// </para>
    /// <para>
    /// Regardless of success or failure, <c>ObserveMethod</c> is emitted in the <c>finally</c> block with the measured
    /// duration and final success flag.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// // Registration (e.g., in Program.cs or Startup):
    /// builder.Services.AddSignalR(options =>
    /// {
    ///     // Other options...
    /// }).AddHubOptions<MyHub>(o => { /* hub-specific options */ });
    ///
    /// // Add filter via DI:
    /// builder.Services.AddSingleton<ISignalRMetrics, DefaultSignalRMetrics>();
    /// builder.Services.AddSingleton(new SignalRMetricsOptions { EnableMessageSize = true });
    /// builder.Services.AddSingleton<IHubFilter, SignalRHubFilter>();
    /// ]]></code>
    /// </example>
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);

        var sw = Stopwatch.StartNew();
        var hub = invocationContext.Hub.GetType().Name;
        var method = invocationContext.HubMethodName;
        var ok = false;

        try
        {
            if (_opt.EnableMessageSize && invocationContext.HubMethodArguments is { Count: > 0 })
            {
                try
                {
                    var bytes = SafeSerializeToUtf8Bytes(invocationContext.HubMethodArguments);
                    _metrics.ObserveMessageSize(hub, method, SignalRTagValues.DirIn, bytes.Length);
                }
                catch
                {
                    // NOTE: Current behavior is to rethrow. This ensures the caller is aware of serialization failures.
                    throw;
                }
            }

            var result = await next(invocationContext).ConfigureAwait(false);
            ok = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            _metrics.ObserveError(hub, scope: "method", exceptionType: nameof(OperationCanceledException));
            throw;
        }
        catch (HubException)
        {
            _metrics.ObserveError(hub, scope: "method", exceptionType: nameof(HubException));
            throw;
        }
        finally
        {
            sw.Stop();
            _metrics.ObserveMethod(hub, method, sw.Elapsed, ok);
        }
    }

    /// <summary>
    /// Called when a new connection is established and the hub is ready for use.
    /// Increments the active connection counter and forwards the call to the next delegate.
    /// </summary>
    /// <param name="context">The lifetime context for the current hub connection.</param>
    /// <param name="next">The next delegate in the lifetime pipeline.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the next delegate has finished.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="next"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The transport type is resolved from <c>IHttpTransportFeature</c> when available; otherwise, it is recorded as
    /// <c>"unknown"</c>.
    /// </remarks>
    public async ValueTask OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, ValueTask> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var hub = context.Hub.GetType().Name;
        var transport = context.Context?
            .Features?
            .Get<Microsoft.AspNetCore.Http.Connections.Features.IHttpTransportFeature>()?
            .TransportType
            .ToString();

        _metrics.IncConnection(hub, transport ?? "unknown");
        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Called when a connection is terminated. Decrements the active connection counter and, if the connection
    /// start time is available, observes the connection duration.
    /// </summary>
    /// <param name="context">The lifetime context for the current hub connection.</param>
    /// <param name="exception">An optional exception that triggered the disconnect; <see langword="null"/> for normal closes.</param>
    /// <param name="next">The next delegate in the lifetime pipeline.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the next delegate has finished.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="next"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The disconnect <c>reason</c> is recorded as <c>"normal"</c> when <paramref name="exception"/> is <see langword="null"/>,
    /// otherwise as <c>"error"</c>.
    /// </para>
    /// <para>
    /// If the connection start timestamp was stored under the <c>"__nm_conn_started"</c> item key,
    /// the total lifetime is measured and recorded via <c>ObserveConnectionDuration</c>.
    /// </para>
    /// </remarks>
    public async ValueTask OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, ValueTask> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var hub = context.Hub.GetType().Name;
        var transport = context.Context?
            .Features?
            .Get<Microsoft.AspNetCore.Http.Connections.Features.IHttpTransportFeature>()?
            .TransportType
            .ToString();

        var reason = exception is null ? "normal" : "error";
        var started = context.Context?.Items.TryGetValue("__nm_conn_started", out var ts) == true
            ? (DateTimeOffset)ts!
            : DateTimeOffset.MinValue;

        await next(context, exception).ConfigureAwait(false);

        _metrics.DecConnection(hub, transport ?? "unknown", reason);

        if (started != DateTimeOffset.MinValue)
        {
            _metrics.ObserveConnectionDuration(
                hub,
                transport ?? "unknown",
                reason,
                DateTimeOffset.UtcNow - started);
        }
    }

    // --- Helper: Single point for JSON size calculation with trimming/AOT suppressions ---

    /// <summary>
    /// Serializes the provided value to UTF-8 JSON and returns the resulting byte array.
    /// Used exclusively for computing payload size; the JSON output is never materialized as text.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>A UTF-8 encoded JSON byte array.</returns>
    /// <remarks>
    /// This method is annotated to suppress trimming and AOT warnings because it is only used to measure payload size.
    /// For NativeAOT scenarios, consider using a <see cref="JsonSerializerOptions"/> with source-generated metadata.
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown when the runtime type cannot be serialized by <see cref="JsonSerializer"/>.</exception>
    /// <exception cref="JsonException">Thrown when an error occurs during serialization.</exception>
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Payload size measurement only; type preservation is not required.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Payload size measurement only; prefer JSON source generation for NativeAOT.")]
    private static byte[] SafeSerializeToUtf8Bytes(object? value)
        => JsonSerializer.SerializeToUtf8Bytes(value);
}
