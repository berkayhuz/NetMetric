// <copyright file="CertificateInfo.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Abstractions;

/// <summary>
/// Immutable record describing a certificate and its key metadata used for monitoring and metrics.
/// </summary>
/// <remarks>
/// <para>
/// Serves as the common abstraction for certificates discovered from multiple sources
/// such as Windows certificate stores, PEM/CRT files, or remote TLS endpoints.
/// </para>
/// <para>
/// This type is intended for use in metrics collectors and aggregators, providing
/// consistent metadata across heterogeneous sources. All date-time values are normalized
/// to UTC.
/// </para>
/// <para>
/// Being an immutable record, <see cref="CertificateInfo"/> instances are thread-safe
/// and can be safely shared across multiple collectors and exporters.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Example: Create a record manually (for testing or mock source)
/// var cert = new CertificateInfo(
///     Id: "ABC123DEF456",
///     Subject: "CN=example.com",
///     Issuer: "CN=ExampleCA",
///     NotBeforeUtc: DateTime.UtcNow.AddMonths(-1),
///     NotAfterUtc: DateTime.UtcNow.AddMonths(11),
///     Algorithm: "RSA",
///     HasPrivateKey: true,
///     Source: "File",
///     Path: @"C:\certs\example.com.pfx",
///     HostName: "example.com"
/// );
/// 
/// Console.WriteLine($"{cert.Subject} expires at {cert.NotAfterUtc:u}");
/// ]]></code>
/// </example>
/// <param name="Id">
/// Stable identifier of the certificate. Typically the thumbprint (for store-based certificates)
/// or the absolute file path (for file-based sources). Must be unique within an aggregation scope.
/// </param>
/// <param name="Subject">
/// Subject distinguished name (DN) of the certificate, e.g. <c>CN=example.com</c>.
/// </param>
/// <param name="Issuer">
/// Issuer distinguished name (DN) of the certificate, e.g. <c>CN=ExampleCA</c>.
/// </param>
/// <param name="NotBeforeUtc">
/// UTC timestamp from which the certificate becomes valid.
/// </param>
/// <param name="NotAfterUtc">
/// UTC timestamp at which the certificate expires and is no longer valid.
/// </param>
/// <param name="Algorithm">
/// Cryptographic algorithm used for the certificate, e.g. <c>RSA</c>, <c>ECDSA</c>.
/// </param>
/// <param name="HasPrivateKey">
/// Indicates whether the certificate has an associated private key. 
/// This is <see langword="true"/> for client/server certificates that can perform signing/decryption.
/// </param>
/// <param name="Source">
/// Logical source of the certificate, e.g. <c>"Store"</c>, <c>"File"</c>, or <c>"Endpoint"</c>.
/// </param>
/// <param name="StoreName">
/// Name of the Windows certificate store when the source is <c>"Store"</c>, e.g. <c>"My"</c>.
/// Null for non-store sources.
/// </param>
/// <param name="StoreLocation">
/// Location of the Windows certificate store when the source is <c>"Store"</c>,
/// e.g. <c>"CurrentUser"</c> or <c>"LocalMachine"</c>. Null otherwise.
/// </param>
/// <param name="Path">
/// Path of the certificate file when the source is file-based. Null otherwise.
/// </param>
/// <param name="HostName">
/// Associated host name (SNI/endpoint) when available, e.g. from a TLS endpoint probe. Null otherwise.
/// </param>
public sealed record CertificateInfo(
    string Id,
    string Subject,
    string Issuer,
    DateTime NotBeforeUtc,
    DateTime NotAfterUtc,
    string Algorithm,
    bool HasPrivateKey,
    string Source,
    string? StoreName = null,
    string? StoreLocation = null,
    string? Path = null,
    string? HostName = null
);
