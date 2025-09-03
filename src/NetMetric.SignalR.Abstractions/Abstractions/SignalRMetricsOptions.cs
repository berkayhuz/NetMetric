// <copyright file="SignalRMetricsOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SignalR.Abstractions;

/// <summary>
/// Provides configuration options that control how SignalR-related telemetry is recorded
/// by NetMetric components (e.g., <c>SignalRHubFilter</c>, negotiation middleware, and metric sets).
/// </summary>
/// <remarks>
/// <para>
/// These options influence histogram bucket boundaries, sampling behavior, and whether certain
/// high-cardinality dimensions (such as exception type names) are captured. They are typically
/// bound from configuration (for example, <c>IOptions&lt;SignalRMetricsOptions&gt;</c>) and injected
/// where needed.
/// </para>
/// <para>
/// <b>Usage</b><br/>
/// The most common pattern is to register an instance in DI and pass it to the SignalR
/// instrumentation layer:
/// </para>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Program.cs / Startup.cs
/// services.AddSingleton(new SignalRMetricsOptions
/// {
///     EnableMessageSize = true,
///     MethodSampleRate = 0.5, // sample 50% of method invocations
///     CaptureExceptionType = true,
///     MaxExceptionTypeLength = 64,
///     NormalizeTransport = true
/// });
/// ]]></code>
/// </example>
/// </remarks>
public sealed class SignalRMetricsOptions
{
    /// <summary>
    /// Gets the histogram bucket boundaries (in milliseconds) used for measuring hub method latency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These bounds are applied when building the <c>signalr.hub.method.duration</c> histogram.
    /// Choose values that reflect typical end-to-end invocation times in your environment to get
    /// meaningful percentile and bucket breakdowns.
    /// </para>
    /// <para>
    /// The default buckets emphasize sub-second latencies while still tracking multi-second outliers.
    /// </para>
    /// </remarks>
    /// <value>
    /// A read-only list of strictly increasing bucket upper bounds, expressed in milliseconds.
    /// </value>
    public IReadOnlyList<double> LatencyBucketsMs { get; init; } =
        new List<double> { 1d, 2d, 5d, 10d, 20d, 50d, 100d, 200d, 500d, 1000d, 2000d, 5000d };

    /// <summary>
    /// Gets the histogram bucket boundaries (in milliseconds) used for measuring total connection duration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These bounds are applied to the <c>signalr.connection.duration</c> histogram, which is typically
    /// observed on disconnect. Pick values that reflect the expected lifetime of your SignalR connections
    /// (for example, shorter for server-to-server relays, longer for user sessions).
    /// </para>
    /// </remarks>
    /// <value>
    /// A read-only list of strictly increasing bucket upper bounds, expressed in milliseconds.
    /// </value>
    public IReadOnlyList<double> ConnectionDurationBucketsMs { get; init; } =
        new List<double> { 1000d, 5000d, 15000d, 60000d, 300000d, 900000d, 1800000d };

    /// <summary>
    /// Gets a value indicating whether inbound/outbound message size metrics are captured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see langword="true"/>, the instrumentation may serialize hub method arguments to determine
    /// payload size in bytes (for example, to record <c>signalr.message.size</c>). This can add small
    /// CPU overhead proportional to payload complexity. Disable to avoid additional serialization.
    /// </para>
    /// <para>Default: <see langword="false"/>.</para>
    /// </remarks>
    public bool EnableMessageSize { get; init; }

    /// <summary>
    /// Gets the sampling rate for hub method metrics, expressed as a fraction in the range [0, 1].
    /// </summary>
    /// <remarks>
    /// <para>
    /// A value of <c>1.0</c> records every invocation; <c>0.0</c> records none. Values in between
    /// sample invocations uniformly at random. This setting affects duration histograms and outcome
    /// counters for hub methods and is useful to control metric cardinality and overhead under high load.
    /// </para>
    /// <para>Default: <c>1.0</c> (collect all).</para>
    /// </remarks>
    /// <value>A double in [0, 1]. Values outside this range should be avoided.</value>
    public double MethodSampleRate { get; init; } = 1.0;

    /// <summary>
    /// Gets a value indicating whether the CLR exception type name is captured on error metrics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When enabled, error counters are tagged with the exception type (e.g., <c>OperationCanceledException</c>).
    /// This can improve diagnostics but may increase cardinality. Consider disabling in extremely
    /// high-volume systems or trimming the length using <see cref="MaxExceptionTypeLength"/>.
    /// </para>
    /// <para>Default: <see langword="true"/>.</para>
    /// </remarks>
    public bool CaptureExceptionType { get; init; } = true;

    /// <summary>
    /// Gets the maximum allowed length for exception type names when <see cref="CaptureExceptionType"/> is enabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exception type strings longer than this limit will be truncated to keep tag cardinality bounded
    /// and avoid oversized label values in downstream backends.
    /// </para>
    /// <para>Default: <c>64</c> characters.</para>
    /// </remarks>
    public int MaxExceptionTypeLength { get; init; } = 64;

    /// <summary>
    /// Gets a value indicating whether transport names are normalized to canonical tags
    /// (e.g., <c>"WebSockets"</c> → <c>"ws"</c>, <c>"ServerSentEvents"</c> → <c>"sse"</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Normalization improves aggregation and dashboard queries by keeping transport labels consistent
    /// across frameworks and hosting variations. Disable only if you need the raw transport names
    /// as reported by the hosting stack.
    /// </para>
    /// <para>Default: <see langword="true"/>.</para>
    /// </remarks>
    public bool NormalizeTransport { get; init; } = true;
}
