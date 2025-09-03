// <copyright file="AzureCommonOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Azure.Identity;

namespace NetMetric.Azure.Options;

/// <summary>
/// Provides common configuration options shared across Azure-related collectors and adapters.
/// </summary>
/// <remarks>
/// <para>
/// These options control credential usage and client behavior (such as timeouts) 
/// for Azure SDK operations within NetMetric. They are typically bound from configuration
/// (e.g., <c>appsettings.json</c>) and validated via <see cref="Validation.AzureCommonOptionsValidator"/>.
/// </para>
/// <para>
/// Consumers should register <see cref="AzureCommonOptions"/> using <c>IOptions&lt;AzureCommonOptions&gt;</c>
/// so that they can be injected into collectors and adapters.
/// </para>
/// </remarks>
/// <example>
/// Example <c>appsettings.json</c> section:
/// <code>
/// {
///   "Azure": {
///     "UseDefaultCredential": true,
///     "ManagedIdentityClientId": "11111111-2222-3333-4444-555555555555",
///     "ClientTimeoutMs": 5000
///   }
/// }
/// </code>
/// Example registration in <c>Program.cs</c>:
/// <code>
/// builder.Services.Configure&lt;AzureCommonOptions&gt;(
///     builder.Configuration.GetSection("Azure"));
/// </code>
/// </example>
public sealed class AzureCommonOptions
{
    /// <summary>
    /// Gets a value indicating whether <see cref="DefaultAzureCredential"/>
    /// should be used by default for authentication.
    /// </summary>
    /// <value>
    /// <c>true</c> to use <see cref="DefaultAzureCredential"/> for authentication;
    /// <c>false</c> to require a custom <see cref="IAzureCredentialProvider"/>.
    /// </value>
    /// <remarks>
    /// Defaults to <c>true</c>.  
    /// If set to <c>false</c>, a custom credential provider must be supplied and registered
    /// (for example, for testing scenarios or unsupported environments).
    /// </remarks>
    public bool UseDefaultCredential { get; init; } = true;

    /// <summary>
    /// Gets the client ID of the managed identity to use, if applicable.
    /// </summary>
    /// <value>
    /// A string containing the managed identity client ID, or <c>null</c> if not specified.
    /// </value>
    /// <remarks>
    /// Relevant when multiple managed identities are available and a specific one must be targeted.  
    /// This value is passed to <see cref="DefaultAzureCredentialOptions.ManagedIdentityClientId"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var opts = new AzureCommonOptions
    /// {
    ///     ManagedIdentityClientId = "11111111-2222-3333-4444-555555555555"
    /// };
    /// </code>
    /// </example>
    public string? ManagedIdentityClientId { get; init; }

    /// <summary>
    /// Gets the timeout, in milliseconds, applied to Azure SDK client operations.
    /// </summary>
    /// <value>
    /// The operation timeout in milliseconds. Default is 10000 (10 seconds).
    /// A value of 0 indicates an infinite timeout.
    /// </value>
    /// <remarks>
    /// This value is used to configure SDK client options such as 
    /// <c>CosmosClientOptions.RequestTimeout</c> or service client timeouts.
    /// </remarks>
    /// <example>
    /// <code>
    /// var opts = new AzureCommonOptions
    /// {
    ///     ClientTimeoutMs = 30000 // 30 seconds
    /// };
    /// </code>
    /// </example>
    public int ClientTimeoutMs { get; init; } = 10000;  // 0 => infinite
}
