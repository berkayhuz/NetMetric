// <copyright file="AzureMonitorChannel.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.AzureMonitor.Internal;

/// <summary>
/// Represents a bounded, asynchronous queue of <see cref="TelemetryEnvelope"/> items backed by
/// <see cref="System.Threading.Channels.Channel"/> for producer/consumer scenarios.
/// The channel supports multiple concurrent writers and a single reader, exposes an approximate
/// length for observability, and reports queue metrics via <see cref="AzureMonitorDiagnostics"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Concurrency model.</b> Internally, the channel is created with
/// <see cref="System.Threading.Channels.BoundedChannelOptions"/> configured as
/// <c>SingleReader = true</c> and <c>SingleWriter = false</c>; i.e., many producers,
/// one consumer. The class is safe to use concurrently from multiple writer threads.
/// The consumer side must be single-reader.
/// </para>
/// <para>
/// <b>Capacity and backpressure.</b> The channel is bounded. When the queue is full, behavior is governed by the
/// selected <see cref="System.Threading.Channels.BoundedChannelFullMode"/>. <see cref="TryWrite"/> returns <see langword="false"/>
/// if an item cannot be enqueued due to capacity or completion, in which case a "dropped" diagnostic is recorded.
/// </para>
/// <para>
/// <b>Diagnostics.</b> The class maintains a fast, <i>approximate</i> item count which is updated on enqueue/dequeue
/// paths and surfaced via <see cref="ApproximateCount"/>. The value is also pushed to
/// <see cref="AzureMonitorDiagnostics"/> (queue length gauge). On failed writes, <see cref="AzureMonitorDiagnostics.IncDropped"/>
/// is called.
/// </para>
/// <para>
/// <b>Completion and disposal.</b> This type does not complete or close the underlying channel; lifecycle
/// is expected to be managed by the owning component. <see cref="DisposeAsync"/> is a no-op and returns a completed
/// task to support <see cref="System.IAsyncDisposable"/> usage in higher-level compositions.
/// </para>
/// <para>
/// <b>Performance.</b> Queue operations use lock-free primitives (see <see cref="System.Threading.Interlocked"/> and
/// <see cref="System.Threading.Volatile"/>) and <c>Try*</c> fast paths where possible.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Capacity of 10_000 items; drop newest writers when full.
/// var diag = new AzureMonitorDiagnostics(/* ... */);
/// var channel = new AzureMonitorChannel(
///     capacity: 10_000,
///     mode: System.Threading.Channels.BoundedChannelFullMode.DropWrite,
///     diag: diag);
///
/// // Producer(s)
/// bool Enqueue(TelemetryEnvelope env)
/// {
///     // If false, the item was dropped (queue full or completed).
///     return channel.TryWrite(in env);
/// }
///
/// // Single consumer (typically in a BackgroundService)
/// async Task ConsumeAsync(CancellationToken ct)
/// {
///     while (!ct.IsCancellationRequested)
///     {
///         TelemetryEnvelope env = await channel.ReadAsync(ct);
///         // Process the envelope...
///     }
/// }
///
/// // Opportunistic, non-blocking poll (e.g., in a tight loop or shutdown path)
/// if (channel.TryRead(out var next))
/// {
///     // Handle 'next'
/// }
///
/// int queued = channel.ApproximateCount; // For dashboards/metrics
/// ]]></code>
/// </example>
/// <seealso cref="System.Threading.Channels.Channel"/>
/// <seealso cref="System.Threading.Channels.BoundedChannelFullMode"/>
/// <seealso cref="AzureMonitorDiagnostics"/>
/// <seealso cref="TelemetryEnvelope"/>
internal sealed class AzureMonitorChannel : IAsyncDisposable
{
    private readonly System.Threading.Channels.Channel<TelemetryEnvelope> _channel;
    private readonly AzureMonitorDiagnostics _diag;
    private int _approxCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureMonitorChannel"/> class with the specified capacity,
    /// full-mode policy, and diagnostics sink.
    /// </summary>
    /// <param name="capacity">
    /// The maximum number of items the queue can hold. Must be a positive integer; values that are too small
    /// may cause excessive drops under bursty load, while overly large values increase memory usage and latency.
    /// </param>
    /// <param name="mode">
    /// The <see cref="System.Threading.Channels.BoundedChannelFullMode"/> that determines producer behavior when the queue is full
    /// (e.g., <see cref="System.Threading.Channels.BoundedChannelFullMode.DropWrite"/> to drop the newest write attempts).
    /// </param>
    /// <param name="diag">
    /// The diagnostics instance used to emit queue length and drop counters. Must not be <see langword="null"/>.
    /// </param>
    /// <remarks>
    /// The underlying channel is configured as <c>SingleReader = true</c> and <c>SingleWriter = false</c>.
    /// </remarks>
    public AzureMonitorChannel(int capacity, System.Threading.Channels.BoundedChannelFullMode mode, AzureMonitorDiagnostics diag)
    {
        _channel = System.Threading.Channels.Channel.CreateBounded<TelemetryEnvelope>(
            new System.Threading.Channels.BoundedChannelOptions(capacity)
            {
                FullMode = mode,
                SingleReader = true,
                SingleWriter = false
            });

        _diag = diag;
    }

