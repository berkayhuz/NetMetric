// <copyright file="StorageQueuesOptionsValidator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Options;

namespace NetMetric.Azure.Options.Validation;

/// <summary>
/// Validates <see cref="StorageQueuesOptions"/> to ensure that configuration values
/// for Azure Storage Queues integration are consistent and well-formed.
/// </summary>
/// <remarks>
/// <para>This validator enforces the following rules:</para>
/// <list type="bullet">
///   <item><description>If <see cref="StorageQueuesOptions.Queues"/> are specified, a non-empty <see cref="StorageQueuesOptions.AccountName"/> must also be provided.</description></item>
///   <item><description>If <see cref="StorageQueuesOptions.EndpointSuffix"/> is provided, it must be a DNS suffix only (for example, <c>core.windows.net</c>) and must not include a URI scheme such as <c>https://</c>.</description></item>
/// </list>
/// <para>All other combinations are considered valid.</para>
/// </remarks>
/// <example>
/// The following example shows how to register and rely on this validator in an application:
/// <code language="csharp"><![CDATA[
/// Program.cs
/// services.AddOptions<StorageQueuesOptions>()
///         .Bind(configuration.GetSection("Azure:StorageQueues"))
///         .Services
///         .AddSingleton<IValidateOptions<StorageQueuesOptions>, StorageQueuesOptionsValidator>();
///
/// // Later, when options are resolved, validation is executed automatically:
/// var opts = serviceProvider.GetRequiredService<IOptions<StorageQueuesOptions>>().Value;
/// ]]></code>
/// </example>
/// <seealso cref="StorageQueuesOptions"/>
/// <seealso cref="IValidateOptions{TOptions}"/>
/// <seealso cref="ValidateOptionsResult"/>
internal sealed class StorageQueuesOptionsValidator : IValidateOptions<StorageQueuesOptions>
{
    /// <summary>
    /// Validates the given <see cref="StorageQueuesOptions"/> instance.
    /// </summary>
    /// <param name="name">The name of the options instance being validated, if named options are used; otherwise <see langword="null"/>.</param>
    /// <param name="o">The <see cref="StorageQueuesOptions"/> instance to validate. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A <see cref="ValidateOptionsResult"/> that indicates success if validation passes,
    /// or failure with a descriptive error message if validation fails.
    /// </returns>
    /// <remarks>
    /// <para>Returns failure when queues are configured but <see cref="StorageQueuesOptions.AccountName"/> is missing or whitespace.</para>
    /// <para>Returns failure when <see cref="StorageQueuesOptions.EndpointSuffix"/> contains a URI scheme (for example, <c>http://</c> or <c>https://</c>).</para>
    /// <para>Returns success in all other cases.</para>
    /// </remarks>
    public ValidateOptionsResult Validate(string? name, StorageQueuesOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);

        // Queues imply an account name must be present.
        if (o.Queues is { Count: > 0 } && string.IsNullOrWhiteSpace(o.AccountName))
        {
            return ValidateOptionsResult.Fail("AccountName must be provided when Queues are specified.");
        }

        // Endpoint suffix should be a plain DNS suffix, not a full URI.
        if (!string.IsNullOrWhiteSpace(o.EndpointSuffix) &&
            o.EndpointSuffix!.Contains("://", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail("EndpointSuffix must be a DNS suffix only (e.g., core.windows.net).");
        }

        return ValidateOptionsResult.Success;
    }
}
