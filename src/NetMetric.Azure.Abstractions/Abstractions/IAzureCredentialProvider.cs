// <copyright file="IAzureCredentialProvider.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Azure.Abstractions;

/// <summary>
/// Defines an abstraction for providing Azure credentials to adapters and collectors.
/// </summary>
/// <remarks>
/// <para>
/// The return type of <see cref="CreateCredential"/> is intentionally declared as <see cref="object"/>
/// to avoid introducing direct dependencies on the Azure SDK (e.g., <c>Azure.Core.TokenCredential</c>)
/// in higher-level abstractions.
/// </para>
/// <para>
/// Implementations are expected to return a <c>Azure.Core.TokenCredential</c> (or a compatible object)
/// usable by Azure SDK clients. Consumers of this interface should cast the returned value to the
/// expected type when interacting with Azure SDK components.
/// </para>
/// </remarks>
/// <example>
/// Example implementation using <c>Azure.Identity.DefaultAzureCredential</c>:
/// <code><![CDATA[
/// using Azure.Core;
/// using Azure.Identity;
///
/// internal sealed class DefaultAzureCredentialProvider : IAzureCredentialProvider
/// {
///     private readonly TokenCredential _credential = new DefaultAzureCredential();
///
///     public object CreateCredential() => _credential;
/// }
///
/// // Usage in an adapter:
/// var provider = new DefaultAzureCredentialProvider();
/// var credential = (TokenCredential)provider.CreateCredential();
/// var client = new Azure.Messaging.ServiceBus.ServiceBusClient("namespace.servicebus.windows.net", credential);
/// ]]></code>
/// </example>
public interface IAzureCredentialProvider
{
    /// <summary>
    /// Creates and returns an Azure credential object that can be used by Azure SDK clients for authentication.
    /// </summary>
    /// <returns>
    /// An object representing the credential, typically an instance of <c>Azure.Core.TokenCredential</c>.
    /// </returns>
    object CreateCredential(); // Returns TokenCredential, wrapped as object to hide SDK dependency
}
