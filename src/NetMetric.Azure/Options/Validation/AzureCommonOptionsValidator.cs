// <copyright file="AzureCommonOptionsValidator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Options;

namespace NetMetric.Azure.Options.Validation;


/// <summary>
/// Validates <see cref="AzureCommonOptions"/> instances to ensure option values are well-formed
/// and within acceptable ranges for Azure client operations.
/// </summary>
/// <remarks>
/// <para>This validator currently enforces the following rule:</para>
/// <list type="bullet">
///   <item>
///     <description><c>ClientTimeoutMs</c> must be greater than or equal to <c>0</c>.</description>
///   </item>
/// </list>
/// <para>
/// A value of <c>0</c> indicates an infinite timeout; positive values represent a timeout in milliseconds.
/// Negative values are rejected.
/// </para>
/// <para><b>Usage in DI</b></para>
/// <example>
/// <code lang="csharp"><![CDATA[
/// services.AddOptions<AzureCommonOptions>()
///         .Bind(configuration.GetSection("NetMetric:Azure:Common"))
///         .ValidateOnStart();
///
/// services.AddSingleton<IValidateOptions<AzureCommonOptions>, AzureCommonOptionsValidator>();
/// ]]></code>
/// </example>
/// <para><b>Typical configuration</b></para>
/// <code lang="json"><![CDATA[
/// {
///   "NetMetric": {
///     "Azure": {
///       "Common": {
///         "UseDefaultCredential": true,
///         "ManagedIdentityClientId": null,
///         "ClientTimeoutMs": 10000
///       }
///     }
///   }
/// }
/// ]]></code>
/// <para>This validator is idempotent and thread-safe; it does not mutate the provided options instance.</para>
/// </remarks>
internal sealed class AzureCommonOptionsValidator : IValidateOptions<AzureCommonOptions>
{
    /// <summary>
    /// Validates the provided <see cref="AzureCommonOptions"/> instance.
    /// </summary>
    /// <param name="name">
    /// The name of the options instance being validated (may be <see langword="null"/> when unnamed).
    /// </param>
    /// <param name="o">The options instance to validate. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// <see cref="ValidateOptionsResult.Success"/> when all checks pass; otherwise a failure result
    /// with an explanatory error message.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="o"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Validation rules:
    /// <list type="bullet">
    ///   <item><description><c>ClientTimeoutMs</c> must be <c>&gt;= 0</c>.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Examples of valid values:
    /// <list type="bullet">
    ///   <item><description><c>0</c> — infinite timeout (allowed).</description></item>
    ///   <item><description><c>10000</c> — 10 seconds.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Examples of invalid values:
    /// <list type="bullet">
    ///   <item><description><c>-1</c> — negative timeouts are rejected.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public ValidateOptionsResult Validate(string? name, AzureCommonOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);

        if (o.ClientTimeoutMs < 0)
            return ValidateOptionsResult.Fail("ClientTimeoutMs must be >= 0.");

        return ValidateOptionsResult.Success;
    }
}
