// <copyright file="IAzureMonitorDirectClient.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.ApplicationInsights.DataContracts;

namespace NetMetric.Export.AzureMonitor.Abstractions;

/// <summary>
/// Defines a contract for direct metric tracking into Azure Monitor by extending
/// <see cref="IAzureMonitorClient"/> with the ability to send pre-constructed
/// <see cref="MetricTelemetry"/> objects.
/// </summary>
/// <remarks>
/// <para>
/// This interface is designed for scenarios where the caller has already constructed
/// a <see cref="MetricTelemetry"/> with the desired metadata, tags, or sampling configuration,
/// and wants to bypass the higher-level abstraction methods (such as
/// <c>TrackMetricAsync</c> or <c>TrackMetricAggregateAsync</c>).
/// </para>
/// <para>
/// Implementations are expected to forward telemetry items to Azure Monitor reliably,
/// handling serialization, property enrichment, and flush behavior as required.
/// </para>
/// <para>
/// <b>Thread safety:</b> Implementations must be safe to use concurrently from multiple threads.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var mt = new MetricTelemetry("netmetric.export.flush_duration_ms", 123.4);
/// mt.Properties["phase"] = "final-flush";
///
/// IAzureMonitorDirectClient client = new AzureMonitorTelemetryClient(telemetryClient);
/// await client.TrackMetricTelemetryAsync(mt);
/// ]]></code>
/// </example>
public interface IAzureMonitorDirectClient : IAzureMonitorClient
{
    /// <summary>
    /// Tracks a metric telemetry item asynchronously.
    /// </summary>
    /// <param name="telemetry">
    /// The <see cref="MetricTelemetry"/> instance to send. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation. While the current
    /// implementation may complete synchronously, the token is included for consistency
    /// and future extensibility.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask"/> representing the asynchronous tracking operation.
    /// The task is typically already completed when returned.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="telemetry"/> is <see langword="null"/>.
    /// </exception>
    ValueTask TrackMetricTelemetryAsync(MetricTelemetry telemetry, CancellationToken ct = default);
}
