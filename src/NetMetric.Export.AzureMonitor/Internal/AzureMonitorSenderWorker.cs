// <copyright file="AzureMonitorSenderWorker.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Collections.ObjectModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetMetric.Export.AzureMonitor.Internal;

/// <summary>
/// Background worker that reads telemetry envelopes from an internal bounded queue and
/// sends them to Azure Monitor in batches.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AzureMonitorSenderWorker"/> is designed to run as a hosted background service and:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>Collects telemetry envelopes from <see cref="AzureMonitorChannel"/>.</description>
///   </item>
///   <item>
///     <description>Batches items up to <see cref="AzureMonitorExporterOptions.MaxBatchSize"/>.</description>
///   </item>
///   <item>
///     <description>Sends each batch via <see cref="IAzureMonitorClient"/> with an exponential backoff retry policy.</description>
///   </item>
///   <item>
///     <description>Flushes the client after each successful batch to minimize telemetry latency.</description>
///   </item>
/// </list>
/// <para>
/// This worker does not perform its own scheduling beyond a small delay when the queue is empty
/// (<see cref="AzureMonitorExporterOptions.EmptyQueueDelay"/>). Throughput, resilience, and backpressure are
/// governed by the queue capacity, the batch size, and the retry policy parameters in
/// <see cref="AzureMonitorExporterOptions"/>.
/// </para>
/// <para>
/// Threading model: the worker runs a single consumer loop (one instance) and is <em>not</em> thread-safe for
/// concurrent <see cref="ExecuteAsync(CancellationToken)"/> invocations. The underlying channel provides the necessary
/// concurrency guarantees between producers and this consumer.
/// </para>
/// </remarks>
/// <example>
/// <para>Registering the worker in a typical .NET host:</para>
/// <code language="csharp"><![CDATA[
/// builder.Services.Configure<AzureMonitorExporterOptions>(builder.Configuration.GetSection("AzureMonitor"));
/// builder.Services.AddSingleton<AzureMonitorChannel>();
/// builder.Services.AddSingleton<IAzureMonitorClient, AzureMonitorClient>();
/// builder.Services.AddHostedService<AzureMonitorSenderWorker>();
/// ]]></code>
/// <para>Minimal configuration (appsettings.json):</para>
/// <code language="json"><![CDATA[
/// {
///   "AzureMonitor": {
///     "MaxBatchSize": 512,
///     "MaxRetryAttempts": 5,
///     "BaseDelay": "00:00:01",
///     "MaxDelay": "00:00:30",
///     "EmptyQueueDelay": "00:00:02"
///   }
/// }
/// ]]></code>
/// </example>
/// <seealso cref="AzureMonitorChannel"/>
/// <seealso cref="AzureMonitorExporterOptions"/>
/// <seealso cref="IAzureMonitorClient"/>
internal sealed class AzureMonitorSenderWorker : BackgroundService
{
    private readonly AzureMonitorChannel _queue;
    private readonly AzureMonitorExporterOptions _o;
    private readonly IAzureMonitorClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureMonitorSenderWorker"/> class.
    /// </summary>
    /// <param name="queue">The bounded queue that supplies telemetry envelopes to be sent.</param>
    /// <param name="client">The Azure Monitor client used to transmit telemetry and perform flush operations.</param>
    /// <param name="options">The exporter options that control batching and retry behavior.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The <paramref name="client"/> is expected to be either an <see cref="IAzureMonitorDirectClient"/> or
    /// wrap a component that supports direct submission of <c>MetricTelemetry</c> instances. If the client
    /// does not support direct metric submission, sending a batch will result in
    /// <see cref="NotSupportedException"/>.
    /// </para>
    /// </remarks>
    public AzureMonitorSenderWorker(
        AzureMonitorChannel queue,
        IAzureMonitorClient client,
        IOptions<AzureMonitorExporterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options, nameof(options));

