// <copyright file="CosmosOptionsValidator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Options;

namespace NetMetric.Azure.Options.Validation;

/// <summary>
/// Validates <see cref="CosmosOptions"/> to ensure that configuration values
/// supplied for Cosmos DB diagnostics/collection are internally consistent and well-formed.
/// </summary>
/// <remarks>
/// <para>
/// This validator performs light, syntactic checks that help catch common misconfigurations early:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     If any target containers are specified via <see cref="CosmosOptions.Containers"/>, then a non-empty
///     <see cref="CosmosOptions.AccountEndpoint"/> must also be provided.
///     </description>
///   </item>
///   <item>
///     <description>
///     If <see cref="CosmosOptions.AccountEndpoint"/> is set, it must be a valid absolute URI whose scheme is
///     <c>https</c> (preferred) or <c>http</c>.
///     </description>
///   </item>
/// </list>
/// <para>
/// The validator does <b>not</b> attempt to contact Azure resources; it only verifies option shape/format so that
/// startup failures are fast and actionable.
/// </para>
/// </remarks>
/// <example>
/// The typical way to wire this validator is during DI setup:
/// <code language="csharp"><![CDATA[
/// services.AddOptions<CosmosOptions>()
///         .Bind(configuration.GetSection("NetMetric:Azure:Cosmos"))
///         .ValidateOnStart() // ensure problems surface at startup
///         .Services
///         .AddSingleton<IValidateOptions<CosmosOptions>, CosmosOptionsValidator>();
/// ]]></code>
/// With the following <c>appsettings.json</c> snippet:
/// <code language="json"><![CDATA[
/// {
///   "NetMetric": {
///     "Azure": {
///       "Cosmos": {
///         "AccountEndpoint": "https://mycosmos.documents.azure.com:443/",
///         "Containers": [ { "Database": "prod", "Container": "orders" } ]
///       }
///     }
///   }
/// }
/// ]]></code>
/// </example>
internal sealed class CosmosOptionsValidator : IValidateOptions<CosmosOptions>
{
    /// <summary>
    /// Validates the provided <see cref="CosmosOptions"/> instance for basic consistency.
    /// </summary>
    /// <param name="name">
    /// The named options instance being validated (may be <see langword="null"/> for the default instance).
    /// </param>
    /// <param name="o">The <see cref="CosmosOptions"/> object to validate. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A <see cref="ValidateOptionsResult"/> that represents the outcome of validation. Returns
    /// <see cref="ValidateOptionsResult.Success"/> if all checks pass; otherwise returns a failure result
    /// containing a human-readable error message describing the first detected issue.
    /// </returns>
    /// <remarks>
    /// <para>Failure cases:</para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///     <b>Containers without endpoint</b> — If <see cref="CosmosOptions.Containers"/> is non-empty while
    ///     <see cref="CosmosOptions.AccountEndpoint"/> is <see langword="null"/>, empty, or whitespace.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>Malformed endpoint</b> — If <see cref="CosmosOptions.AccountEndpoint"/> is present but not a valid
    ///     absolute URI (scheme must be <c>https</c> or <c>http</c>).
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// On success, the options are considered structurally valid; runtime connectivity, authorization, and
    /// resource existence are out of scope for this validator.
    /// </para>
    /// </remarks>
    /// <example>
    /// Programmatic validation:
    /// <code language="csharp"><![CDATA[
    /// var options = new CosmosOptions
    /// {
    ///     AccountEndpoint = "https://acct.documents.azure.com/",
    ///     Containers = new List<(string Database, string Container)> { ("prod", "orders") }
    /// };
    ///
    /// var validator = new CosmosOptionsValidator();
    /// var result = validator.Validate(name: null, options);
    /// if (result.Failed)
    /// {
    ///     // e.g. log and stop startup
    ///     logger.LogError("Cosmos options invalid: {Error}", result.FailureMessage);
    /// }
    /// ]]></code>
    /// </example>
    public ValidateOptionsResult Validate(string? name, CosmosOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);

        // Case 1: Containers specified but no endpoint provided.
        if (o.Containers is { Count: > 0 } && string.IsNullOrWhiteSpace(o.AccountEndpoint))
        {
            return ValidateOptionsResult.Fail("AccountEndpoint must be provided when Containers are specified.");
        }

        // Case 2: Endpoint provided but not a valid absolute http(s) URI.
        if (!string.IsNullOrWhiteSpace(o.AccountEndpoint))
        {
            if (!Uri.TryCreate(o.AccountEndpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return ValidateOptionsResult.Fail("AccountEndpoint must be a valid absolute URI (https or http).");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
