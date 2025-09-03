// <copyright file="X509StoreCertificateSource.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Infra;

/// <summary>
/// Certificate source that reads certificates directly from an <see cref="X509Store"/>.
/// </summary>
/// <remarks>
/// <para>
/// By default, the constructor targets <see cref="StoreName.My"/> in the
/// <see cref="StoreLocation.CurrentUser"/> location.
/// </para>
/// <para>
/// Consumers can override these parameters to enumerate from other system stores,
/// for example <see cref="StoreName.Root"/> in <see cref="StoreLocation.LocalMachine"/>.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Example: Enumerate certificates from the CurrentUser\My store.
/// var source = new X509StoreCertificateSource();
/// await foreach (var cert in source.EnumerateAsync())
/// {
///     Console.WriteLine($"{cert.Subject} expires {cert.NotAfterUtc}");
/// }
///
/// // Example: Enumerate trusted root certificates from LocalMachine\Root.
/// var roots = new X509StoreCertificateSource(StoreName.Root, StoreLocation.LocalMachine);
/// await foreach (var rootCert in roots.EnumerateAsync())
/// {
///     Console.WriteLine($"{rootCert.Subject} issued by {rootCert.Issuer}");
/// }
/// ]]></code>
/// </example>
public sealed class X509StoreCertificateSource : ICertificateSource
{
    private readonly StoreName _storeName;
    private readonly StoreLocation _location;

    /// <summary>
    /// Initializes a new instance of the <see cref="X509StoreCertificateSource"/> class
    /// with the specified store name and location.
    /// </summary>
    /// <param name="storeName">The X.509 certificate store name (defaults to <see cref="StoreName.My"/>).</param>
    /// <param name="location">The X.509 store location (defaults to <see cref="StoreLocation.CurrentUser"/>).</param>
    /// <remarks>
    /// Supported store names include <see cref="StoreName.My"/>, <see cref="StoreName.Root"/>,
    /// <see cref="StoreName.CertificateAuthority"/>, etc. Store location can be
    /// <see cref="StoreLocation.CurrentUser"/> or <see cref="StoreLocation.LocalMachine"/>.
    /// </remarks>
    public X509StoreCertificateSource(
        StoreName storeName = StoreName.My,
        StoreLocation location = StoreLocation.CurrentUser)
    {
        _storeName = storeName;
        _location = location;
    }

    /// <summary>
    /// Asynchronously enumerates certificates from the configured <see cref="X509Store"/>.
    /// </summary>
    /// <param name="ct">A cancellation token to abort enumeration.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> sequence of <see cref="CertificateInfo"/> objects
    /// representing the certificates in the target store.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The store is opened in read-only mode and closed automatically when enumeration completes.
    /// </para>
    /// <para>
    /// Consumers can add filtering logic before yielding items if only specific types of
    /// certificates (e.g., server authentication certs) should be included.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Enumeration is canceled via <paramref name="ct"/>.</exception>
    public async IAsyncEnumerable<CertificateInfo> EnumerateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var store = new X509Store(_storeName, _location);
        store.Open(OpenFlags.ReadOnly);
        try
        {
            foreach (var cert in store.Certificates)
            {
                ct.ThrowIfCancellationRequested();
                // Optional: Apply filtering logic if only valid server certs should be included.
                yield return ToInfo(cert, _storeName.ToString(), _location.ToString());
                await Task.Yield();
            }
        }
        finally { store.Close(); }
    }

    /// <summary>
    /// Converts an <see cref="X509Certificate2"/> to a <see cref="CertificateInfo"/> with metadata.
    /// </summary>
    /// <param name="c">The certificate instance to convert.</param>
    /// <param name="store">The store name where the certificate was found.</param>
    /// <param name="loc">The store location where the certificate was found.</param>
    /// <returns>A populated <see cref="CertificateInfo"/> representing the certificate.</returns>
    /// <remarks>
    /// <para>
    /// The resulting <see cref="CertificateInfo"/> includes fields such as:
    /// <c>Id</c> (thumbprint or serial number), <c>Subject</c>, <c>Issuer</c>,
    /// validity period (<c>NotBeforeUtc</c>, <c>NotAfterUtc</c>), signature algorithm,
    /// whether it has a private key, and store metadata.
    /// </para>
    /// <para>
    /// If both thumbprint and serial number are unavailable, a random GUID is used as the identifier.
    /// </para>
    /// </remarks>
    private static CertificateInfo ToInfo(X509Certificate2 c, string store, string loc)
    {
        ArgumentNullException.ThrowIfNull(c);

        return new CertificateInfo(
            Id: c.Thumbprint ?? c.SerialNumber ?? Guid.NewGuid().ToString("N"),
            Subject: c.Subject,
            Issuer: c.Issuer,
            NotBeforeUtc: c.NotBefore.ToUniversalTime(),
            NotAfterUtc: c.NotAfter.ToUniversalTime(),
            Algorithm: c.SignatureAlgorithm?.FriendlyName ?? "unknown",
            HasPrivateKey: c.HasPrivateKey,
            Source: "Store",
            StoreName: store,
            StoreLocation: loc);
    }
}
