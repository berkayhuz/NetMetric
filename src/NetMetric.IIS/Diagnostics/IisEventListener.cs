// <copyright file="IisEventListener.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.IIS.Diagnostics;

/// <summary>
/// An <see cref="EventListener"/> specialized for IIS in-process hosting that
/// enables a bounded set of IIS/ASP.NET Core Module providers and translates
/// their events into <see cref="IisMetricSet"/> updates.
/// </summary>
/// <remarks>
/// <para>
/// This listener subscribes only to the following event providers:
/// <list type="bullet">
///   <item><description><c>Microsoft-AspNetCore-Server-IIS</c></description></item>
///   <item><description><c>Microsoft-AspNetCore-Server-IISIntegration</c></description></item>
/// </list>
/// This bounded subscription keeps overhead predictable and avoids accidental
/// high-cardinality or noisy signals.
/// </para>
/// <para>
/// Each recognized event is mapped deterministically to a fast-path metric call
/// on <see cref="IisMetricSet"/>; unknown events are deliberately ignored.
/// The mapping currently includes:
/// <list type="bullet">
///   <item><description><c>ConnectionStart</c> → <see cref="IisMetricSet.ConnectionStart"/></description></item>
///   <item><description><c>ConnectionStop</c> → <see cref="IisMetricSet.ConnectionStop"/></description></item>
///   <item><description><c>RequestStart</c> → <see cref="IisMetricSet.Request"/></description></item>
///   <item><description><c>BadRequest</c> → <see cref="IisMetricSet.Error(string)"/> with <c>reason =</c> <see cref="IisTagKeys.Reason_BadRequest"/></description></item>
///   <item><description><c>Timeout</c>/<c>RequestTimeout</c> → <see cref="IisMetricSet.Error(string)"/> with <c>reason =</c> <see cref="IisTagKeys.Reason_Timeout"/></description></item>
///   <item><description><c>Reset</c>/<c>ConnectionReset</c> → <see cref="IisMetricSet.Error(string)"/> with <c>reason =</c> <see cref="IisTagKeys.Reason_Reset"/></description></item>
///   <item><description><c>ApplicationError</c> → <see cref="IisMetricSet.Error(string)"/> with <c>reason =</c> <see cref="IisTagKeys.Reason_AppError"/></description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Thread safety:</strong> The listener is safe to use concurrently. It guards
/// disposal with a volatile flag and uses an internal collection to track enabled
/// sources for deterministic cleanup.
/// </para>
/// <para>
/// <strong>Platform behavior:</strong> This type is typically created by
/// <c>IisMetricsHostedService</c> only on Windows; on non-Windows platforms the
/// hosted service becomes a no-op. See the package's hosting guidance for details.
/// </para>
/// </remarks>
/// <example>
/// The listener is commonly managed by a hosted service:
/// <code language="csharp"><![CDATA[
/// // In your DI setup:
/// services.AddHostedService<IisMetricsHostedService>();
/// // IisMetricsHostedService will instantiate IisEventListener with the shared IisMetricSet
/// // when running on Windows and when NETMETRIC_IIS_ENABLED is not set to "0"/"false".
/// ]]></code>
/// </example>
/// <seealso cref="IisMetricSet"/>
/// <seealso cref="IisTagKeys"/>
internal sealed class IisEventListener : EventListener
{
    private readonly IisMetricSet _set;
    private readonly ConcurrentBag<EventSource> _enabledSources = new();
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IisEventListener"/> class.
    /// </summary>
    /// <param name="set">The IIS metric set used to record observed metrics.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="set"/> is <see langword="null"/>.
    /// </exception>
    public IisEventListener(IisMetricSet set) => _set = set;

    /// <summary>
    /// Called whenever a new <see cref="EventSource"/> is created. If the source name matches
    /// a known IIS/ASP.NET Core provider, events are enabled at <see cref="EventLevel.Informational"/>
    /// with <see cref="EventKeywords.All"/>, and the source is tracked for later cleanup.
    /// </summary>
    /// <param name="eventSource">The created <see cref="EventSource"/>.</param>
    /// <remarks>
    /// No subscription occurs after this listener is disposed; such sources are ignored.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="eventSource"/> is <see langword="null"/>.
    /// </exception>
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        ArgumentNullException.ThrowIfNull(eventSource);

        if (_disposed)
        {
            return;
        }

        // Known providers for IIS / ASP.NET Core Module integration.
        if (eventSource.Name is "Microsoft-AspNetCore-Server-IIS"
                             or "Microsoft-AspNetCore-Server-IISIntegration")
        {
            EnableEvents(eventSource, EventLevel.Informational, EventKeywords.All);
            _enabledSources.Add(eventSource);
        }

        base.OnEventSourceCreated(eventSource);
    }

    /// <summary>
    /// Handles incoming IIS events and translates them into metric updates on the associated
    /// <see cref="IisMetricSet"/>. Unknown event names are ignored to minimize overhead.
    /// </summary>
    /// <param name="eventData">The event payload and metadata emitted by the provider.</param>
    /// <remarks>
    /// Exceptions thrown during processing are counted via
    /// <see cref="IisMetricSet.RecordListenerFault"/> before being rethrown.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="eventData"/> is <see langword="null"/>.
    /// </exception>
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (_disposed)
        {
            return;
        }

        try
        {
            var name = eventData.EventName;

            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            switch (name)
            {
                case "ConnectionStart":
                    _set.ConnectionStart();
                    break;

                case "ConnectionStop":
                    _set.ConnectionStop();
                    break;

                case "RequestStart":
                    _set.Request();
                    break;

                case "BadRequest":
                    _set.Error(IisTagKeys.Reason_BadRequest);
                    break;

                case "Timeout":
                case "RequestTimeout":
                    _set.Error(IisTagKeys.Reason_Timeout);
                    break;

                case "Reset":
                case "ConnectionReset":
                    _set.Error(IisTagKeys.Reason_Reset);
                    break;

                case "ApplicationError":
                    _set.Error(IisTagKeys.Reason_AppError);
                    break;

                default:
                    // Intentionally ignore unknown events to keep overhead minimal.
                    break;
            }
        }
        catch
        {
            // Count rare faults without throwing/log spamming.
            _set.RecordListenerFault();

            // Preserve original exception semantics for the EventListener pipeline.
            throw;
        }
    }

    /// <summary>
    /// Disables all previously enabled event sources and releases resources.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method hides the non-virtual <see cref="EventListener.Dispose()"/> with <c>new</c>.
    /// It is safe to call multiple times; subsequent calls have no effect beyond the first.
    /// </para>
    /// </remarks>
    public new void Dispose()
    {
        if (_disposed)
        {
            base.Dispose();
            return;
        }

        _disposed = true;

        foreach (var src in _enabledSources)
        {
            try
            {
                DisableEvents(src);
            }
            catch
            {
                // Keep semantics identical to the original: signal failure to the caller.
                throw;
            }
        }

        base.Dispose();
    }
}
