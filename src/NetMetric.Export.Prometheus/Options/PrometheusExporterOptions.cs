// <copyright file="PrometheusExporterOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Prometheus.Options;

/// <summary>
/// Provides configuration options for the Prometheus metrics exporter.
/// </summary>
/// <remarks>
/// <para>
/// These options control how metrics are exposed using the Prometheus text exposition format,
/// covering formatting, concurrency, access control (host/network allowlists), proxy handling,
/// authentication (Basic Auth, mTLS), and per-IP rate limiting.
/// </para>
/// <para>
/// The options are designed for production deployments where the <c>/metrics</c> endpoint may be
/// internet-accessible and must be hardened against abuse and misconfiguration.
/// </para>
/// <para>
/// Unless stated otherwise, properties are immutable (<see langword="init"/>-only) and have safe defaults.
/// </para>
/// </remarks>
/// <example>
/// Example minimal registration using the Options pattern:
/// <code language="csharp"><![CDATA[
/// services.Configure<PrometheusExporterOptions>(o =>
/// {
///     o.EndpointPath = "/metrics";
///     o.IncludeMetaLines = true;
/// });
/// ]]></code>
/// </example>
/// <example>
/// Example hardened configuration (appsettings.json):
/// <code language="json">
/// {
///   "NetMetric": {
///     "Export": {
///       "Prometheus": {
///         "EndpointPath": "/metrics",
///         "IncludeMetaLines": true,
///         "AsciiOnlyMetricNames": true,
///         "MaxConcurrentScrapes": 2,
///         "AllowedHosts": [ "metrics.example.com" ],
///         "AllowedNetworks": [ "10.0.0.0/8", "fd00::/8" ],
///         "RequireBasicAuth": true,
///         "BasicAuthUsername": "metrics",
///         "BasicAuthPassword": "change-me",
///         "RequireClientCertificate": true,
///         "AllowedClientThumbprints": [ "‎ABCD1234..." ],
///         "TrustedProxyNetworks": [ "192.0.2.0/24" ],
///         "ExpectedForwardedProto": "https",
///         "EnforceForwardedHostWithAllowList": true,
///         "RateLimitBucketCapacity": 60,
///         "RateLimitRefillRatePerSecond": 30.0,
///         "RateLimitReturn503": false,
///         "ExporterMetricsEnabled": true,
///         "ExporterMetricsPrefix": "netmetric_exporter"
///       }
///     }
///   }
/// }
/// </code>
/// </example>
public sealed class PrometheusExporterOptions
{
    /// <summary>
    /// Gets the HTTP endpoint path where metrics are exposed.
    /// </summary>
    /// <value>Defaults to <c>"/metrics"</c>.</value>
    /// <remarks>
    /// Must be a rooted path starting with <c>"/"</c>. If the hosting stack applies a path base,
    /// the effective URL becomes <c>{PathBase}{EndpointPath}</c>.
    /// </remarks>
    public string EndpointPath { get; init; } = "/metrics";

