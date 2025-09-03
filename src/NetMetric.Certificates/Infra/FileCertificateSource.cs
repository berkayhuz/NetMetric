// <copyright file="FileCertificateSource.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Infra;

/// <summary>
/// Provides a file-based <see cref="ICertificateSource"/> that yields <see cref="CertificateInfo"/> from
/// concrete filesystem paths.
/// </summary>
/// <remarks>
/// <para>
/// Supported file types: <c>.cer</c>, <c>.crt</c>, <c>.pem</c>, <c>.der</c>, <c>.pfx</c>, and <c>.p12</c>.
/// For PFX/P12 payloads the password is obtained via <see cref="CertificatesOptions.PasswordProvider"/>; if
/// no provider is configured, a <see cref="System.Security.Cryptography.CryptographicException"/> is thrown.
/// </para>
/// <para>
/// When supplied, <see cref="CertificatesOptions"/> is enforced for:
/// <list type="bullet">
///   <item><description><see cref="CertificatesOptions.AllowedDirectories"/> (path allow-list)</description></item>
///   <item><description><see cref="CertificatesOptions.AllowedExtensions"/> (extension allow-list)</description></item>
///   <item><description><see cref="CertificatesOptions.MaxFileSizeBytes"/> (maximum file size)</description></item>
/// </list>
/// Violations are skipped and can be reported through <see cref="CertificatesOptions.LogError"/> if provided.
/// </para>
/// <para>
/// This source does not recurse directories; it expects concrete file paths. Constructor arguments may be empty,
/// in which case the enumerator yields no items. All <see cref="CertificateInfo"/> timestamps are normalized to UTC.
/// </para>
/// <threadsafety>
/// This type is thread-safe for concurrent enumeration as long as the provided
/// <see cref="CertificatesOptions"/> instance is not mutated after construction.
/// </threadsafety>
/// </remarks>
/// <example>
/// The following example reads three certificate files with default behavior:
/// <code language="csharp"><![CDATA[
/// var src = new FileCertificateSource("site.pem", "intermediate.crt", "bundle.pfx");
/// await foreach (var ci in src.EnumerateAsync())
/// {
///     Console.WriteLine($"{ci.Subject} -> expires at {ci.NotAfterUtc:u}");
/// }
/// ]]></code>
/// </example>
/// <example>
/// Enforcing policies and providing a password for PFX/P12 files:
/// <code language="csharp"><![CDATA[
/// var opts = new CertificatesOptions
/// {
///     AllowedDirectories = new[] { "/etc/ssl", "/var/certs" },
///     AllowedExtensions  = new[] { ".crt", ".pem", ".pfx" },
///     MaxFileSizeBytes   = 2 * 1024 * 1024, // 2 MB
///     PasswordProvider   = path => "p@ssw0rd".AsMemory(), // use a secure source in production
///     LogError           = (msg, ex) => logger.LogWarning(ex, msg)
/// };
///
/// var src = new FileCertificateSource(opts, "/etc/ssl/site.pfx", "/var/certs/leaf.crt");
/// await foreach (var ci in src.EnumerateAsync(ct))
/// {
///     // consume ci
/// }
/// ]]></code>
/// </example>
public sealed class FileCertificateSource : ICertificateSource
{
    /// <summary>
    /// Absolute or relative file paths to probe for certificates.
    /// </summary>
    private readonly string[] _paths;

    /// <summary>
    /// Optional behavior and policy configuration. When <c>null</c>, built-in defaults apply.
    /// </summary>
    private readonly CertificatesOptions? _options;

