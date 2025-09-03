// <copyright file="IisMetricsHostedService.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Hosting;

namespace NetMetric.IIS.Hosting;

/// <summary>
/// Background hosted service that initializes and manages the IIS metrics event listener.
/// </summary>
/// <remarks>
/// <para>
/// This service conditionally activates IIS metrics collection only when running on Windows and
/// when not explicitly disabled by the <c>NETMETRIC_IIS_ENABLED</c> environment variable.
/// On non-Windows platforms the service is a no-op.
/// </para>
/// <para>
/// Environment gating:
/// <list type="bullet">
///   <item><description>
///     The listener starts only if <see cref="OperatingSystem.IsWindows()"/> returns <see langword="true"/>.
///   </description></item>
///   <item><description>
///     If the <c>NETMETRIC_IIS_ENABLED</c> environment variable is present with a value of
///     <c>"0"</c>, <c>"false"</c>, or <c>"False"</c> (case-sensitive as shown), the listener does not start.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// Lifecycle:
/// <list type="bullet">
///   <item><description><see cref="StartAsync(System.Threading.CancellationToken)"/> creates an <see cref="IisEventListener"/> when permitted.</description></item>
///   <item><description><see cref="StopAsync(System.Threading.CancellationToken)"/> disposes the listener (if any) and clears references.</description></item>
///   <item><description><see cref="Dispose()"/> is idempotent and ensures resources are released.</description></item>
/// </list>
/// </para>
/// <para>
/// Thread safety: The hosted service methods are invoked by the generic host in a synchronized manner.
/// The underlying <see cref="IisEventListener"/> is designed to be thread-safe with respect to event callbacks.
/// </para>
/// </remarks>
/// <example>
/// <para>
/// Registering IIS metrics collection in an ASP.NET Core app:
/// </para>
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using Microsoft.Extensions.Hosting;
/// using NetMetric.IIS.Extensions;
///
/// var builder = Host.CreateApplicationBuilder(args);
///
/// // Registers the hosted service internally (see ServiceCollectionExtensions in NetMetric.IIS).
/// builder.Services.AddNetMetricIis(enabledByDefault: true);
///
/// var app = builder.Build();
/// await app.RunAsync();
/// ]]></code>
/// <para>
/// Disabling IIS metrics via environment variable (e.g., in PowerShell):
/// </para>
/// <code language="powershell"><![CDATA[
/// $env:NETMETRIC_IIS_ENABLED = "0"
/// dotnet run
/// ]]></code>
/// </example>
internal sealed class IisMetricsHostedService : IHostedService, IDisposable
{
    private readonly IisMetricSet _set;
    private IisEventListener? _listener;

    /// <summary>
    /// Initializes a new instance of the <see cref="IisMetricsHostedService"/> class.
    /// </summary>
    /// <param name="set">The IIS metric set used to record counters and observations.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="set"/> is <see langword="null"/>.
    /// </exception>
    public IisMetricsHostedService(IisMetricSet set)
    {
        _set = set ?? throw new ArgumentNullException(nameof(set));
    }

    /// <summary>
    /// Starts the IIS metrics listener if the environment and platform allow it.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that is not used in this implementation.</param>
    /// <returns>A completed task once startup logic has finished.</returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Skips activation on non-Windows platforms.</item>
    /// <item>Respects <c>NETMETRIC_IIS_ENABLED</c> (values <c>"0"</c>, <c>"false"</c>, <c>"False"</c> disable).</item>
    /// <item>Creates an <see cref="IisEventListener"/> instance on success.</item>
    /// </list>
    /// </remarks>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        var env = Environment.GetEnvironmentVariable("NETMETRIC_IIS_ENABLED");
        if (env is "0" or "false" or "False")
        {
            return Task.CompletedTask;
        }

        _listener = new IisEventListener(_set);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the IIS metrics listener and releases associated resources.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that is not used in this implementation.</param>
    /// <returns>A completed task once teardown logic has finished.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Dispose();
        _listener = null;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes the hosted service, ensuring the IIS event listener is released.
    /// Multiple calls are safe and have no additional effect.
    /// </summary>
    public void Dispose()
    {
        _listener?.Dispose();
        _listener = null;
    }
}
