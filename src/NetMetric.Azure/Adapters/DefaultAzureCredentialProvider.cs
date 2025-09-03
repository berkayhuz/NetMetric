// <copyright file="DefaultAzureCredentialProvider.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Azure.Core;
using Azure.Identity;

namespace NetMetric.Azure.Adapters;

/// <summary>
/// Provides an <see cref="IAzureCredentialProvider"/> implementation backed by
/// <see cref="DefaultAzureCredential"/> to authenticate Azure SDK clients consistently
/// across local development and cloud-hosted environments.
/// </summary>
/// <remarks>
/// <para>
/// <b>Credential chain</b><br/>
/// This provider relies on <see cref="DefaultAzureCredential"/> which composes a sequence of
/// credential sources (environment variables, managed identity, Visual Studio, Azure CLI, etc.).
/// The first source that can successfully acquire a token will be used for all subsequent calls.
/// </para>
/// <para>
/// <b>Managed identity targeting</b><br/>
/// If <see cref="AzureCommonOptions.ManagedIdentityClientId"/> is set, that value is applied to
/// <see cref="DefaultAzureCredentialOptions.ManagedIdentityClientId"/>, allowing you to target a
/// specific user-assigned managed identity when multiple identities are available.
/// </para>
/// <para>
/// <b>Thread safety &amp; caching</b><br/>
/// A single <see cref="TokenCredential"/> instance is created lazily and cached. The lazy
/// initialization is thread-safe (<c>isThreadSafe: true</c>), avoiding unnecessary allocations and
/// ensuring consistent behavior across threads.
/// </para>
/// <para>
/// <b>Disposal</b><br/>
/// <see cref="DefaultAzureCredential"/> implements <see cref="System.IDisposable"/>. This provider
/// intentionally keeps the credential for the lifetime of the provider; in typical DI scopes
/// (e.g., a singleton), no explicit disposal is required until the container is torn down.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Register in DI as a singleton:
/// services.AddSingleton<IAzureCredentialProvider, DefaultAzureCredentialProvider>();
/// services.Configure<AzureCommonOptions>(o =>
/// {
///     // Optional: target a user-assigned managed identity by client ID
///     o.ManagedIdentityClientId = "<GUID-OF-MANAGED-IDENTITY>";
/// });
/// ]]></code>
/// /// </example>
/// /// <example>
/// /// Casting the credential to use with Azure SDK clients:
/// /// <code language="csharp"><![CDATA[
/// var provider = serviceProvider.GetRequiredService<IAzureCredentialProvider>();
/// var credential = (TokenCredential)provider.CreateCredential();
/// 
/// // Example: BlobServiceClient (Azure.Storage.Blobs)
/// var blobEndpoint = new Uri("https://myaccount.blob.core.windows.net");
/// var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(blobEndpoint, credential);
/// 
/// // Example: ServiceBusClient (Azure.Messaging.ServiceBus)
/// var sbClient = new Azure.Messaging.ServiceBus.ServiceBusClient("mybus.servicebus.windows.net", credential);
/// ]]></code>
/// </example>
/// <seealso cref="DefaultAzureCredential"/>
/// <seealso cref="DefaultAzureCredentialOptions"/>
/// <seealso cref="TokenCredential"/>
/// <seealso cref="AzureCommonOptions"/>
internal sealed class DefaultAzureCredentialProvider : IAzureCredentialProvider
{
    private readonly AzureCommonOptions _opts;
    private readonly Lazy<TokenCredential> _lazy;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAzureCredentialProvider"/> class.
    /// </summary>
    /// <param name="opts">
    /// Common Azure options used to configure credential behavior (e.g., a managed identity client ID
    /// via <see cref="AzureCommonOptions.ManagedIdentityClientId"/>).
    /// </param>
    /// <remarks>
    /// The constructor does not perform any I/O or token acquisition. The underlying
    /// <see cref="DefaultAzureCredential"/> is created on first use and reused thereafter.
    /// </remarks>
    public DefaultAzureCredentialProvider(AzureCommonOptions opts)
    {
        _opts = opts;
        _lazy = new(() =>
        {
            var o = new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = _opts.ManagedIdentityClientId
            };
            return new DefaultAzureCredential(o);
        }, isThreadSafe: true);
    }

    /// <summary>
    /// Creates (or returns a cached) <see cref="TokenCredential"/> to authenticate Azure SDK clients.
    /// </summary>
    /// <returns>
    /// A cached <see cref="TokenCredential"/> resolved via <see cref="DefaultAzureCredential"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The returned credential is safe to reuse across multiple Azure clients and threads.
    /// Token acquisition is deferred to the first time an SDK client requests a token for a given scope.
    /// </para>
    /// <para>
    /// <b>Exceptions</b><br/>
    /// This method itself does not typically throw. However, subsequent token acquisition performed by
    /// Azure SDK clients using this credential may throw <see cref="AuthenticationFailedException"/> if
    /// the environment is misconfigured (e.g., missing variables, invalid managed identity, or no
    /// interactive credential available).
    /// </para>
    /// </remarks>
    public object CreateCredential() => _lazy.Value;
}