    /// <summary>
    /// Default allow-list for certificate file extensions when options do not provide an explicit list.
    /// </summary>
    private static readonly HashSet<string> DefaultExtSet =
        new(CertificatesOptions.DefaultAllowedExtensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="FileCertificateSource"/> class with default behavior
    /// and the specified file paths.
    /// </summary>
    /// <param name="paths">Concrete certificate file paths to read. May be empty.</param>
    /// <remarks>
    /// When no <see cref="CertificatesOptions"/> is supplied, the default allowed extensions are taken from
    /// <see cref="CertificatesOptions.DefaultAllowedExtensions"/> and size/directory checks are not enforced.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var src = new FileCertificateSource("a.pem", "b.crt");
    /// ]]></code>
    /// </example>
    public FileCertificateSource(params string[] paths)
    {
        _paths = paths ?? Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileCertificateSource"/> class with explicit options
    /// and the specified file paths.
    /// </summary>
    /// <param name="options">Certificate enumeration options and policies; must not be <c>null</c>.</param>
    /// <param name="paths">Concrete certificate file paths to read. May be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var src = new FileCertificateSource(myOptions, "/etc/ssl/site.pfx");
    /// ]]></code>
    /// </example>
    public FileCertificateSource(CertificatesOptions options, params string[] paths)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _paths = paths ?? Array.Empty<string>();
    }

    /// <summary>
    /// Asynchronously enumerates certificate metadata parsed from the configured files.
    /// </summary>
    /// <param name="ct">Cancellation token to abort enumeration.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> that yields a <see cref="CertificateInfo"/> for each successfully
    /// loaded file. Files that fail validation or parsing are skipped; errors may be routed to
    /// <see cref="CertificatesOptions.LogError"/>.
    /// </returns>
    /// <exception cref="OperationCanceledException">Enumeration was canceled via <paramref name="ct"/>.</exception>
    /// <remarks>
    /// <para>
    /// Per-file validation steps:
    /// </para>
    /// <list type="number">
    ///   <item><description>Path exists and is non-empty.</description></item>
    ///   <item><description>Under an allowed directory when <see cref="CertificatesOptions.AllowedDirectories"/> is set.</description></item>
    ///   <item><description>Extension allowed via <see cref="CertificatesOptions.AllowedExtensions"/> or the default allow-list.</description></item>
    ///   <item><description>File size is within <see cref="CertificatesOptions.MaxFileSizeBytes"/> when provided.</description></item>
    /// </list>
    /// <para>
    /// PFX/P12 files require a non-null <see cref="CertificatesOptions.PasswordProvider"/>. The password is
    /// materialized through a short-lived <see cref="PasswordOwner"/> and the backing buffer is wiped on dispose.
    /// </para>
    /// <para>
    /// Certificates are loaded via <c>X509CertificateLoader</c> APIs using
    /// <see cref="X509KeyStorageFlags.EphemeralKeySet"/> to avoid persisting private keys to disk.
    /// </para>
    /// </remarks>
    public async IAsyncEnumerable<CertificateInfo> EnumerateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var p in _paths)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
                continue;

            // --- Allow-Directories ---
            if (_options is { AllowedDirectories: { Count: > 0 } allowedDirs } &&
                !IsUnderAllowedDirectory(p, allowedDirs))
            {
                _options.LogError?.Invoke($"[CertFileSkip] '{p}' not under any allowed directory.", null);
                continue;
            }

            // --- Allowed-Extensions ---
            var ext = Path.GetExtension(p);
            if (!IsAllowedExtension(ext, _options?.AllowedExtensions))
            {
                _options?.LogError?.Invoke($"[CertFileSkip] '{p}' extension '{ext}' not allowed.", null);
                continue;
            }

