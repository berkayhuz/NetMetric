// <copyright file="MvcMetricNames.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AspNetCore.Internal;

/// <summary>
/// Provides metric name constants specific to ASP.NET Core MVC pipeline instrumentation.
/// </summary>
/// <remarks>
/// <para>
/// These names are used to record measurements for different MVC filter/pipeline stages
/// such as authorization, resource filters, action execution, exception handling, and results.
/// </para>
/// <para>
/// Using well-known constants ensures consistency across metrics and aligns with
/// OpenTelemetry and Prometheus conventions for observability.
/// </para>
/// </remarks>
/// <seealso cref="MvcStageNames"/>
internal static class MvcMetricNames
{
    /// <summary>
    /// Metric name for measuring execution duration of MVC pipeline stages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported as a histogram in milliseconds. Typical dimensions (tags) include:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>route</c> — normalized ASP.NET Core route template.</description></item>
    ///   <item><description><c>method</c> — HTTP method (GET, POST, etc.).</description></item>
    ///   <item><description><c>scheme</c> — HTTP scheme (http/https).</description></item>
    ///   <item><description><c>http.flavor</c> — protocol flavor (1.1, 2, or 3).</description></item>
    ///   <item><description><c>stage</c> — one of the <see cref="MvcStageNames"/> constants representing the pipeline stage.</description></item>
    /// </list>
    /// </remarks>
    public const string StageDuration = "aspnetcore.stage.duration"; // ms histogram
}
