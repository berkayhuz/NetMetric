// <copyright file="KestrelMetricsHostedService.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Hosting;

namespace NetMetric.Kestrel.Hosting;

/// <summary>
/// Background hosted service that wires up a <see cref="KestrelEventListener"/> to
/// collect Kestrel server metrics for the lifetime of the application,
/// and resets internal state on shutdown.
/// </summary>
/// <remarks>
/// <para>
/// On <see cref="StartAsync(CancellationToken)"/>, a <see cref="KestrelEventListener"/> is created
/// and attached to Kestrel/TLS event sources. Observed events are reported into the
/// provided <see cref="KestrelMetricSet"/>.  
/// On <see cref="StopAsync(CancellationToken)"/>, the listener is disposed and
/// <see cref="KestrelMetricSet.Reset"/> is called to clear any active gauges and counters.
/// </para>
/// <para>
/// This type should be registered once as a singleton <see cref="IHostedService"/> in the
/// dependency injection container. Multiple calls to <see cref="StartAsync(CancellationToken)"/>
/// and <see cref="StopAsync(CancellationToken)"/> are tolerated and safe (idempotent).
/// </para>
/// <para>
/// Typical registration:
/// <code language="csharp"><![CDATA[
/// services.AddSingleton<KestrelMetricSet>();
/// services.AddHostedService<KestrelMetricsHostedService>();
/// ]]></code>
/// </para>
/// </remarks>
/// <example>
/// <para>
/// <strong>How metrics flow</strong>
/// </para>
/// <list type="bullet">
/// <item><description><c>Http2ConnectionStart</c> → <see cref="KestrelMetricSet.IncConnection"/></description></item>
/// <item><description><c>Http3StreamStop</c> → <see cref="KestrelMetricSet.H3StreamStop"/></description></item>
/// <item><description>TLS <c>HandshakeStop</c> → <see cref="KestrelMetricSet.ObserveTlsHandshake"/></description></item>
/// </list>
/// </example>
/// <seealso cref="KestrelEventListener"/>
/// <seealso cref="KestrelMetricSet"/>
internal sealed class KestrelMetricsHostedService : IHostedService, IDisposable
{
    private readonly KestrelMetricSet _set;
    private KestrelEventListener? _listener;

    /// <summary>
    /// Initializes a new instance of <see cref="KestrelMetricsHostedService"/>.
    /// </summary>
    /// <param name="set">The <see cref="KestrelMetricSet"/> used as the sink for observed metrics.</param>
    public KestrelMetricsHostedService(KestrelMetricSet set)
    {
        _set = set;
    }

    /// <summary>
    /// Starts the metrics listener for Kestrel by instantiating
    /// a <see cref="KestrelEventListener"/> bound to the current metric set.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation before start completes.</param>
    /// <returns>A completed task once startup work has finished.</returns>
    /// <remarks>
    /// This method is non-blocking. It performs no long-running work and returns synchronously.
    /// </remarks>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _listener = new KestrelEventListener(_set);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the metrics listener and resets the metric state to a clean baseline.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation before stop completes.</param>
    /// <returns>A completed task once resources are released.</returns>
    /// <remarks>
    /// <para>
    /// Disposes the underlying <see cref="KestrelEventListener"/>, clears the reference,
    /// and calls <see cref="KestrelMetricSet.Reset"/> to set all active gauges back to zero.
    /// </para>
    /// <para>
    /// Any exceptions during cleanup (such as <see cref="ObjectDisposedException"/> or
    /// <see cref="InvalidOperationException"/>) are caught and suppressed.
    /// </para>
    /// </remarks>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _listener?.Dispose();
            _listener = null;

            _set.Reset();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes the underlying listener if it is still active.
    /// </summary>
    /// <remarks>
    /// This method is a fallback for cleanup in case <see cref="StopAsync(CancellationToken)"/>
    /// was not invoked. It only disposes the listener and does not reset metrics.
    /// </remarks>
    public void Dispose() => _listener?.Dispose();
}
