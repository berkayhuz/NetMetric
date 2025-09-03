// <copyright file="IisTagKeys.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.IIS.Internal;

/// <summary>
/// Provides canonical tag keys and bounded values used when recording IIS metrics.
/// </summary>
/// <remarks>
/// <para>
/// These constants are used to annotate metrics with standardized reasons for request
/// termination or failure. Keeping the value set bounded and predictable helps avoid
/// excessive cardinality in metric dimensions and improves queryability.
/// </para>
/// <para>
/// The constants defined here are intended to be used with <see cref="IisMetricSet"/> and
/// the underlying metric factory APIs when adding tags to counters or gauges.
/// </para>
/// </remarks>
/// <example>
/// The following example increments the IIS error counter with a well-known reason value:
/// <code language="csharp"><![CDATA[
/// var set = new IisMetricSet(factory);
/// // Record a timeout error using a canonical tag value:
/// set.Error(IisTagKeys.Reason_Timeout);
/// ]]></code>
/// </example>
/// <example>
/// Adding the reason tag when building a metric directly:
/// <code language="csharp"><![CDATA[
/// var counter = factory.Counter(IisMetricNames.Errors, "IIS errors total")
///     .WithTags(t => t.Add(IisTagKeys.Reason, IisTagKeys.Reason_BadRequest))
///     .Build();
///
/// counter.Increment();
/// ]]></code>
/// </example>
internal static class IisTagKeys
{
    /// <summary>
    /// Tag key that identifies the reason for a request termination or error.
    /// </summary>
    /// <remarks>
    /// Use this key with one of the <c>Reason_*</c> values to classify error counts
    /// without introducing unbounded cardinality (e.g., avoid free-form exception messages).
    /// </remarks>
    public const string Reason = "reason";

    /// <summary>
    /// Canonical reason value indicating a malformed or invalid request (commonly corresponds to HTTP 400).
    /// </summary>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// set.Error(IisTagKeys.Reason_BadRequest);
    /// ]]></code>
    /// </example>
    public const string Reason_BadRequest = "bad_request";

    /// <summary>
    /// Canonical reason value indicating that the request timed out.
    /// </summary>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// set.Error(IisTagKeys.Reason_Timeout);
    /// ]]></code>
    /// </example>
    public const string Reason_Timeout = "timeout";

    /// <summary>
    /// Canonical reason value indicating that the connection was reset.
    /// </summary>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// set.Error(IisTagKeys.Reason_Reset);
    /// ]]></code>
    /// </example>
    public const string Reason_Reset = "reset";

    /// <summary>
    /// Canonical reason value indicating an application-level error.
    /// </summary>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// set.Error(IisTagKeys.Reason_AppError);
    /// ]]></code>
    /// </example>
    public const string Reason_AppError = "app_error";
}
