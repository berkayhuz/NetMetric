// <copyright file="CloudWatchExporterOptionsValidator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Options;

namespace NetMetric.AWS.Options;

/// <summary>
/// Validates <see cref="CloudWatchExporterOptions"/> to ensure option values comply with
/// Amazon CloudWatch service limits and the exporter’s behavioral assumptions.
/// </summary>
/// <remarks>
/// <para>
/// This validator performs fast, deterministic checks and returns on the first violation to
/// keep error messages focused. If you prefer aggregating multiple errors, adapt the
/// implementation to collect all failures and return
/// <see cref="ValidateOptionsResult.Fail(System.Collections.Generic.IEnumerable{string})"/>.
/// </para>
/// <para>The enforced rules include (non-exhaustive):</para>
/// <list type="bullet">
///   <item><description><see cref="CloudWatchExporterOptions.Namespace"/> must be a non-empty string.</description></item>
///   <item><description><see cref="CloudWatchExporterOptions.MaxBatchSize"/> must be in the inclusive range [1, 20] (CloudWatch batch limit).</description></item>
///   <item><description><see cref="CloudWatchExporterOptions.TimeoutMs"/> and <see cref="CloudWatchExporterOptions.BaseDelayMs"/> must be positive.</description></item>
///   <item><description><see cref="CloudWatchExporterOptions.MaxRetries"/> must be greater than or equal to 0.</description></item>
///   <item><description><see cref="CloudWatchExporterOptions.DimensionTagKeys"/> (if provided) must contain ≤ 10 entries (CloudWatch dimension limit), with no empty or duplicate keys (ordinal comparison).</description></item>
/// </list>
/// </remarks>
/// <example>
/// <para><b>Registering validation with the options pipeline</b></para>
/// <code language="csharp">
/// services
///     .AddOptions&lt;CloudWatchExporterOptions&gt;()
///     .Bind(configuration.GetSection("NetMetric:AWS:CloudWatch"))
///     .ValidateOptions&lt;CloudWatchExporterOptionsValidator&gt;()
///     .ValidateOnStart(); // Fail fast at app startup
/// </code>
/// <para><b>Typical invalid configuration symptoms</b></para>
/// <list type="bullet">
///   <item><description>Empty <c>Namespace</c> → "CloudWatch Namespace is required."</description></item>
///   <item><description><c>MaxBatchSize = 50</c> → "MaxBatchSize must be between 1 and 20 (CloudWatch limit)."</description></item>
///   <item><description>Duplicate keys in <c>DimensionTagKeys</c> → "DimensionTagKeys contains empty/duplicate keys."</description></item>
/// </list>
/// </example>
/// <threadsafety>
/// This validator is stateless and thread-safe. It can be used concurrently by the options monitor.
/// </threadsafety>
internal sealed class CloudWatchExporterOptionsValidator
    : IValidateOptions<CloudWatchExporterOptions>
{
    /// <summary>
    /// Validates the provided <see cref="CloudWatchExporterOptions"/> instance.
    /// </summary>
    /// <param name="name">The named options instance being validated, or <see langword="null"/> for the default instance.</param>
    /// <param name="options">The options to validate. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A <see cref="ValidateOptionsResult"/> indicating success or failure. On failure, the
    /// result contains a descriptive error message suitable for logs and startup diagnostics.
    /// </returns>
    /// <remarks>
    /// Returning early on the first violation keeps the failure reason clear and actionable.
    /// Adapt the method if your configuration UX benefits from batched error reporting.
    /// </remarks>
    public ValidateOptionsResult Validate(string? name, CloudWatchExporterOptions options)
    {
        if (options is null)
            return ValidateOptionsResult.Fail("Options are null.");

        if (string.IsNullOrWhiteSpace(options.Namespace))
            return ValidateOptionsResult.Fail("CloudWatch Namespace is required.");

        if (options.MaxBatchSize <= 0 || options.MaxBatchSize > 20)
            return ValidateOptionsResult.Fail("MaxBatchSize must be between 1 and 20 (CloudWatch limit).");

        if (options.TimeoutMs <= 0)
            return ValidateOptionsResult.Fail("TimeoutMs must be positive.");

        if (options.MaxRetries < 0)
            return ValidateOptionsResult.Fail("MaxRetries must be >= 0.");

        if (options.BaseDelayMs <= 0)
            return ValidateOptionsResult.Fail("BaseDelayMs must be positive.");

        // CloudWatch allows at most 10 dimensions per metric.
        if (options.DimensionTagKeys is { Count: > 10 })
            return ValidateOptionsResult.Fail("DimensionTagKeys cannot exceed 10 (CloudWatch dimension limit).");

        // Ensure no empty or duplicate keys (ordinal, trimmed).
        if (options.DimensionTagKeys is not null)
        {
            var cleaned = options.DimensionTagKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (cleaned.Length != options.DimensionTagKeys.Count)
                return ValidateOptionsResult.Fail("DimensionTagKeys contains empty/duplicate keys.");
        }

        return ValidateOptionsResult.Success;
    }
}
