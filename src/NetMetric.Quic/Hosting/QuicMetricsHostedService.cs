// <copyright file="QuicMetricsHostedService.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NetMetric.Quic.Hosting;

/// <summary>
/// A lightweight <see cref="IHostedService"/> that wires up a QUIC <see cref="EventListener"/>
/// for best-effort metric collection and tears it down on shutdown.
/// </summary>
/// <remarks>
/// <para>
/// This hosted service is intentionally minimal: it does not start background loops or timers.
/// On <see cref="StartAsync(CancellationToken)"/> it creates a single <see cref="QuicEventListener"/>
/// instance configured with the provided <see cref="QuicOptions"/> and metric sink
/// (<see cref="QuicMetricSet"/>). On <see cref="StopAsync(CancellationToken)"/> it disposes the
/// listener and clears the reference.
/// </para>
/// <para>
/// The service is idempotent with respect to common host lifecycles: multiple start/stop transitions
/// will not throw; cancellation tokens are honored by immediately throwing
/// <see cref="OperationCanceledException"/> if already requested.
/// </para>
/// <para>
/// Threading model: the ASP.NET Core hosting infrastructure calls <see cref="StartAsync(CancellationToken)"/>
/// and <see cref="StopAsync(CancellationToken)"/> in a serialized fashion for a given service instance.
/// This type does not implement additional synchronization beyond disposing the underlying listener.
/// </para>
/// <para>
/// Typical usage is to register a singleton <see cref="QuicMetricSet"/> and then add this hosted service
/// so that QUIC EventCounters from providers like <c>MsQuic</c> / <c>System.Net.Quic</c> are mapped into
/// your metrics pipeline.
/// </para>
/// <example>
/// The following example shows how to enable QUIC metrics in a generic host:
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using Microsoft.Extensions.Hosting;
/// using NetMetric.Quic.Hosting;
/// using NetMetric.Quic.Internal;
///
/// var host = Host.CreateDefaultBuilder(args)
///     .ConfigureServices(services =>
///     {
///         // Register the metric set (singleton)
///         services.AddSingleton<QuicMetricSet>();
///
///         // Optionally customize options
///         services.Configure<QuicOptions>(opt =>
///         {
///             opt.SamplingIntervalSec = 1;          // EventCounter sampling period
///             opt.EnableFallback = true;            // Publish unknown counters as multi-gauges
///             opt.MaxFallbackSeries = 200;          // Cardinality guard
///         });
///
///         // Start the listener for the app lifetime
///         services.AddHostedService<QuicMetricsHostedService>();
///     })
///     .Build();
///
/// await host.RunAsync();
/// ]]></code>
/// </example>
/// <seealso cref="QuicEventListener"/>
/// <seealso cref="QuicMetricSet"/>
/// <seealso cref="QuicOptions"/>
/// </remarks>
internal sealed class QuicMetricsHostedService : IHostedService, IDisposable
{
    private readonly QuicMetricSet _set;
    private readonly QuicOptions _opt;

    private QuicEventListener? _listener;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuicMetricsHostedService"/> class.
    /// </summary>
    /// <param name="set">The metric sink that will be populated with QUIC metrics.</param>
    /// <param name="opt">The options monitor that provides <see cref="QuicOptions"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="opt"/> is <see langword="null"/>.
    /// </exception>
    public QuicMetricsHostedService(
         QuicMetricSet set,
         IOptions<QuicOptions> opt)
    {
        ArgumentNullException.ThrowIfNull(opt);
        _set = set;
        _opt = opt.Value;
    }

    /// <summary>
    /// Creates and activates the underlying <see cref="QuicEventListener"/>.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that signals the start operation should be aborted. If cancellation is already requested,
    /// the method throws <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// No background work is scheduled; the listener subscribes to QUIC EventSources and forwards
    /// mapped counters to <see cref="QuicMetricSet"/>.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/> is canceled.</exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _listener = new QuicEventListener(_set, _opt);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes the underlying <see cref="QuicEventListener"/> and clears internal state.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that signals the stop operation should be aborted. If cancellation is already requested,
    /// the method throws <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// This method is safe to call even if the service was never started; it will simply no-op.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/> is canceled.</exception>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _listener?.Dispose();
        _listener = null;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Releases resources held by this service by disposing the internal event listener (if any).
    /// </summary>
    /// <remarks>
    /// This method is idempotent and may be called by the host at shutdown or explicitly by user code.
    /// </remarks>
    public void Dispose() => _listener?.Dispose();
}
