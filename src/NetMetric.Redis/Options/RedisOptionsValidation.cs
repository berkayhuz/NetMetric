// <copyright file="RedisOptionsValidation.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Options;

namespace NetMetric.Redis.Options;

/// <summary>
/// Performs validation for <see cref="RedisOptions"/> to ensure required values are present
/// and numeric ranges are sane before the options are consumed at runtime.
/// </summary>
/// <remarks>
/// <para>
/// This validator checks the following rules:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><see cref="RedisOptions.ConnectionString"/> must be a non-empty string (not null, empty, or whitespace).</description>
///   </item>
///   <item>
///     <description><see cref="RedisOptions.ConnectTimeoutMs"/> must be greater than zero.</description>
///   </item>
///   <item>
///     <description><see cref="RedisOptions.CommandTimeoutMs"/> must be greater than zero.</description>
///   </item>
/// </list>
/// <para>
/// The validator is designed to be registered with the options system via
/// <c>IServiceCollection</c> so that invalid configurations are reported during application startup,
/// rather than failing later at first use.
/// </para>
/// <para>
/// This type is thread-safe and stateless.
/// </para>
/// </remarks>
/// <example>
/// The following example shows how to register <see cref="RedisOptions"/> with validation:
/// <code language="csharp"><![CDATA[
/// services
///     .AddOptions<RedisOptions>()
///     .Bind(configuration.GetSection("Redis"))
///     .ValidateOnStart()
///     .Services
///     .AddSingleton<IValidateOptions<RedisOptions>, RedisOptionsValidation>();
/// ]]></code>
/// </example>
/// <seealso cref="IValidateOptions{TOptions}"/>
/// <seealso cref="ValidateOptionsResult"/>
internal sealed class RedisOptionsValidation : IValidateOptions<RedisOptions>
{
    /// <summary>
    /// Validates the supplied <see cref="RedisOptions"/> instance against the rules described in the
    /// <see cref="RedisOptionsValidation"/> remarks.
    /// </summary>
    /// <param name="name">The named options instance being validated; may be <see langword="null"/>.</param>
    /// <param name="o">The <see cref="RedisOptions"/> instance to validate.</param>
    /// <returns>
    /// A <see cref="ValidateOptionsResult"/> indicating success when all rules pass; otherwise a failure result containing one or more error messages.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="o"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method is invoked by the options infrastructure; applications typically do not call it directly.
    /// </para>
    /// </remarks>
    public ValidateOptionsResult Validate(string? name, RedisOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);

        // Connection string must be provided
        if (string.IsNullOrWhiteSpace(o.ConnectionString))
        {
            return ValidateOptionsResult.Fail("ConnectionString is required.");
        }

        // Timeouts must be strictly positive
        if (o.ConnectTimeoutMs <= 0 || o.CommandTimeoutMs <= 0)
        {
            return ValidateOptionsResult.Fail("Timeouts must be > 0.");
        }

        return ValidateOptionsResult.Success;
    }
}
