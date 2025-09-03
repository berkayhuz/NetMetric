// <copyright file="KestrelReasonValues.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kestrel.Internal;

/// <summary>
/// Provides canonical reason values used for tagging and classifying
/// Kestrel connection and request termination metrics.
/// </summary>
/// <remarks>
/// <para>
/// These constants are used as standardized tag values when incrementing counters
/// such as connection resets, protocol errors, or application faults.
/// Using fixed values ensures low-cardinality dimensions that remain
/// stable across releases, making them safe for dashboards and alerts.
/// </para>
/// <para>
/// Typical usage:
/// <code language="csharp"><![CDATA[
/// // Example: recording a reset reason
/// metricSet.DecConnection("h2", "tcp", KestrelReasonValues.Reset);
///
/// // Example: recording a bad request
/// metricSet.Error(KestrelReasonValues.BadRequest);
/// ]]></code>
/// </para>
/// </remarks>
internal static class KestrelReasonValues
{
    /// <summary>
    /// Represents that the connection was reset by the peer or server.
    /// <para>
    /// Used when decrementing active connection counters with a reset reason.
    /// </para>
    /// </summary>
    public const string Reset = "reset";

    /// <summary>
    /// Represents that the request was rejected because of a malformed
    /// or invalid HTTP message.
    /// <para>
    /// This value is typically incremented when Kestrel detects
    /// an HTTP parsing failure.
    /// </para>
    /// </summary>
    public const string BadRequest = "bad_request";

    /// <summary>
    /// Represents that the request failed due to an unhandled
    /// application-level exception.
    /// <para>
    /// This value is typically used when recording application-originated
    /// errors that surface at the Kestrel connection/request layer.
    /// </para>
    /// </summary>
    public const string ApplicationError = "app_error";
}
