// <copyright file="DiagnosticObserverHostedService.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Hosting;

namespace NetMetric.HttpClient.Diagnostics;

/// <summary>
/// Hosted service that owns the lifetime of an <see cref="HttpClientDiagnosticObserver"/> instance.
/// </summary>
/// <remarks>
/// <para>
/// This service does not run a background loop and performs no work on start. Its sole purpose is to
/// ensure the provided <see cref="HttpClientDiagnosticObserver"/> is disposed when the host shuts down,
/// which cleanly unsubscribes from <c>System.Diagnostics.DiagnosticSource</c> listeners and prevents
/// resource leaks.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> The hosting infrastructure serializes calls to
/// <see cref="StartAsync(System.Threading.CancellationToken)"/> and
/// <see cref="StopAsync(System.Threading.CancellationToken)"/>. The service exposes no mutable shared state.
/// </para>
/// <para>
/// <strong>Registration:</strong> This type is typically registered by NetMetric's DI extensions. If you register it
/// manually, ensure the observer is a singleton and the hosted service is added once.
/// </para>
/// </remarks>
/// <example>
/// The following example shows a minimal manual registration:
/// <code language="csharp"><![CDATA[
/// services.AddSingleton<HttpClientMetricSet>(sp => /* construct metric set */);
/// services.AddSingleton<HttpClientDiagnosticObserver>();
/// services.AddHostedService<DiagnosticObserverHostedService>();
/// ]]></code>
/// </example>
/// <seealso cref="HttpClientDiagnosticObserver"/>
/// <seealso cref="System.Diagnostics.DiagnosticListener"/>
internal sealed class DiagnosticObserverHostedService : IHostedService
{
    private readonly HttpClientDiagnosticObserver _observer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticObserverHostedService"/> class.
    /// </summary>
    /// <param name="observer">
    /// The <see cref="HttpClientDiagnosticObserver"/> whose lifetime will be managed by this service.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="observer"/> is <see langword="null"/>.
    /// </exception>
    public DiagnosticObserverHostedService(HttpClientDiagnosticObserver observer)
        => _observer = observer ?? throw new ArgumentNullException(nameof(observer));

    /// <summary>
    /// Starts the hosted service.
    /// </summary>
    /// <param name="cancellationToken">A token used to signal start cancellation. Not used.</param>
    /// <returns>
    /// A completed task. No work is required at startup because the observer subscribes when constructed.
    /// </returns>
    /// <remarks>
    /// This method is a no-op; it completes synchronously.
    /// </remarks>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Stops the hosted service and disposes the underlying <see cref="HttpClientDiagnosticObserver"/>.
    /// </summary>
    /// <param name="cancellationToken">A token used to signal stop cancellation. Not used.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// Disposing the observer unsubscribes from all diagnostic listeners and releases any resources.
    /// This method is idempotent with respect to the observer's disposal semantics.
    /// </remarks>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _observer.Dispose();
        return Task.CompletedTask;
    }
}