        _queue = queue;
        _client = client;
        _o = options.Value;
    }

    /// <summary>
    /// Primary worker loop that drains the queue, forms batches, and dispatches them to Azure Monitor.
    /// </summary>
    /// <param name="stoppingToken">A token that signals when the host is shutting down.</param>
    /// <returns>
    /// A task that completes when the worker is stopped by the hosting infrastructure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The loop attempts to read up to <see cref="AzureMonitorExporterOptions.MaxBatchSize"/> items per iteration.
    /// If no items are available, the worker delays for <see cref="AzureMonitorExporterOptions.EmptyQueueDelay"/>
    /// to avoid hot-spinning.
    /// </para>
    /// <para>
    /// Transient network issues are tolerated through the retry policy configured in
    /// <see cref="AzureMonitorExporterOptions"/>. Non-transient failures are rethrown to allow the host to
    /// observe the exception according to its configured behavior (e.g., restart policy).
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Propagated when the worker is asked to stop via <paramref name="stoppingToken"/>.
    /// </exception>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<TelemetryEnvelope>(_o.MaxBatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                batch.Clear();

                // Drain up to MaxBatchSize items from the queue.
                while (batch.Count < _o.MaxBatchSize && _queue.TryRead(out var item))
                    batch.Add(item);

                if (batch.Count > 0)
                {
                    await SendBatchAsync(batch.AsReadOnly(), stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    // Back off briefly when there is no data to process.
                    await Task.Delay(_o.EmptyQueueDelay, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Host-initiated shutdown; swallow and exit gracefully.
            }
            catch (HttpRequestException)
            {
                // Handled by the internal retry in SendBatchAsync; loop continues for next iteration.
            }
            catch (TimeoutException)
            {
                // Handled by the internal retry in SendBatchAsync; loop continues for next iteration.
            }
            catch (Exception)
            {
                // Unexpected error. Let the host decide how to handle worker failures.
                throw;
            }
        }
    }

    /// <summary>
    /// Sends a read-only batch of telemetry envelopes to Azure Monitor with retry semantics.
    /// </summary>
    /// <param name="batch">The read-only collection of telemetry envelopes to send.</param>
    /// <param name="ct">A cancellation token used to cancel the send operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// <para>
    /// Each telemetry envelope in <paramref name="batch"/> is submitted via
    /// <see cref="IAzureMonitorDirectClient.TrackMetricTelemetryAsync(Microsoft.ApplicationInsights.DataContracts.MetricTelemetry, CancellationToken)"/>.
    /// After all items are queued, the client is explicitly flushed using <see cref="IAzureMonitorClient.FlushAsync(CancellationToken)"/>
    /// to reduce end-to-end latency.
    /// </para>
    /// <para>
    /// The operation is wrapped by <see cref="RetryPolicy.RunAsync"/>.
    /// honoring <see cref="AzureMonitorExporterOptions.MaxRetryAttempts"/>, <see cref="AzureMonitorExporterOptions.BaseDelay"/>,
    /// and <see cref="AzureMonitorExporterOptions.MaxDelay"/>. Retries are attempted when the exception is
    /// <see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/>, or <see cref="TimeoutException"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="batch"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when the configured client does not implement <see cref="IAzureMonitorDirectClient"/> to support direct metric submission.
    /// </exception>
    private Task SendBatchAsync(ReadOnlyCollection<TelemetryEnvelope> batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RetryPolicy.RunAsync(
            async t =>
            {
                if (_client is not IAzureMonitorDirectClient direct)
                {
                    throw new NotSupportedException("Client does not support direct MetricTelemetry.");
                }

                for (int i = 0; i < batch.Count; i++)
                {
                    var env = batch[i];
                    await direct.TrackMetricTelemetryAsync(env.Telemetry, t).ConfigureAwait(false);
                }

                await _client.FlushAsync(t).ConfigureAwait(false);
            },
            _o.MaxRetryAttempts,
            _o.BaseDelay,
            _o.MaxDelay,
            ex => ex is HttpRequestException or TaskCanceledException or TimeoutException, ct);
    }
}
