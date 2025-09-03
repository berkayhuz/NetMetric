// <copyright file="TelemetryEnvelope.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.ApplicationInsights.DataContracts;

namespace NetMetric.Export.AzureMonitor.Internal;

/// <summary>
/// Represents a lightweight, immutable wrapper (envelope) for a single telemetry item destined for Azure Monitor.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TelemetryEnvelope"/> is a <c>readonly record struct</c> that encapsulates a single
/// <see cref="MetricTelemetry"/> instance. It is intended to be passed across internal queues and pipelines
/// (e.g., bounded channels) with minimal allocation overhead and without exposing additional mutation surface.
/// </para>
/// <para>
/// Because the type is a value type and marked <c>readonly</c>, it is naturally thread-safe for concurrent
/// reads after construction. The contained <see cref="MetricTelemetry"/> instance should be treated as immutable
/// once wrapped to avoid race conditions in asynchronous processing.
/// </para>
/// <para>
/// This envelope does not perform validation. Producers are responsible for creating a well-formed
/// <see cref="MetricTelemetry"/> (e.g., setting <see cref="MetricTelemetry.Name"/>, <see cref="MetricTelemetry.Sum"/>,
/// and any relevant dimensions) before enqueuing.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// using Microsoft.ApplicationInsights.DataContracts;
/// using NetMetric.Export.AzureMonitor.Internal;
///
/// // Create a MetricTelemetry with required fields
/// var metric = new MetricTelemetry(
///     name: "requests.duration",
///     sum: 123.45);
///
/// // Optionally add properties (dimensions/tags)
/// metric.Properties["endpoint"] = "/api/orders";
/// metric.Properties["method"]   = "GET";
///
/// // Optionally set timestamp or other metadata
/// metric.Timestamp = DateTimeOffset.UtcNow;
///
/// // Wrap in an envelope for queuing/transport in the exporter pipeline
/// var envelope = new TelemetryEnvelope(metric);
///
/// // Enqueue to an internal channel (example)
/// // await azureMonitorChannel.EnqueueAsync(envelope, cancellationToken);
/// ]]></code>
/// </example>
/// <seealso cref="MetricTelemetry"/>
/// <seealso href="https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview">Azure Monitor Application Insights</seealso>
internal readonly record struct TelemetryEnvelope(MetricTelemetry Telemetry);
