// <copyright file="ICertificateSource.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Abstractions;

/// <summary>
/// Defines an abstraction for asynchronously enumerating certificates from a concrete provider.
/// </summary>
/// <remarks>
/// <para>
/// Implementations can fetch certificates from a variety of backends (e.g., local file system,
/// X.509 stores, secret managers, cloud KMS/HSM). The asynchronous contract allows providers
/// to stream results and to avoid blocking I/O threads during potentially slow operations.
/// </para>
/// <para>
/// Enumeration SHOULD be resilient to per-item failures: a single unreadable or malformed
/// certificate SHOULD NOT abort the entire sequence unless the provider's policy dictates otherwise.
/// Providers are encouraged to log errors and continue yielding remaining items when safe.
/// </para>
/// <para>
/// Thread-safety: unless otherwise documented by an implementation, instances are not guaranteed
/// to be thread-safe. Create separate instances per concurrent enumeration if needed.
/// </para>
/// </remarks>
/// <example>
/// The following example aggregates subjects from all certificates emitted by a source:
/// <code language="csharp"><![CDATA[
/// ICertificateSource source = new FileCertificateSource(options, "c1.cer", "c2.pem");
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
///
/// var subjects = new List<string>();
/// await foreach (var cert in source.EnumerateAsync(cts.Token))
/// {
///     subjects.Add(cert.Subject);
/// }
/// Console.WriteLine($"Enumerated {subjects.Count} certificates.");
/// ]]></code>
/// </example>
/// <seealso cref="CertificateInfo"/>
/// <seealso cref="System.Threading.CancellationToken"/>
/// <seealso cref="IAsyncEnumerable{T}"/>
public interface ICertificateSource
{
    /// <summary>
    /// Asynchronously enumerates the certificates available from this source.
    /// </summary>
    /// <param name="ct">
    /// A <see cref="CancellationToken"/> that can be observed to cancel the enumeration early.
    /// </param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> sequence of <see cref="CertificateInfo"/> instances that
    /// represent the certificates exposed by the provider.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Implementations should:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Honor <paramref name="ct"/> promptly to support responsive shutdown.</description></item>
    ///   <item><description>Normalize times in <see cref="CertificateInfo"/> to UTC for consistency.</description></item>
    ///   <item><description>Prefer streaming (yielding items as they are discovered) over buffering all results.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown if enumeration is canceled via <paramref name="ct"/>.
    /// </exception>
    IAsyncEnumerable<CertificateInfo> EnumerateAsync(CancellationToken ct = default);
}
