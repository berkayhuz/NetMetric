// <copyright file="MvcStageNames.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AspNetCore.Internal;

/// <summary>
/// Provides constant names for the different stages in the ASP.NET Core MVC request pipeline,
/// used as tag values when recording metrics.
/// </summary>
/// <remarks>
/// <para>
/// These values are applied as the <c>"stage"</c> tag in metrics produced by <see cref="MvcMetricSet"/>.  
/// Using consistent stage names ensures observability tools can group and analyze performance
/// across different parts of the pipeline.
/// </para>
/// <para>
/// Typical emitters include MVC filters, model binder/validator decorators, and authorization handlers
/// that measure per-stage latency and record the observations to histograms such as
/// <see cref="MvcMetricNames.StageDuration"/>.
/// </para>
/// </remarks>
/// <seealso cref="MvcMetricSet"/>
/// <seealso cref="MvcMetricNames"/>
internal static class MvcStageNames
{
    /// <summary>
    /// Stage for model binding (binding request data to action parameters).
    /// </summary>
    /// <remarks>
    /// Usually measured by a custom <c>IModelBinder</c> wrapper (e.g., a timing model binder).
    /// </remarks>
    public const string ModelBinding = "model_binding";

    /// <summary>
    /// Stage for model validation (executing validation attributes and validators).
    /// </summary>
    /// <remarks>
    /// Often measured by an <see cref="Microsoft.AspNetCore.Mvc.ModelBinding.Validation.IObjectModelValidator"/> decorator.
    /// </remarks>
    public const string Validation = "validation";

    /// <summary>
    /// Stage for action execution (controller/action or endpoint handler logic).
    /// </summary>
    /// <remarks>
    /// Emitted by components such as <see cref="NetMetric.AspNetCore.Filters.ActionTimingFilter"/>
    /// or Minimal API timing filters.
    /// </remarks>
    public const string Action = "action";

    /// <summary>
    /// Stage for authorization filter execution (evaluating policies/attributes).
    /// </summary>
    /// <remarks>
    /// Represents the MVC authorization filter phase, distinct from the deeper authorization decision evaluation.
    /// </remarks>
    public const string Authorization = "authorization";

    /// <summary>
    /// Stage for resource filter execution (resource initialization/caching).
    /// </summary>
    /// <remarks>
    /// Wraps action execution (excluding authorization) and is commonly used for caching or scoping services.
    /// </remarks>
    public const string Resource = "resource";

    /// <summary>
    /// Stage for exception filter execution (when unhandled exceptions occur).
    /// </summary>
    /// <remarks>
    /// Emitted when exception filters run after an unhandled exception is raised during action execution.
    /// </remarks>
    public const string Exception = "exception";

    /// <summary>
    /// Stage for detailed authorization decision evaluation
    /// (policy/requirement checks inside the authorization system).
    /// </summary>
    /// <remarks>
    /// This is distinct from the MVC authorization filter stage and is typically emitted by
    /// authorization handlers/middleware decorators that time policy evaluation.
    /// </remarks>
    public const string AuthzDecision = "authorization_decision";
}