    /// <summary>
    /// Attempts to enqueue a <see cref="TelemetryEnvelope"/> without blocking.
    /// </summary>
    /// <param name="env">The telemetry envelope to enqueue.</param>
    /// <returns>
    /// <see langword="true"/> if the envelope was enqueued; otherwise <see langword="false"/> if the queue is full
    /// (per <see cref="System.Threading.Channels.BoundedChannelFullMode"/>) or the channel has been completed/closed.
    /// </returns>
    /// <remarks>
    /// On success, the internal approximate count is incremented and exported to diagnostics.
    /// On failure, a "dropped" counter is incremented via diagnostics.
    /// </remarks>
    public bool TryWrite(in TelemetryEnvelope env)
    {
        if (_channel.Writer.TryWrite(env))
        {
            System.Threading.Interlocked.Increment(ref _approxCount);
            _diag.SetQueueLength(System.Threading.Volatile.Read(ref _approxCount));
            return true;
        }

        _diag.IncDropped();
        return false;
    }

    /// <summary>
    /// Attempts to dequeue a <see cref="TelemetryEnvelope"/> without blocking.
    /// </summary>
    /// <param name="env">
    /// When this method returns, contains the dequeued envelope if the call succeeded;
    /// otherwise, the default value of <see cref="TelemetryEnvelope"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if an envelope was dequeued; otherwise <see langword="false"/> if the queue is empty.
    /// </returns>
    /// <remarks>
    /// On success, the internal approximate count is decremented and exported to diagnostics.
    /// </remarks>
    public bool TryRead(out TelemetryEnvelope env)
    {
        if (_channel.Reader.TryRead(out env))
        {
            System.Threading.Interlocked.Decrement(ref _approxCount);
            _diag.SetQueueLength(System.Threading.Volatile.Read(ref _approxCount));
            return true;
        }

        env = default;
        return false;
    }

    /// <summary>
    /// Asynchronously waits for and dequeues the next <see cref="TelemetryEnvelope"/>.
    /// </summary>
    /// <param name="ct">A cancellation token to observe. If canceled, the operation throws.</param>
    /// <returns>
    /// A task that completes with the next available <see cref="TelemetryEnvelope"/>.
    /// </returns>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown if <paramref name="ct"/> is canceled before an item becomes available.
    /// </exception>
    /// <exception cref="System.Threading.Channels.ChannelClosedException">
    /// Thrown if the underlying channel is completed and no further items are available.
    /// </exception>
    /// <remarks>
    /// On successful dequeue, the internal approximate count is decremented and exported to diagnostics.
    /// </remarks>
    public async ValueTask<TelemetryEnvelope> ReadAsync(CancellationToken ct)
    {
        var e = await _channel.Reader.ReadAsync(ct).ConfigureAwait(false);

        System.Threading.Interlocked.Decrement(ref _approxCount);
        _diag.SetQueueLength(System.Threading.Volatile.Read(ref _approxCount));

        return e;
    }

    /// <summary>
    /// Gets the <i>approximate</i> number of items currently enqueued.
    /// </summary>
    /// <remarks>
    /// This value is maintained via atomic increments/decrements on enqueue/dequeue paths and may lag reality
    /// transiently under contention. It is intended for diagnostics and dashboards rather than strict flow control.
    /// </remarks>
    public int ApproximateCount => System.Threading.Volatile.Read(ref _approxCount);

    /// <summary>
    /// Asynchronously disposes the channel instance.
    /// </summary>
    /// <returns>
    /// A completed task. This implementation does not close the underlying channel and is provided to conform to
    /// <see cref="IAsyncDisposable"/> for consumers that compose disposable resources uniformly.
    /// </returns>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
