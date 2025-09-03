// <copyright file="CloudWatchBufferedExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AWS.Exporters;

/// <summary>
/// A non-blocking metric exporter that writes metrics into a bounded
/// <see cref="global::System.Threading.Channels.Channel{T}"/> buffer.  
/// The actual upload to Amazon CloudWatch is performed asynchronously by a background flush service,
/// allowing application threads to proceed without incurring network I/O latency.
/// </summary>
/// <remarks>
/// <para>
/// <b>Behavior</b><br/>
/// When <see cref="CloudWatchExporterOptions.EnableBuffering"/> is <see langword="true"/>, calls to
/// <see cref="ExportAsync(System.Collections.Generic.IEnumerable{IMetric}, System.Threading.CancellationToken)"/>
/// enqueue items into an in-memory channel. A single background reader (see <c>CloudWatchFlushService</c>)
/// drains the channel at a configurable cadence and forwards batches to <c>CloudWatchMetricExporter</c>.
/// If buffering is disabled, this exporter becomes a no-op; callers should resolve and use
/// <c>CloudWatchMetricExporter</c> directly instead.
/// </para>
/// <para>
/// <b>Backpressure &amp; loss policy</b><br/>
/// The underlying channel is bounded. When full, the writer uses
/// <see cref="global::System.Threading.Channels.BoundedChannelFullMode.DropOldest"/> to shed load by
/// discarding the oldest enqueued metrics to make room for new items. This protects the process from
/// unbounded memory growth at the cost of potential data loss during sustained spikes.
/// </para>
/// <para>
/// <b>Thread safety</b><br/>
/// Multiple concurrent writers are supported (see System.Threading.Channels.BoundedChannelOptions.SingleWriter is <see langword="false"/>),
/// and a single reader is assumed by design (see System.Threading.Channels.BoundedChannelOptions.SingleReader is <see langword="true"/>).
/// Instances are intended to be registered as singletons in DI.
/// </para>
/// <para>
/// <b>Disposal</b><br/>
/// Disposing the exporter completes the channel writer to signal graceful shutdown to the background
/// flush service. Remaining items are drained by the service if possible.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// // Configure options
/// var opts = new CloudWatchExporterOptions
/// {
///     Namespace = "MyCompany.MyService",
///     EnableBuffering = true,
///     BufferCapacity = 20_000,
///     FlushIntervalMs = 1000
/// };
///
/// // Create the buffered exporter (registered as a singleton in DI in typical setups)
/// var buffered = new CloudWatchBufferedExporter(opts);
///
/// // Application code produces metrics without blocking on CloudWatch I/O:
/// await buffered.ExportAsync(new IMetric[] { myCounter, myGauge }, CancellationToken.None);
///
/// // A hosted service (CloudWatchFlushService) reads buffered.Reader and forwards to CloudWatchMetricExporter.
/// </code>
/// </example>
/// <seealso cref="CloudWatchMetricExporter"/>
/// <seealso cref="CloudWatchExporterOptions"/>
/// <seealso cref="NetMetric.AWS.Hosting.CloudWatchFlushService"/>
public sealed class CloudWatchBufferedExporter : IMetricExporter, IDisposable
{
    private readonly Channel<IMetric> _channel;
    private readonly CloudWatchExporterOptions _opts;

    /// <summary>
    /// Gets the channel reader that consumes metrics written to the buffer.  
    /// This is intended exclusively for the internal background flush service.
    /// </summary>
    internal ChannelReader<IMetric> Reader => _channel.Reader;

    /// <summary>
    /// Gets the configured exporter options for this instance.
    /// </summary>
    internal CloudWatchExporterOptions Options => _opts;

    /// <summary>
    /// Initializes a new instance of the <see cref="CloudWatchBufferedExporter"/> class.
    /// </summary>
    /// <param name="opts">The exporter options that control buffering behavior (capacity, flush policy, etc.).</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="opts"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The channel capacity is bounded to at least 1000 items regardless of <see cref="CloudWatchExporterOptions.BufferCapacity"/>,
    /// and the full mode is configured to <see cref="BoundedChannelFullMode.DropOldest"/> to prevent unbounded memory usage.
    /// </remarks>
    public CloudWatchBufferedExporter(CloudWatchExporterOptions opts)
    {
        _opts = opts ?? throw new ArgumentNullException(nameof(opts));
        var capacity = Math.Max(1000, _opts.BufferCapacity);

        var options = new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        };

        _channel = Channel.CreateBounded<IMetric>(options);
    }

    /// <summary>
    /// Enqueues metrics for export by writing them into the bounded channel.  
    /// The actual export to CloudWatch is performed asynchronously by a background service.
    /// </summary>
    /// <param name="metrics">The collection of metrics to export. Each item is enqueued individually.</param>
    /// <param name="ct">A cancellation token that aborts the enqueue loop before all items are written.</param>
    /// <returns>
    /// A completed <see cref="global::System.Threading.Tasks.Task"/> once enqueueing completes
    /// (or stops early if <paramref name="ct"/> is cancelled).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="metrics"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// If <see cref="CloudWatchExporterOptions.EnableBuffering"/> is <see langword="false"/>,
    /// this method is a no-op and returns immediately. In that mode, resolve and call
    /// <c>CloudWatchMetricExporter</c> directly to send metrics synchronously.
    /// </para>
    /// <para>
    /// When the channel is full, the writer uses a best-effort <see cref="ChannelWriter{T}.TryWrite(T)"/> to avoid blocking.
    /// Depending on load, some metrics can be dropped according to the channel's full-mode policy.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("ExportAsync may rely on members that can be trimmed in AOT/linking scenarios. Keep necessary members or disable trimming for exporters.")]
    public Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        if (!_opts.EnableBuffering)
        {
            // If buffering is disabled, this exporter does nothing.
            // The caller should use CloudWatchMetricExporter directly.
            return Task.CompletedTask;
        }

        foreach (var m in metrics)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            _channel.Writer.TryWrite(m);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes the exporter and marks the channel writer as complete,  
    /// signaling the background service to finish consuming any remaining metrics.
    /// </summary>
    /// <remarks>
    /// Disposal does not wait for the background service to flush; it only completes the writer.
    /// The hosted flush service is responsible for draining remaining items during shutdown.
    /// </remarks>
    public void Dispose()
    {
        _channel.Writer.TryComplete();
    }
}