            // --- Size limit ---
            if (_options is { MaxFileSizeBytes: { } max } && max > 0)
            {
                try
                {
                    var len = new FileInfo(p).Length;
                    if (len > max)
                    {
                        _options.LogError?.Invoke(
                            $"[CertFileSkip] '{p}' exceeds max size {max} bytes (actual {len}).", null);

                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _options?.LogError?.Invoke($"[CertFileError] Can't stat file '{p}': {ex.Message}", ex);

                    throw;
                }
            }

            X509Certificate2? cert = null;
            CertificateInfo? info = null;

            try
            {
                // PFX/P12 branch
                if (ext.Equals(".pfx", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".p12", StringComparison.OrdinalIgnoreCase))
                {
                    if (_options?.PasswordProvider is { } pwdProvider)
                    {
                        using var pwdOwner = new PasswordOwner(pwdProvider(p));

                        // .NET recommendation: use X509CertificateLoader instead of constructors/Import
                        cert = X509CertificateLoader.LoadPkcs12FromFile(
                            p,
                            pwdOwner.PasswordString,
                            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
                    }
                    else
                    {
                        throw new CryptographicException("PFX password required but PasswordProvider is null.");
                    }
                }
                else
                {
                    // PEM/CRT/CER/DER – use loader
                    cert = X509CertificateLoader.LoadCertificateFromFile(p);
                }

                info = new CertificateInfo(
                    Id: cert.Thumbprint ?? p,
                    Subject: cert.Subject,
                    Issuer: cert.Issuer,
                    NotBeforeUtc: cert.NotBefore.ToUniversalTime(),
                    NotAfterUtc: cert.NotAfter.ToUniversalTime(),
                    Algorithm: cert.SignatureAlgorithm?.FriendlyName ?? "unknown",
                    HasPrivateKey: cert.HasPrivateKey,
                    Source: "File",
                    Path: p);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (CryptographicException ex)
            {
                _options?.LogError?.Invoke($"[CertFileError] Cryptographic error for '{p}': {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _options?.LogError?.Invoke($"[CertFileError] Unexpected error for '{p}': {ex.Message}", ex);
                throw; // rethrow to satisfy "catch more specific or rethrow"
            }
            finally
            {
                cert?.Dispose();
            }

            if (info is not null)
            {
                yield return info;

                // Ensure there's at least one await in the async iterator (fair scheduling & analyzer appeasement)
                await Task.Yield();
            }
        }
    }

    /// <summary>
    /// Determines whether <paramref name="path"/> resides under any of the <paramref name="allowed"/> directories.
    /// </summary>
    /// <param name="path">The file path to validate.</param>
    /// <param name="allowed">A list of allowed root directories.</param>
    /// <returns><c>true</c> if the file is under one of the allowed roots; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Comparison is case-insensitive and normalizes trailing separators to reduce false negatives.
    /// </remarks>
    private static bool IsUnderAllowedDirectory(string path, IReadOnlyList<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);

        var fullPath = Path.GetFullPath(path);
        foreach (var dir in allowed)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;

            var root = Path.GetFullPath(
                dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar);

            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Determines whether a file extension is permitted, using either the explicit allow-list from options
    /// or the built-in default allow-list.
    /// </summary>
    /// <param name="ext">The file extension (including the leading dot), e.g., <c>".cer"</c>.</param>
    /// <param name="allowedFromOpts">Optional custom allow-list from <see cref="CertificatesOptions.AllowedExtensions"/>.</param>
    /// <returns><c>true</c> if the extension is permitted; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// When using <paramref name="allowedFromOpts"/>, entries missing a leading dot are normalized before comparison.
    /// </remarks>
    private static bool IsAllowedExtension(string? ext, IReadOnlyList<string>? allowedFromOpts)
    {
        if (string.IsNullOrWhiteSpace(ext))
            return false;

        if (allowedFromOpts is { Count: > 0 })
        {
            foreach (var raw in allowedFromOpts)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var norm = raw.StartsWith('.') ? raw : "." + raw;
                if (ext.Equals(norm, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        return DefaultExtSet.Contains(ext);
    }

    /// <summary>
    /// Helper that owns a password buffer, materializes a string for API compatibility, and clears the buffer on dispose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="PasswordString"/> is an immutable managed string and therefore cannot be erased; however, the
    /// backing <see cref="_buffer"/> is zeroed during <see cref="Dispose"/> to reduce exposure.
    /// </para>
    /// </remarks>
    private sealed class PasswordOwner : IDisposable
    {
        /// <summary>
        /// Backing buffer that is wiped during <see cref="Dispose"/>.
        /// </summary>
        private char[]? _buffer;

        /// <summary>
        /// The materialized password string. Note: strings are immutable and cannot be wiped.
        /// </summary>
        public string PasswordString { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PasswordOwner"/> class from a
        /// <see cref="ReadOnlyMemory{T}"/> of <see cref="char"/>.
        /// </summary>
        /// <param name="rom">Password characters. May be empty.</param>
        public PasswordOwner(ReadOnlyMemory<char> rom)
        {
            if (rom.IsEmpty)
            {
                PasswordString = string.Empty;
                return;
            }
            _buffer = rom.ToArray();
            PasswordString = new string(_buffer);
        }

        /// <summary>
        /// Clears the backing buffer, if any, and releases references to allow garbage collection.
        /// </summary>
        public void Dispose()
        {
            if (_buffer is { Length: > 0 })
                Array.Clear(_buffer, 0, _buffer.Length);
            _buffer = null;
        }
    }
}
