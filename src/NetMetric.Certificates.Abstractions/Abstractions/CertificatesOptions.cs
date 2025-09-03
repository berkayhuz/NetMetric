// <copyright file="CertificatesOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Abstractions;

/// <summary>
/// Defines configuration options for certificate collection and analysis.
/// </summary>
/// <remarks>
/// <para>
/// These options control how certificates are scanned, filtered, and interpreted across all
/// <see cref="ICertificateSource"/> implementations. Typical consumers pass an instance of this
/// type to the certificate module or directly to sources (e.g., file- or store-based sources)
/// and to the aggregation layer.
/// </para>
/// <para>
/// Thread safety: this type is a simple data container and is not thread-safe by itself. Construct and configure
/// an instance during application startup and treat it as immutable thereafter, or expose it via a read-only
/// options pattern.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Configure options for a production service:
/// var opts = new CertificatesOptions
/// {
///     WarningDays = 30,
///     CriticalDays = 7,
///     ScanTtl = TimeSpan.FromSeconds(30),
///     MaxConcurrentSources = Environment.ProcessorCount,
///     AllowedDirectories = new[] { "/etc/certs", "/var/certs" },
///     AllowedExtensions = CertificatesOptions.DefaultAllowedExtensions,
///     MaxFileSizeBytes = 512 * 1024, // 512 KB
///     LogError = (msg, ex) => logger.LogWarning(ex, "[Cert] {Message}", msg)
/// };
///
/// // Pass to module / aggregator / sources
/// var module = new CertificatesModule(sources, opts, metricFactory);
/// ]]></code>
/// </example>
public sealed class CertificatesOptions
{
    // backing fields to avoid repeated allocations
    private static readonly IReadOnlyList<double> DefaultBucketBounds =
        Array.AsReadOnly(new[] { 0d, 7d, 14d, 30d, 60d, 90d, 180d });

    private static readonly IReadOnlyList<string> DefaultExtensions =
        Array.AsReadOnly(new[] { ".cer", ".crt", ".pem", ".der", ".pfx", ".p12" });

    /// <summary>
    /// The number of days before expiry considered a <em>warning</em> threshold.
    /// </summary>
    /// <value>
    /// Defaults to <c>30</c>. A certificate is typically classified as "warning" when the
    /// remaining days are greater than <see cref="CriticalDays"/> but less than or equal to this value.
    /// </value>
    /// <remarks>
    /// Choose a value that matches your operational renewal lead time (e.g., 14 or 30 days).
    /// </remarks>
    public int WarningDays { get; set; } = 30;

    /// <summary>
    /// The number of days before expiry considered a <em>critical</em> threshold.
    /// </summary>
    /// <value>Defaults to <c>7</c>.</value>
    /// <remarks>
    /// Certificates at or below this threshold should be prioritized for immediate renewal.
    /// </remarks>
    public int CriticalDays { get; set; } = 7;

    /// <summary>
    /// Whether to enable default built-in sources (for example, local machine/user certificate stores).
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    /// <remarks>
    /// Set to <see langword="false"/> if you plan to provide your own explicit sources only
    /// (e.g., file paths, remote endpoints).
    /// </remarks>
    public bool UseDefaultSources { get; set; } = true;

    /// <summary>
    /// The time-to-live (TTL) duration for caching scan results.
    /// </summary>
    /// <value>Defaults to <c>00:00:30</c> (30 seconds).</value>
    /// <remarks>
    /// <para>
    /// If the value is less than or equal to <c>TimeSpan.Zero</c>, a default of 30 seconds is applied by the aggregator.
    /// </para>
    /// <para>
    /// Increase this to reduce load on sources at the cost of freshness; decrease it for more frequent rescans.
    /// </para>
    /// </remarks>
    public TimeSpan ScanTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum degree of parallelism across sources during enumeration.
    /// </summary>
    /// <value>
    /// When <see langword="null"/>, the implementation may default to the number of logical CPUs.
    /// </value>
    /// <remarks>
    /// Tune this to avoid overwhelming slow or rate-limited sources. Values &lt;= 0 are treated as <see langword="null"/>.
    /// </remarks>
    public int? MaxConcurrentSources { get; set; }

    /// <summary>
    /// Histogram bucket boundaries (in days) for the expiry distribution metric.
    /// </summary>
    /// <value>
    /// Defaults to <c>{ 0, 7, 14, 30, 60, 90, 180 }</c>.
    /// </value>
    /// <remarks>
    /// <para>
    /// Used by collectors that publish bucket histograms of days-until-expiry. Buckets should be strictly increasing.
    /// </para>
    /// <para>
    /// If empty or <see langword="null"/>, implementations fall back to the default set.
    /// </para>
    /// </remarks>
    public IReadOnlyList<double> BucketBoundsDays { get; set; } = DefaultBucketBounds;

    /// <summary>
    /// Password provider for PFX/P12 files.
    /// </summary>
    /// <value>
    /// A delegate that accepts the file path and returns the password as a <see cref="ReadOnlyMemory{T}"/> of <see cref="char"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// If no provider is specified and a PFX requires a password, the file is skipped by compliant sources.
    /// </para>
    /// <para>
    /// Prefer returning a short-lived buffer rather than a string to minimize exposure in memory.
    /// </para>
    /// </remarks>
    public Func<string, ReadOnlyMemory<char>>? PasswordProvider { get; set; }

    /// <summary>
    /// Optional error logging callback invoked when a source or file operation fails.
    /// </summary>
    /// <value>
    /// Receives an error message and an optional <see cref="Exception"/> instance.
    /// </value>
    /// <remarks>
    /// Use this to surface non-fatal issues (e.g., unreadable files) to your logging pipeline.
    /// </remarks>
    public Action<string, Exception?>? LogError { get; set; }

    /// <summary>
    /// Allow-list of directories permitted to be scanned by file-based sources.
    /// </summary>
    /// <value>
    /// If <see langword="null"/> or empty, no directory restriction is applied.
    /// </value>
    /// <remarks>
    /// Recommended in production to ensure only vetted locations are scanned.
    /// </remarks>
    public IReadOnlyList<string>? AllowedDirectories { get; set; }

    /// <summary>
    /// Maximum file size (in bytes) for scanned certificate files.
    /// </summary>
    /// <value>
    /// Defaults to <c>262144</c> (256 KB). If <see langword="null"/> or less than or equal to <c>0</c>, no size limit is applied.
    /// </value>
    /// <remarks>
    /// Helps prevent scanning arbitrarily large files that are unlikely to be valid certificates.
    /// </remarks>
    public long? MaxFileSizeBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// Allow-list of file extensions for scanned certificates (for example, <c>".cer"</c>, <c>".crt"</c>, <c>".pem"</c>, <c>".der"</c>, <c>".pfx"</c>, <c>".p12"</c>).
    /// </summary>
    /// <value>
    /// If <see langword="null"/> or empty, <see cref="DefaultAllowedExtensions"/> is used.
    /// </value>
    /// <remarks>
    /// Extension comparison is case-insensitive; entries missing a leading dot may be normalized by sources.
    /// </remarks>
    public IReadOnlyList<string>? AllowedExtensions { get; set; }

    /// <summary>
    /// Gets the default allow-list of file extensions recognized by file-based sources.
    /// </summary>
    /// <value>
    /// <c>{ ".cer", ".crt", ".pem", ".der", ".pfx", ".p12" }</c>
    /// </value>
    public static IReadOnlyList<string> DefaultAllowedExtensions => DefaultExtensions;
}