    /// <summary>
    /// Gets a value indicating whether <c># HELP</c> and <c># TYPE</c> meta lines are included.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    /// <remarks>
    /// Disabling meta lines reduces response size but may impair scrapers or tooling that rely on them.
    /// </remarks>
    public bool IncludeMetaLines { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether UNIX epoch timestamps (in milliseconds) are appended to samples.
    /// </summary>
    /// <value>Defaults to <see langword="false"/>.</value>
    /// <remarks>
    /// Prometheus typically assigns scrape time; enabling explicit timestamps is rarely necessary and can
    /// complicate staleness semantics.
    /// </remarks>
    public bool IncludeTimestamps { get; init; }

    /// <summary>
    /// Gets the maximum length of label values. Values longer than this are truncated.
    /// </summary>
    /// <value>Defaults to <c>256</c>.</value>
    /// <remarks>
    /// Truncation helps bound response size and cardinality explosions caused by untrusted inputs.
    /// </remarks>
    public int LabelMaxLength { get; init; } = 256;

    /// <summary>
    /// Gets a value indicating whether metric names should be restricted to ASCII only.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    /// <remarks>
    /// When <see langword="true"/>, non-ASCII characters are sanitized to meet Prometheus
    /// naming rules (<c>[a-zA-Z_:][a-zA-Z0-9_:]*</c>).
    /// </remarks>
    public bool AsciiOnlyMetricNames { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether <c># HELP</c> text is derived from metric descriptions.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    /// <remarks>
    /// If a metric has no description, the HELP line may be omitted or synthesized depending on the formatter.
    /// </remarks>
    public bool DeriveHelpFromDescription { get; init; } = true;

    /// <summary>
    /// Gets the number of decimal digits used in quantile label formatting (e.g., <c>quantile="0.99"</c>).
    /// </summary>
    /// <value>Defaults to <c>2</c>.</value>
    public int QuantileLabelPrecision { get; init; } = 2;

    /// <summary>
    /// Gets the maximum number of concurrent Prometheus scrapes allowed.
    /// </summary>
    /// <value>Defaults to <c>1</c>.</value>
    /// <remarks>
    /// Excess concurrent requests are rejected (e.g., with <c>503</c> or <c>429</c> depending on rate-limit settings).
    /// Increasing this value may increase memory/CPU pressure during scrapes.
    /// </remarks>
    public int MaxConcurrentScrapes { get; init; } = 1;

    /// <summary>
    /// Gets the allowlist of hostnames permitted to access the exporter.
    /// </summary>
    /// <remarks>
    /// <para>Set to <see langword="null"/> to disable hostname allowlisting.</para>
    /// <para>Comparison is typically case-insensitive and performed against the effective host (considering proxies when enabled).</para>
    /// </remarks>
    public Collection<string>? AllowedHosts { get; init; }

    /// <summary>
    /// Gets the allowlist of networks (IPv4/IPv6 CIDR) permitted to access the exporter.
    /// </summary>
    /// <remarks>Set to <see langword="null"/> to disable network allowlisting.</remarks>
    public Collection<string>? AllowedNetworks { get; init; }

    /// <summary>
    /// Gets a value indicating whether HTTP Basic Authentication is required.
    /// </summary>
    /// <value>Defaults to <see langword="false"/>.</value>
    /// <remarks>
    /// If enabled, either <see cref="BasicAuthUsername"/>/<see cref="BasicAuthPassword"/> or
    /// <see cref="BasicAuthValidator"/> must be supplied.
    /// </remarks>
    public bool RequireBasicAuth { get; init; }

    /// <summary>
    /// Gets the expected Basic Auth username, if authentication is enabled via <see cref="RequireBasicAuth"/>.
    /// </summary>
    public string? BasicAuthUsername { get; init; }

    /// <summary>
    /// Gets the expected Basic Auth password, if authentication is enabled via <see cref="RequireBasicAuth"/>.
    /// </summary>
    /// <remarks>Store secrets securely (e.g., user secrets, environment variables, or a vault), not in source control.</remarks>
    public string? BasicAuthPassword { get; init; }

    /// <summary>
    /// Gets a custom validation delegate for Basic Auth credentials.
    /// </summary>
    /// <value>
    /// A delegate <c>Func&lt;string username, string password, bool&gt;</c> that returns
    /// <see langword="true"/> to accept the credentials; otherwise <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// When provided, this takes precedence over <see cref="BasicAuthUsername"/> and <see cref="BasicAuthPassword"/>.
    /// </remarks>
    public Func<string, string, bool>? BasicAuthValidator { get; init; }

    /// <summary>
    /// Gets a value indicating whether a valid client certificate is required (mTLS).
    /// </summary>
    /// <value>Defaults to <see langword="false"/>.</value>
    /// <remarks>
    /// When enabled, requests must present a client certificate that matches at least one of the
    /// configured pinning/issuer allowlists.
    /// </remarks>
    public bool RequireClientCertificate { get; init; }

    /// <summary>
    /// Gets the allowlist of permitted client certificate thumbprints.
    /// </summary>
    /// <remarks>Thumbprints are typically SHA-1 hex strings without separators.</remarks>
    public Collection<string>? AllowedClientThumbprints { get; init; }

    /// <summary>
    /// Gets the allowlist of permitted client certificate issuers (distinguished names).
    /// </summary>
    public Collection<string>? AllowedClientIssuers { get; init; }

    /// <summary>
    /// Gets the allowlist of permitted Subject Public Key Info (SPKI) SHA-256 pins (Base64 encoded).
    /// </summary>
    /// <remarks>Values should be Base64 without padding, as produced by common pinning tools.</remarks>
    public Collection<string>? AllowedSpkiSha256PinsBase64 { get; init; }

    /// <summary>
    /// Gets the trusted proxy networks (CIDR) from which proxy headers are accepted.
    /// </summary>
    /// <remarks>
    /// Requests originating from these networks are allowed to supply and influence
    /// <c>X-Forwarded-*</c> / <c>Forwarded</c> headers during host/proto validation.
    /// </remarks>
    public Collection<string>? TrustedProxyNetworks { get; init; }

    /// <summary>
    /// Gets the expected <c>Forwarded: proto=</c> value (e.g., <c>"https"</c>).
    /// </summary>
    /// <remarks>
    /// When set, requests advertising a different protocol via proxy headers are rejected.
    /// Useful when TLS termination happens upstream.
    /// </remarks>
    public string? ExpectedForwardedProto { get; init; }

    /// <summary>
    /// Gets a value indicating whether forwarded host headers must match the host allowlist.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    /// <remarks>
    /// Helps prevent host-header attacks in proxied environments by enforcing <see cref="AllowedHosts"/> against
    /// <c>X-Forwarded-Host</c>/<c>Forwarded</c> data from trusted proxies.
    /// </remarks>
    public bool EnforceForwardedHostWithAllowList { get; init; } = true;

    /// <summary>
    /// Gets the token bucket capacity for per-IP rate limiting.
    /// </summary>
    /// <value>Defaults to <c>60</c>.</value>
    /// <remarks>
    /// The bucket holds up to this many tokens; each request consumes one. See also
    /// <see cref="RateLimitRefillRatePerSecond"/>.
    /// </remarks>
    public int RateLimitBucketCapacity { get; init; } = 60;

    /// <summary>
    /// Gets the refill rate (tokens per second) for the per-IP rate limit bucket.
    /// </summary>
    /// <value>Defaults to <c>30.0</c>.</value>
    public double RateLimitRefillRatePerSecond { get; init; } = 30.0;

    /// <summary>
    /// Gets a value indicating whether a rate-limited request should return HTTP 503 (Service Unavailable)
    /// instead of 429 (Too Many Requests).
    /// </summary>
    /// <value>Defaults to <see langword="false"/>.</value>
    /// <remarks>
    /// Returning <c>503</c> can discourage aggressive retries by some clients but may affect monitoring alerts.
    /// </remarks>
    public bool RateLimitReturn503 { get; init; }

    /// <summary>
    /// Gets a value indicating whether the exporter should expose its own operational metrics
    /// (e.g., scrape durations, status codes, rate-limit counters).
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool ExporterMetricsEnabled { get; init; } = true;

    /// <summary>
    /// Gets the prefix applied to exporter self-metrics.
    /// </summary>
    /// <value>Defaults to <c>"netmetric_exporter"</c>.</value>
    /// <remarks>Ensure the prefix is a valid Prometheus metric name prefix.</remarks>
    public string ExporterMetricsPrefix { get; init; } = "netmetric_exporter";

    /// <summary>
    /// Returns <see cref="AllowedHosts"/> or an empty read-only collection when it is <see langword="null"/> or empty.
    /// </summary>
    /// <remarks>For internal use to simplify checks without repeated <see langword="null"/>/length guards.</remarks>
    internal ReadOnlyCollection<string> AllowedHostsOrEmpty =>
        AllowedHosts?.Count > 0 ? new ReadOnlyCollection<string>(AllowedHosts) : Empty;

    /// <summary>
    /// Returns <see cref="AllowedNetworks"/> or an empty read-only collection when it is <see langword="null"/> or empty.
    /// </summary>
    internal ReadOnlyCollection<string> AllowedNetworksOrEmpty =>
        AllowedNetworks?.Count > 0 ? new ReadOnlyCollection<string>(AllowedNetworks) : Empty;

    /// <summary>
    /// Returns <see cref="TrustedProxyNetworks"/> or an empty read-only collection when it is <see langword="null"/> or empty.
    /// </summary>
    internal ReadOnlyCollection<string> TrustedProxyNetworksOrEmpty =>
        TrustedProxyNetworks?.Count > 0 ? new ReadOnlyCollection<string>(TrustedProxyNetworks) : Empty;

    /// <summary>
    /// Returns <see cref="AllowedClientThumbprints"/> or an empty read-only collection when it is <see langword="null"/> or empty.
    /// </summary>
    internal ReadOnlyCollection<string> AllowedClientThumbprintsOrEmpty =>
        AllowedClientThumbprints?.Count > 0 ? new ReadOnlyCollection<string>(AllowedClientThumbprints) : Empty;

    /// <summary>
    /// Returns <see cref="AllowedClientIssuers"/> or an empty read-only collection when it is <see langword="null"/> or empty.
    /// </summary>
    internal ReadOnlyCollection<string> AllowedClientIssuersOrEmpty =>
        AllowedClientIssuers?.Count > 0 ? new ReadOnlyCollection<string>(AllowedClientIssuers) : Empty;

    /// <summary>
    /// Returns <see cref="AllowedSpkiSha256PinsBase64"/> or an empty read-only collection when it is <see langword="null"/> or empty.
    /// </summary>
    internal ReadOnlyCollection<string> AllowedSpkiPinsOrEmpty =>
        AllowedSpkiSha256PinsBase64?.Count > 0 ? new ReadOnlyCollection<string>(AllowedSpkiSha256PinsBase64) : Empty;

    /// <summary>
    /// Shared empty read-only collection instance to avoid allocations.
    /// </summary>
    private static readonly ReadOnlyCollection<string> Empty =
        new ReadOnlyCollection<string>(Array.Empty<string>());
}
