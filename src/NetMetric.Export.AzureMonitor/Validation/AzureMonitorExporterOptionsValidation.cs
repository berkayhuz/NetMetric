// <copyright file="AzureMonitorExporterOptionsValidation.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System;
using Microsoft.Extensions.Options;

namespace NetMetric.Export.AzureMonitor.Validation;

/// <summary>
/// Validates <see cref="AzureMonitorExporterOptions"/> using the <see cref="IValidateOptions{TOptions}"/> pattern.
/// Ensures that required configuration (e.g., connection string) is present and numeric/time-based
/// settings fall within supported ranges for reliable Azure Monitor exporting.
/// </summary>
/// <remarks>
/// <para>
/// This validator is intended to be registered with the Options system so that misconfiguration
/// is detected early—ideally at application startup via <c>ValidateOnStart()</c>. A failing validation
/// result prevents the application from running with invalid settings, reducing runtime faults.
/// </para>
/// <para>
/// The following conditions are validated:
/// <list type="bullet">
///   <item><description><see cref="AzureMonitorExporterOptions.ConnectionString"/> must be non-empty.</description></item>
///   <item><description><see cref="AzureMonitorExporterOptions.MaxQueueLength"/> must be greater than 0.</description></item>
///   <item><description><see cref="AzureMonitorExporterOptions.MaxBatchSize"/> must be greater than 0.</description></item>
///   <item><description><see cref="AzureMonitorExporterOptions.MaxRetryAttempts"/> must be greater than or equal to 0.</description></item>
///   <item><description><see cref="AzureMonitorExporterOptions.BaseDelay"/> and <see cref="AzureMonitorExporterOptions.MaxDelay"/> must be non-negative.</description></item>
///   <item><description><see cref="AzureMonitorExporterOptions.BaseDelay"/> must be less than or equal to <see cref="AzureMonitorExporterOptions.MaxDelay"/>.</description></item>
/// </list>
/// </para>
/// <para>
/// This type is stateless and thread-safe.
/// </para>
/// </remarks>
/// <example>
/// The following example demonstrates how to register the options and enable validation:
/// <code language="csharp"><![CDATA[
/// Using Microsoft.Extensions.DependencyInjection;
/// builder is a WebApplicationBuilder or HostApplicationBuilder
/// builder.Services
///     .AddOptions<AzureMonitorExporterOptions>()
///     .Bind(builder.Configuration.GetSection("AzureMonitor"))
///     .ValidateOnStart() // Fail fast at startup if invalid.
///     .Services
///     .AddSingleton<IValidateOptions<AzureMonitorExporterOptions>, AzureMonitorExporterOptionsValidation>();
/// ]]></code>
/// </example>
/// <seealso cref="AzureMonitorExporterOptions"/>
/// <seealso cref="IValidateOptions{TOptions}"/>
public sealed class AzureMonitorExporterOptionsValidation : IValidateOptions<AzureMonitorExporterOptions>
{
    /// <summary>
    /// Performs validation on the supplied <paramref name="options"/> instance.
    /// </summary>
    /// <param name="name">The named options instance being validated, if any. Not used.</param>
    /// <param name="options">The <see cref="AzureMonitorExporterOptions"/> instance to validate.</param>
    /// <returns>
    /// <para>
    /// <see cref="ValidateOptionsResult.Success"/> when all checks pass; otherwise a failure result containing one or more
    /// descriptive error messages indicating the violated constraints.
    /// </para>
    /// </returns>
    /// <remarks>
    /// This method throws <see cref="ArgumentNullException"/> if <paramref name="options"/> is <see langword="null"/>.
    /// </remarks>
    public ValidateOptionsResult Validate(string? name, AzureMonitorExporterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail("ConnectionString is required.");
        }

        if (options.MaxQueueLength <= 0)
        {
            return ValidateOptionsResult.Fail("MaxQueueLength must be > 0.");
        }

        if (options.MaxBatchSize <= 0)
        {
            return ValidateOptionsResult.Fail("MaxBatchSize must be > 0.");
        }

        if (options.MaxRetryAttempts < 0)
        {
            return ValidateOptionsResult.Fail("MaxRetryAttempts must be >= 0.");
        }

        if (options.BaseDelay < TimeSpan.Zero || options.MaxDelay < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("Delays must be >= 0.");
        }

        if (options.BaseDelay > options.MaxDelay)
        {
            return ValidateOptionsResult.Fail("BaseDelay must be <= MaxDelay.");
        }

        return ValidateOptionsResult.Success;
    }
}
