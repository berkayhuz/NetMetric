// <copyright file="CloudWatchFlushService.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Amazon.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetMetric.AWS.Hosting;

/// <summary>
/// A hosted background service that continuously reads metrics from the
/// buffered exporter channel and periodically flushes them to Amazon CloudWatch.
/// </summary>
/// <remarks>
/// <para>
/// <b>How it works</b><br/>
/// This service polls the <see cref="CloudWatchBufferedExporter"/> channel for pending
/// <see cref="IMetric"/> instances, aggregates them into in-memory batches, and delegates
/// the actual upload to <see cref="CloudWatchMetricExporter"/>. Batching cadence and size
/// are controlled by <see cref="CloudWatchExporterOptions.FlushIntervalMs"/> and
/// <see cref="CloudWatchExporterOptions.MaxFlushBatch"/> (falling back to
/// <see cref="CloudWatchExporterOptions.MaxBatchSize"/> when <c>MaxFlushBatch</c> is not set).
/// </para>
/// <para>
/// <b>Shutdown behavior</b><br/>
/// During application shutdown (i.e., when
/// <see cref="IHostedService.StopAsync(System.Threading.CancellationToken)"/> is invoked),
/// the service performs a best-effort <i>graceful drain</i>: it empties the channel and flushes any
/// remaining metrics to minimize data loss. If CloudWatch returns transient errors, the service
/// applies a short delay and retries on the next loop iteration.
/// </para>
/// <para>
/// <b>Thread-safety &amp; performance</b><br/>
/// A single background reader consumes from the channel (<c>SingleReader = true</c>),
/// while multiple producers may write concurrently. The batching loop avoids busy-waiting by
/// leveraging a periodic delay and short back-off sleeps when the channel is temporarily empty.
/// </para>
/// <para>
/// <b>Logging</b><br/>
/// An optional <see cref="ILogger{TCategoryName}"/> can be supplied to record operational
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// // Program.cs (minimal hosting)
/// var builder = Host.CreateDefaultBuilder(args)
///     .ConfigureServices(services =>
///     {
///         // register options (CloudWatchExporterOptions)
///         services.Configure&lt;CloudWatchExporterOptions&gt;(o =>
///         {
///             o.Namespace = "MyCompany.MyService";
///             o.FlushIntervalMs = 1000;
///             o.MaxBatchSize = 20; // CloudWatch limit
///         });
///
///         // register exporter components
///         services.AddSingleton(new CloudWatchExporterOptions()); // or IOptions&lt;&gt;
///         services.AddSingleton&lt;CloudWatchMetricExporter&gt;();
///         services.AddSingleton&lt;CloudWatchBufferedExporter&gt;();
///
///         // register the background flush service
///         services.AddHostedService&lt;CloudWatchFlushService&gt;();
///     });
///
/// await builder.Build().RunAsync();
/// </code>
/// </example>
public sealed class CloudWatchFlushService : BackgroundService
{
    private readonly CloudWatchBufferedExporter _buffered;
    private readonly CloudWatchMetricExporter _inner;
    private readonly CloudWatchExporterOptions _opts;
    private readonly ILogger<CloudWatchFlushService>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CloudWatchFlushService"/> class.
    /// </summary>
    /// <param name="buffered">
    /// The buffered exporter that holds metrics in an in-memory channel prior to flush.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="inner">
    /// The underlying exporter that sends metric batches to Amazon CloudWatch.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="opts">
    /// The exporter options controlling flush cadence, batch sizes, buffering, and retry behavior.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="logger">
    /// Optional logger for operational diagnostics. If <see langword="null"/>, logging is disabled.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="buffered"/>, <paramref name="inner"/>, or <paramref name="opts"/> is <see langword="null"/>.
    /// </exception>
    public CloudWatchFlushService(
        CloudWatchBufferedExporter buffered,
        CloudWatchMetricExporter inner,
        CloudWatchExporterOptions opts,
        ILogger<CloudWatchFlushService>? logger = null)
    {
        _buffered = buffered ?? throw new ArgumentNullException(nameof(buffered));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _opts = opts ?? throw new ArgumentNullException(nameof(opts));
        _logger = logger;
    }

    /// <summary>
    /// Executes the background loop that reads from the channel and flushes metrics to CloudWatch.
    /// </summary>
    /// <param name="stoppingToken">A token that is signaled when the host is shutting down.</param>
    /// <remarks>
    /// <para>
    /// The loop awaits a periodic delay for cadence and opportunistically drains the channel in between.
    /// If <paramref name="stoppingToken"/> is signaled, the method exits the loop and performs a final
    /// best-effort drain and flush.
    /// </para>
    /// <para>
    /// Transient AWS errors (<see cref="AmazonServiceException"/> with retryable status codes) cause a short
    /// delay before continuing the loop, letting retries be handled by
    /// <see cref="CloudWatchMetricExporter"/> on the next batch send.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="stoppingToken"/> is canceled during delay operations.
    /// </exception>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Exporter (CloudWatchMetricExporter.ExportAsync) may rely on reflection. Either guard members with DynamicDependency in the exporter or disable trimming for that assembly.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _buffered.Reader;
        var period = TimeSpan.FromMilliseconds(Math.Max(250, _opts.FlushIntervalMs));
        var batch = new List<IMetric>(capacity: Math.Max(100, _opts.MaxBatchSize));
        var maxFlush = _opts.MaxFlushBatch > 0 ? _opts.MaxFlushBatch : _opts.MaxBatchSize;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delayTask = Task.Delay(period, stoppingToken);

                // Opportunistic drain loop until the period elapses.
                while (!delayTask.IsCompleted)
                {
                    if (reader.TryRead(out var metric))
                    {
                        batch.Add(metric);
                        if (batch.Count >= maxFlush) break;
                    }
                    else
                    {
                        // Short backoff to avoid busy-waiting when the channel is empty.
                        await Task.Delay(25, stoppingToken).ConfigureAwait(false);
                    }
                }

                if (batch.Count > 0)
                {
                    await ExportBatchAsync(batch.AsReadOnly(), stoppingToken).ConfigureAwait(false);
                    batch.Clear();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown path.
                break;
            }
            catch (AmazonServiceException)
            {
                await Task.Delay(500, stoppingToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                throw;
            }
        }

        // ---------- Graceful drain on shutdown ----------
        try
        {
            while (reader.TryRead(out var metric))
            {
                batch.Add(metric);
                if (batch.Count >= maxFlush)
                {
                    await ExportBatchAsync(batch.AsReadOnly(), CancellationToken.None).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await ExportBatchAsync(batch.AsReadOnly(), CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (AmazonServiceException)
        {
            throw;
        }
    }

    /// <summary>
    /// Centralizes the exporter invocation; accepts a <see cref="ReadOnlyCollection{T}"/> for API clarity.
    /// </summary>
    /// <param name="batch">A read-only batch of metrics to export.</param>
    /// <param name="token">A cancellation token to observe.</param>
    /// <returns>A <see cref="Task"/> that completes when export finishes.</returns>
    /// <remarks>
    /// If <see cref="CloudWatchMetricExporter.ExportAsync(System.Collections.Generic.IEnumerable{IMetric}, System.Threading.CancellationToken)"/>
    /// depends on reflection-accessed members under trimming/AOT, ensure they are preserved via
    /// <see cref="System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute"/> in the exporter assembly,
    /// or disable trimming for that assembly.
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Controlled call-site; required members are preserved or trimming is disabled for the exporter assembly.")]
    private Task ExportBatchAsync(ReadOnlyCollection<IMetric> batch, CancellationToken token) =>
        _inner.ExportAsync(batch, token);
}
