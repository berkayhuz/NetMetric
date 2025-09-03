// <copyright file="AzureMonitorSender.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Collections.ObjectModel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NetMetric.Export.AzureMonitor.Hosted;

/// <summary>
/// Background worker that dequeues telemetry envelopes and sends them to Azure Monitor in timed, size-bounded batches.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AzureMonitorSender"/> reads <see cref="TelemetryEnvelope"/> items from an
/// <see cref="AzureMonitorChannel"/> and forwards them via <see cref="IAzureMonitorClient"/>.
/// It applies a simple batching strategy governed by <see cref="AzureMonitorExporterOptions"/>:
/// it accumulates items until either the configured flush interval elapses or the maximum batch size is reached,
/// then dispatches the batch. Transient failures are retried with backoff through <c>RetryPolicy</c>.
/// </para>
/// <para>
/// When <see cref="AzureMonitorExporterOptions.EnableSelfMetrics"/> is enabled, the service emits
/// self-observability metrics (queue length, last batch size, send duration, retry and failure counters)
/// back into the same <see cref="AzureMonitorChannel"/>. These are tagged with
/// <c>nm.self=1</c> and <c>nm.component=sender</c> for easy filtering in Azure Monitor.
/// Self-metrics emission is further controlled by the allow/block lists in
/// <see cref="AzureMonitorExporterOptions.SelfMetricsAllow"/> and
/// <see cref="AzureMonitorExporterOptions.SelfMetricsBlock"/>.
/// </para>
/// <para>
/// Shutdown behavior: on <see cref="StopAsync(System.Threading.CancellationToken)"/>, the service links the caller
/// token and applies <see cref="AzureMonitorExporterOptions.ShutdownDrainTimeout"/> to let in-flight work finish.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Program.cs / Startup.cs
/// services.AddSingleton<AzureMonitorChannel>();
/// services.AddSingleton<IAzureMonitorClient, AzureMonitorClient>();
/// services.AddSingleton<AzureMonitorDiagnostics>();
/// services.Configure<AzureMonitorExporterOptions>(o =>
/// {
///     o.MaxBatchSize = 500;
///     o.FlushInterval = TimeSpan.FromSeconds(2);
///     o.EmptyQueueDelay = TimeSpan.FromMilliseconds(250);
///     o.EnableSelfMetrics = true;
///     o.SelfMetricPrefix = "nm.sender.";
///     o.MaxRetryAttempts = 5;
///     o.BaseDelay = TimeSpan.FromMilliseconds(200);
///     o.MaxDelay = TimeSpan.FromSeconds(5);
///     o.ShutdownDrainTimeout = TimeSpan.FromSeconds(10);
/// });
/// services.AddHostedService<AzureMonitorSender>();
/// ]]></code>
/// </example>
/// <threadsafety>
/// The type is designed to be used as a singleton hosted service. It is not intended for direct concurrent access
/// from user code; concurrency is managed internally by the hosted service loop and the thread-safe queue.
/// </threadsafety>
/// <seealso cref="AzureMonitorExporterOptions"/>
/// <seealso cref="AzureMonitorChannel"/>
/// <seealso cref="IAzureMonitorClient"/>
/// <seealso cref="IAzureMonitorDirectClient"/>
/// <seealso cref="TelemetryEnvelope"/>
/// <seealso cref="MetricTelemetry"/>
internal sealed class AzureMonitorSender : BackgroundService
{
    private readonly IAzureMonitorClient _client;
    private readonly AzureMonitorChannel _queue;
    private readonly AzureMonitorExporterOptions _o;
    private readonly AzureMonitorDiagnostics _diag;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureMonitorSender"/> hosted service.
    /// </summary>
    /// <param name="client">Client used to send telemetry to Azure Monitor.</param>
    /// <param name="queue">In-process channel from which telemetry envelopes are dequeued.</param>
    /// <param name="diag">Diagnostics aggregator for queue/batch/send counters.</param>
    /// <param name="options">Exporter configuration; must not be <see langword="null"/> and must contain a valid value.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="client"/>, <paramref name="queue"/>, <paramref name="diag"/>, or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public AzureMonitorSender(
        IAzureMonitorClient client,
        AzureMonitorChannel queue,
        AzureMonitorDiagnostics diag,
        IOptions<AzureMonitorExporterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _client = client ?? throw new ArgumentNullException(nameof(client));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _diag = diag ?? throw new ArgumentNullException(nameof(diag));
        _o = options.Value;
    }

    /// <summary>
    /// Main worker loop: periodically drains the queue, forms a batch, and sends it to Azure Monitor with retries.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token signaled by the hosting infrastructure.</param>
    /// <returns>A task that completes when the service stops.</returns>
    /// <remarks>
    /// <para>
    /// Batching algorithm:
    /// </para>
    /// <list type="number">
    /// <item><description>Wait up to <see cref="AzureMonitorExporterOptions.FlushInterval"/> while filling the batch.</description></item>
    /// <item><description>If the queue is empty, wait <see cref="AzureMonitorExporterOptions.EmptyQueueDelay"/> and optionally emit self-metrics.</description></item>
    /// <item><description>On send, attempt to top-up to <see cref="AzureMonitorExporterOptions.MaxBatchSize"/> before dispatch.</description></item>
    /// </list>
    /// <para>
    /// On transient errors (HTTP/network timeouts, cancellations), increments diagnostics and retries according to
    /// <see cref="AzureMonitorExporterOptions.MaxRetryAttempts"/>, <see cref="AzureMonitorExporterOptions.BaseDelay"/>,
    /// and <see cref="AzureMonitorExporterOptions.MaxDelay"/>.
    /// Unexpected exceptions are rethrown to surface fatal conditions to the host.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="stoppingToken"/> is canceled.</exception>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new Collection<TelemetryEnvelope>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for the flush interval and accumulate metrics into the batch
                var delayTask = Task.Delay(_o.FlushInterval, stoppingToken);
                while (batch.Count < _o.MaxBatchSize && _queue.TryRead(out var item))
                {
                    batch.Add(item);
                }

                if (batch.Count == 0)
                {
                    await Task.Delay(_o.EmptyQueueDelay, stoppingToken).ConfigureAwait(false);

                    if (_o.EnableSelfMetrics)
                    {
                        EmitSelfMetrics();
                    }

                    continue;
                }

                await delayTask.ConfigureAwait(false);

                // Attempt to fill the batch up to the max size
                while (batch.Count < _o.MaxBatchSize && _queue.TryRead(out var more))
                {
                    batch.Add(more);
                }

                var started = Environment.TickCount64;

                await SendBatchAsync(batch, stoppingToken).ConfigureAwait(false);

                var elapsedMs = unchecked(Environment.TickCount64 - started);

                _diag.SetLastBatchSize(batch.Count);
                _diag.SetLastSendDurationMs((long)elapsedMs);
                _diag.AddSent(batch.Count);

                if (_o.EnableSelfMetrics)
                {
                    EmitSelfMetrics();
                }
            }
            catch (HttpRequestException)
            {
                _diag.IncFailure();
                // Optionally log
            }
            catch (TaskCanceledException)
            {
                _diag.IncFailure();
                // Optionally log
            }
            catch (TimeoutException)
            {
                _diag.IncFailure();
                // Optionally log
            }
            catch (Exception)
            {
                _diag.IncFailure();
                // Bubble up unexpected exceptions; the host will handle lifecycle
                throw;
            }
            finally
            {
                batch.Clear();
            }
        }
    }

    /// <summary>
    /// Sends a non-empty batch to Azure Monitor, retrying transient failures according to the configured policy.
    /// </summary>
    /// <param name="batch">The batch to send; must not be <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token for the send operation.</param>
    /// <returns>A task that completes when the batch has been sent (or skipped if empty).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="batch"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">
    /// Thrown if the injected <see cref="IAzureMonitorClient"/> does not implement <see cref="IAzureMonitorDirectClient"/>.
    /// </exception>
    /// <remarks>
    /// Each <see cref="TelemetryEnvelope"/> is forwarded via <see cref="IAzureMonitorDirectClient.TrackMetricTelemetryAsync(MetricTelemetry,System.Threading.CancellationToken)"/>
    /// followed by <see cref="IAzureMonitorClient.FlushAsync(System.Threading.CancellationToken)"/>.
    /// </remarks>
    private Task SendBatchAsync(Collection<TelemetryEnvelope> batch, CancellationToken ct)
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

                foreach (var env in batch)
                {
                    await direct.TrackMetricTelemetryAsync(env.Telemetry, t).ConfigureAwait(false);
                }

                await _client.FlushAsync(t).ConfigureAwait(false);
            },
            _o.MaxRetryAttempts,
            _o.BaseDelay,
            _o.MaxDelay,
            IsTransient,
            ct,
            onRetry: (_, __) => _diag.IncRetries());
    }

    /// <summary>
    /// Determines whether an exception is considered transient for retry purposes.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns><see langword="true"/> for <see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/>, or <see cref="TimeoutException"/>; otherwise <see langword="false"/>.</returns>
    private static bool IsTransient(Exception ex)
    {
        return ex is HttpRequestException or TaskCanceledException or TimeoutException;
    }

    /// <summary>
    /// Requests graceful shutdown and applies a drain timeout as configured.
    /// </summary>
    /// <param name="cancellationToken">Caller token used to signal shutdown.</param>
    /// <returns>A task that completes when the service has stopped.</returns>
    /// <remarks>
    /// Links <paramref name="cancellationToken"/> with an internal CTS and calls <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>
    /// using <see cref="AzureMonitorExporterOptions.ShutdownDrainTimeout"/> to bound drain time.
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        cts.CancelAfter(_o.ShutdownDrainTimeout);

        await base.StopAsync(cts.Token).ConfigureAwait(false);
    }

    // -------------------- SELF METRICS --------------------

    /// <summary>
    /// Emits diagnostic self-metrics back into the queue when enabled.
    /// </summary>
    /// <remarks>
    /// Emitted metrics (names include <see cref="AzureMonitorExporterOptions.SelfMetricPrefix"/>):
    /// <list type="bullet">
    /// <item><description><c>queue.length</c> (<c>unit=items</c>)</description></item>
    /// <item><description><c>batch.size.last</c> (<c>unit=items</c>)</description></item>
    /// <item><description><c>send.duration.ms.last</c> (<c>unit=ms</c>)</description></item>
    /// <item><description><c>send.retries.total</c> (<c>unit=count</c>)</description></item>
    /// <item><description><c>send.failures.total</c> (<c>unit=count</c>)</description></item>
    /// <item><description><c>enqueue.dropped.total</c> (<c>unit=count</c>)</description></item>
    /// </list>
    /// Each metric is tagged with <c>nm.self=1</c> and <c>nm.component=sender</c>.
    /// </remarks>
    private void EmitSelfMetrics()
    {
        var p = _o.SelfMetricPrefix;

        TryEnqueueSelf($"{p}queue.length", _diag.QueueLength, unit: "items");
        TryEnqueueSelf($"{p}batch.size.last", _diag.LastBatchSize, unit: "items");
        TryEnqueueSelf($"{p}send.duration.ms.last", _diag.LastSendDurationMs, unit: "ms");
        TryEnqueueSelf($"{p}send.retries.total", _diag.Retries, unit: "count");
        TryEnqueueSelf($"{p}send.failures.total", _diag.SendFailures, unit: "count");
        TryEnqueueSelf($"{p}enqueue.dropped.total", _diag.Dropped, unit: "count");
    }

    /// <summary>
    /// Adds a single self-metric to the queue if allowed by the current filters.
    /// </summary>
    /// <param name="fullName">Fully-qualified metric name, including <see cref="AzureMonitorExporterOptions.SelfMetricPrefix"/>.</param>
    /// <param name="value">Metric value.</param>
    /// <param name="unit">Optional unit (stored in <see cref="MetricTelemetry.Properties"/>).</param>
    /// <remarks>
    /// Name filtering is evaluated against the name <em>without</em> the configured prefix.
    /// </remarks>
    private void TryEnqueueSelf(string fullName, long value, string? unit = null)
    {
        ArgumentNullException.ThrowIfNull(fullName);

        // Allow/block filters apply to the names without the prefix
        var nameWithoutPrefix = fullName.StartsWith(_o.SelfMetricPrefix, StringComparison.Ordinal)
            ? fullName.Substring(_o.SelfMetricPrefix.Length)
            : fullName;

        if (!ShouldEmitSelf(nameWithoutPrefix))
        {
            return;
        }

        var mt = new MetricTelemetry(fullName, value);

        if (!string.IsNullOrEmpty(unit))
        {
            mt.Properties["unit"] = unit;
        }

        // self-metrics are distinguished by tags
        mt.Properties["nm.self"] = "1";
        mt.Properties["nm.component"] = "sender";

        _queue.TryWrite(new TelemetryEnvelope(mt));
    }

    /// <summary>
    /// Evaluates allow/block lists to decide if a self-metric should be emitted.
    /// </summary>
    /// <param name="nameWithoutPrefix">Metric name without the configured prefix.</param>
    /// <returns><see langword="true"/> if the metric passes filtering; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// If an allow list is present and non-empty, only exact matches are emitted (subject to the block list).
    /// If the allow list is empty or <see langword="null"/>, the block list alone is applied.
    /// </remarks>
    private bool ShouldEmitSelf(string nameWithoutPrefix)
    {
        // Only allow metrics in the allow list
        if (_o.SelfMetricsAllow is { Count: > 0 })
        {
            foreach (var s in _o.SelfMetricsAllow)
            {
                if (string.Equals(s, nameWithoutPrefix, StringComparison.Ordinal))
                {
                    return !IsBlocked(nameWithoutPrefix);
                }
            }
            return false; // allow list exists but no match
        }

        // If no allow list is provided, only check the block list
        return !IsBlocked(nameWithoutPrefix);
    }

    /// <summary>
    /// Returns whether a self-metric is blocked by the block list.
    /// </summary>
    /// <param name="nameWithoutPrefix">Metric name without the configured prefix.</param>
    /// <returns><see langword="true"/> if blocked; otherwise <see langword="false"/>.</returns>
    private bool IsBlocked(string nameWithoutPrefix)
    {
        if (_o.SelfMetricsBlock is { Count: > 0 })
        {
            foreach (var s in _o.SelfMetricsBlock)
            {
                if (string.Equals(s, nameWithoutPrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
