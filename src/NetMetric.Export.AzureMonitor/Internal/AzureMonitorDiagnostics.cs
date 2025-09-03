// <copyright file="AzureMonitorDiagnostics.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.AzureMonitor.Internal;

/// <summary>
/// Provides lightweight, thread-safe counters and gauges that describe the health of the
/// Azure Monitor export pipeline (enqueue drops, send totals, failures, retries, batch size,
/// last send duration, and current queue length).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AzureMonitorDiagnostics"/> is intentionally simple and non-blocking. All updates
/// use atomic operations (<see cref="Interlocked"/> and <see cref="Volatile"/>), which makes it
/// suitable for high-throughput telemetry paths (hot paths).
/// </para>
/// <para>
/// This type does not emit telemetry by itself. It is designed to be polled or exported by
/// a higher-level diagnostic component (for example, an internal self-metrics exporter) at a
/// cadence appropriate for your application.
/// </para>
/// <para>
/// Typical producers:
/// <list type="bullet">
///   <item>
///     <description>Enqueue logic increments <see cref="IncDropped"/> on back-pressure drop.</description>
///   </item>
///   <item>
///     <description>Sender logic calls <see cref="AddSent(long)"/> after a successful batch send.</description>
///   </item>
///   <item>
///     <description>Retry policy calls <see cref="IncRetries"/> per retry attempt.</description>
///   </item>
///   <item>
///     <description>Terminal failure path calls <see cref="IncFailure"/> once per failed batch.</description>
///   </item>
///   <item>
///     <description>Batch bookkeeping updates <see cref="SetLastBatchSize(int)"/> and
///     <see cref="SetLastSendDurationMs(long)"/>.</description>
///   </item>
///   <item>
///     <description>Queue owner updates <see cref="SetQueueLength(int)"/> after enqueue/dequeue.</description>
///   </item>
/// </list>
/// </para>
/// </remarks>
/// <threadsafety>
/// All members are thread-safe. Counters and gauges are maintained using atomic operations.
/// </threadsafety>
/// <example>
/// The following example demonstrates typical usage within a sender:
/// <code language="csharp"><![CDATA[
/// // Construct once and share across producer/consumer components.
/// // var diag = new AzureMonitorDiagnostics();
///
/// try
/// {
///     // Before sending a batch:
///     diag.SetLastBatchSize(batch.Count);
/// 
///     var sw = Stopwatch.StartNew();
///     await client.SendAsync(batch, cancellationToken).ConfigureAwait(false);
///     sw.Stop();
/// 
///     diag.AddSent(batch.Count);
///     diag.SetLastSendDurationMs(sw.ElapsedMilliseconds);
/// }
/// catch (TransientException)
/// {
///     diag.IncRetries();
///     throw;
/// }
/// catch (Exception)
/// {
///     // A terminal failure for this batch:
///     diag.IncFailure();
///     throw;
/// }
/// // Elsewhere in the enqueue path:
/// if (!channel.TryWrite(item)) { diag.IncDropped(); }
///
/// int observedQueueLength = queue.Count;
/// diag.SetQueueLength(observedQueueLength);
/// ]]></code>
/// </example>
internal sealed class AzureMonitorDiagnostics
{
    private long _dropped;             // Number of dropped metrics during enqueue
    private long _sent;                // Number of successfully sent telemetry items
    private long _sendFailures;        // Number of batch failures (after max retry attempts)
    private long _retries;             // Total retry attempts
    private long _lastBatchSize;       // Size of the last batch sent
    private long _lastSendDurationMs;  // Duration of the last send operation in milliseconds
    private int _queueLength;          // Current length of the queue

    /// <summary>
    /// Atomically increments the count of dropped telemetry items by one.
    /// </summary>
    /// <remarks>
    /// Call this when enqueueing into the channel fails due to back pressure or capacity limits.
    /// </remarks>
    public void IncDropped() => Interlocked.Increment(ref _dropped);

    /// <summary>
    /// Atomically adds <paramref name="n"/> to the total number of successfully sent telemetry items.
    /// </summary>
    /// <param name="n">The number of items that were just sent successfully; must be non-negative.</param>
    /// <remarks>
    /// Invoke once per successful batch send with the batch size. This method is additive and
    /// does not reset existing counts.
    /// </remarks>
    public void AddSent(long n) => Interlocked.Add(ref _sent, n);

    /// <summary>
    /// Atomically increments the count of terminal send failures by one.
    /// </summary>
    /// <remarks>
    /// Use this when a batch definitively fails after all retry attempts have been exhausted.
    /// </remarks>
    public void IncFailure() => Interlocked.Increment(ref _sendFailures);

    /// <summary>
    /// Atomically increments the total retry attempts by one.
    /// </summary>
    /// <remarks>
    /// Call this per retry attempt (not per batch) so the counter reflects total retry operations performed.
    /// </remarks>
    public void IncRetries() => Interlocked.Increment(ref _retries);

    /// <summary>
    /// Atomically sets the size of the most recently attempted or completed batch.
    /// </summary>
    /// <param name="n">The number of telemetry items included in the last batch.</param>
    /// <remarks>
    /// This is a gauge (overwritten per batch), not a cumulative counter.
    /// </remarks>
    public void SetLastBatchSize(int n) => Interlocked.Exchange(ref _lastBatchSize, n);

    /// <summary>
    /// Atomically sets the duration, in milliseconds, of the most recent send operation.
    /// </summary>
    /// <param name="ms">The elapsed time, in milliseconds, for the last send call.</param>
    /// <remarks>
    /// This is a gauge (overwritten per send), not a cumulative counter.
    /// </remarks>
    public void SetLastSendDurationMs(long ms) => Interlocked.Exchange(ref _lastSendDurationMs, ms);

    /// <summary>
    /// Atomically sets the current observed length of the queue.
    /// </summary>
    /// <param name="n">The current number of items in the queue.</param>
    /// <remarks>
    /// Prefer updating this after enqueue/dequeue operations or on a periodic sampling interval.
    /// </remarks>
    public void SetQueueLength(int n) => Interlocked.Exchange(ref _queueLength, n);

    /// <summary>
    /// Gets the total number of telemetry items that were dropped (e.g., due to back pressure).
    /// </summary>
    /// <value>The cumulative count of dropped items.</value>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Gets the cumulative number of telemetry items successfully sent.
    /// </summary>
    /// <value>The cumulative count of sent items.</value>
    public long Sent => Interlocked.Read(ref _sent);

    /// <summary>
    /// Gets the cumulative number of terminal batch send failures (after retries were exhausted).
    /// </summary>
    /// <value>The cumulative count of failed batches.</value>
    public long SendFailures => Interlocked.Read(ref _sendFailures);

    /// <summary>
    /// Gets the cumulative number of retry attempts made by the sender.
    /// </summary>
    /// <value>The cumulative count of retries.</value>
    public long Retries => Interlocked.Read(ref _retries);

    /// <summary>
    /// Gets the number of telemetry items included in the most recently attempted or completed batch.
    /// </summary>
    /// <value>The size of the last batch.</value>
    public long LastBatchSize => Interlocked.Read(ref _lastBatchSize);

    /// <summary>
    /// Gets the duration, in milliseconds, of the most recent send operation.
    /// </summary>
    /// <value>The elapsed time of the last send, in milliseconds.</value>
    public long LastSendDurationMs => Interlocked.Read(ref _lastSendDurationMs);

    /// <summary>
    /// Gets the latest observed length of the queue.
    /// </summary>
    /// <value>The current queue length as last updated by <see cref="SetQueueLength(int)"/>.</value>
    public int QueueLength => Volatile.Read(ref _queueLength);
}
