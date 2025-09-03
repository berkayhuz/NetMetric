// <copyright file="NoopObjectModelValidator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace NetMetric.AspNetCore.Validation;

/// <summary>
/// An <see cref="IObjectModelValidator"/> implementation that performs no validation.
/// </summary>
/// <remarks>
/// <para>
/// This validator is a no-op replacement for the default MVC object model validator. It can be
/// registered to completely bypass model validation when validation metrics are collected
/// elsewhere or validation is intentionally disabled for performance or testing.
/// </para>
/// <para>
/// <strong>Effect:</strong> When registered as the application's <see cref="IObjectModelValidator"/>,
/// MVC's automatic model validation will be effectively disabled. Client- or attribute-based
/// validation (e.g., <c>[Required]</c>) will not produce model state errors through this component.
/// </para>
/// <para><strong>Thread Safety:</strong> The implementation is stateless and safe for concurrent use.</para>
/// </remarks>
/// <example>
/// Register globally (replacing the default validator):
/// <code>
/// using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
/// using Microsoft.Extensions.DependencyInjection;
///
/// builder.Services.AddSingleton&lt;IObjectModelValidator, NoopObjectModelValidator&gt;();
/// // or, if you prefer replacement semantics with ServiceCollection extensions:
/// // builder.Services.Replace(ServiceDescriptor.Singleton&lt;IObjectModelValidator, NoopObjectModelValidator&gt;());
/// </code>
/// </example>
/// <seealso cref="IObjectModelValidator"/>
/// <seealso cref="ActionContext"/>
/// <seealso cref="ValidationStateDictionary"/>
public sealed class NoopObjectModelValidator : IObjectModelValidator
{
    /// <summary>
    /// Does nothing and returns immediately, effectively disabling model validation.
    /// </summary>
    /// <param name="actionContext">The current <see cref="ActionContext"/>.</param>
    /// <param name="validationState">The validation state dictionary (ignored).</param>
    /// <param name="prefix">The model prefix (ignored).</param>
    /// <param name="model">The object to validate (ignored).</param>
    /// <remarks>
    /// This method intentionally performs no work and does not modify
    /// <see cref="ActionContext.ModelState"/>. It never throws by design.
    /// </remarks>
    public void Validate(
        ActionContext actionContext,
        ValidationStateDictionary? validationState,
        string prefix,
        object? model)
    {
        // no-op
    }
}
