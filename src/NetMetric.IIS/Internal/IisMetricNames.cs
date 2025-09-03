// <copyright file="IisMetricNames.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.IIS.Internal;

/// <summary>
/// Defines canonical metric identifier constants for IIS (Internet Information Services)
/// used by NetMetric collectors and listeners.
/// </summary>
/// <remarks>
/// <para>
/// These names are intentionally stable so that dashboards, queries, and alert rules
/// remain compatible across releases. They are consumed by the IIS metrics pipeline
/// (e.g., event listeners and metric sets) to record connection counts, request totals,
/// error reasons, and listener-level faults.
/// </para>
/// <para>
/// <b>Naming and type conventions</b>:
/// <list type="bullet">
///   <item><description><c>*.active</c> rarr; gauge (point-in-time measurement).</description></item>
///   <item><description><c>*.total</c> rarr; counter (monotonically increasing).</description></item>
///   <item><description>
///     Certain counters use additional dimensions (for example,
///     <c>iis.errors.total</c> is typically emitted with a <c>{reason}</c> tag,
///     see <see cref="IisTagKeys.Reason"/>).
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Dimensions</b>:
/// Unless noted otherwise, these identifiers carry no dimensions. When a metric
/// admits a dimension (e.g., an error <c>reason</c>), it is explicitly called out
/// in the constant's documentation.
/// </para>
/// <para>
/// <b>Thread-safety</b>:
/// These are compile-time constants and therefore inherently thread-safe.
/// </para>
/// <example>
/// The following example shows how a collector might build and increment the
/// total connections counter using a hypothetical metric factory:
/// <code language="csharp"><![CDATA[
/// // Acquire an IMetricFactory (injected elsewhere).
/// var totalConnections = factory.Counter(IisMetricNames.ConnTotal, "IIS total connections").Build();
/// totalConnections.Increment();
/// ]]></code>
/// </example>
/// </remarks>
internal static class IisMetricNames
{
    /// <summary>
    /// Current number of active IIS connections.
    /// </summary>
    /// <remarks>
    /// <para>Type: <c>gauge</c></para>
    /// <para>Dimensions: none</para>
    /// </remarks>
    public const string ConnActive = "iis.connections.active";

    /// <summary>
    /// Total number of IIS connections accepted since process start.
    /// </summary>
    /// <remarks>
    /// <para>Type: <c>counter</c> (monotonic)</para>
    /// <para>Dimensions: none</para>
    /// </remarks>
    public const string ConnTotal = "iis.connections.total";

    /// <summary>
    /// Total number of HTTP requests processed by IIS since process start.
    /// </summary>
    /// <remarks>
    /// <para>Type: <c>counter</c> (monotonic)</para>
    /// <para>Dimensions: none</para>
    /// </remarks>
    public const string Requests = "iis.requests.total";

    /// <summary>
    /// Total number of IIS request errors.
    /// </summary>
    /// <remarks>
    /// <para>Type: <c>counter</c> (monotonic)</para>
    /// <para>
    /// Dimensions: <c>{reason}</c> — a short, stable label describing the error cause.
    /// Use <see cref="IisTagKeys.Reason"/> as the tag key and one of the canonical values
    /// such as <see cref="IisTagKeys.Reason_BadRequest"/>, <see cref="IisTagKeys.Reason_Timeout"/>,
    /// <see cref="IisTagKeys.Reason_Reset"/>, or <see cref="IisTagKeys.Reason_AppError"/>.
    /// </para>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var errorCounter = factory
    ///     .Counter(IisMetricNames.Errors, "IIS errors total")
    ///     .WithTags(t => t.Add(IisTagKeys.Reason, IisTagKeys.Reason_Timeout))
    ///     .Build();
    /// errorCounter.Increment();
    /// ]]></code>
    /// </example>
    /// </remarks>
    public const string Errors = "iis.errors.total";

    /// <summary>
    /// Total number of listener-level faults observed while processing IIS events.
    /// </summary>
    /// <remarks>
    /// <para>Type: <c>counter</c> (monotonic)</para>
    /// <para>Dimensions: none</para>
    /// <para>
    /// This is a defensive signal intended to stay near zero. A rising trend may indicate
    /// unexpected provider payloads or exceptions inside the event handling path.
    /// </para>
    /// </remarks>
    public const string ListenerFaults = "iis.listener.faults";
}
