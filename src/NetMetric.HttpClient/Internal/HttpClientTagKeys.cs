// <copyright file="HttpClientTagKeys.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.HttpClient.Internal;

/// <summary>
/// Defines the canonical tag (label) keys used for HTTP client metrics emitted by NetMetric.
/// </summary>
/// <remarks>
/// <para>
/// These keys standardize the dimensional data attached to HTTP client measurements so you can
/// filter, group, and correlate time series consistently across collectors and exporters.
/// They intentionally mirror common industry conventions for HTTP telemetry (e.g., widely used
/// semantic conventions) to keep dashboards portable and queries predictable.
/// </para>
/// <para>
/// All keys are case-sensitive and intended to be used exactly as defined. Values should be
/// normalized by the calling code where appropriate (e.g., method upper-cased, scheme normalized
/// to <c>http</c>/<c>https</c>, host casing decided by your policy).
/// </para>
/// <para>
/// Typical usage when building instruments:
/// </para>
/// <example>
/// <code language="csharp">
/// var histogram = factory
///     .Histogram(HttpClientMetricNames.PhaseDuration, "HTTP client phase duration (ms)")
///     .WithUnit("ms")
///     .WithTags(t =>
///     {
///         t.Add(HttpClientTagKeys.Host, host);          // e.g., "api.example.com"
///         t.Add(HttpClientTagKeys.Method, method);      // e.g., "GET"
///         t.Add(HttpClientTagKeys.Scheme, scheme);      // e.g., "https"
///         t.Add(HttpClientTagKeys.Phase, "connect");    // dns|connect|tls|request|response|total...
///     })
///     .Build();
/// </code>
/// </example>
/// <threadsafety>
/// The constants are immutable and thread-safe to read concurrently from any thread.
/// </threadsafety>
/// </remarks>
internal static class HttpClientTagKeys
{
    /// <summary>
    /// Destination host component of the request URL.
    /// </summary>
    /// <value>Examples: <c>"api.example.com"</c>, <c>"localhost"</c>, <c>"10.0.0.8"</c>.</value>
    /// <remarks>
    /// Prefer the authority host without port. If a non-default port is relevant, attach it via
    /// <see cref="PeerPort"/>.
    /// </remarks>
    public const string Host = "http.host";

    /// <summary>
    /// HTTP method used for the request.
    /// </summary>
    /// <value>Typical values: <c>"GET"</c>, <c>"POST"</c>, <c>"PUT"</c>, <c>"DELETE"</c>, <c>"HEAD"</c>, <c>"PATCH"</c>.</value>
    /// <remarks>For consistency, use upper-case method names.</remarks>
    public const string Method = "http.method";

    /// <summary>
    /// URI scheme of the request.
    /// </summary>
    /// <value>Common values: <c>"http"</c>, <c>"https"</c>.</value>
    /// <remarks>
    /// If your client supports other schemes, keep the raw lowercase scheme (e.g., <c>"ws"</c>/<c>"wss"</c>).
    /// </remarks>
    public const string Scheme = "url.scheme";

    /// <summary>
    /// Numeric HTTP status code attached to a response total.
    /// </summary>
    /// <value>Examples: <c>"200"</c>, <c>"404"</c>, <c>"500"</c>.</value>
    /// <remarks>
    /// Represented as a string to avoid label cardinality drift from integer-to-string conversions later.
    /// For client-side exceptions with no status code, your instrumentation may use a synthetic bucket
    /// (e.g., <c>"EXC"</c>) on the corresponding metric.
    /// </remarks>
    public const string StatusCode = "http.status_code";

    /// <summary>
    /// HTTP request lifecycle phase being measured.
    /// </summary>
    /// <value>
    /// Suggested values include <c>"dns"</c>, <c>"connect"</c>, <c>"tls"</c>, <c>"request"</c>,
    /// <c>"response"</c>, <c>"download"</c>, <c>"total"</c>.
    /// </value>
    /// <remarks>
    /// The exact set may vary by handler/observer. Keep values lowercase to simplify querying.
    /// </remarks>
    public const string Phase = "phase";

    /// <summary>
    /// HTTP protocol version used for the exchange.
    /// </summary>
    /// <value>Examples: <c>"1.1"</c>, <c>"2.0"</c>, <c>"3"</c>.</value>
    /// <remarks>
    /// If unknown, consider omitting this tag rather than sending placeholder values.
    /// </remarks>
    public const string HttpFlavor = "http.flavor";

    /// <summary>
    /// Underlying transport protocol negotiated for the connection.
    /// </summary>
    /// <value>Typical values: <c>"tcp"</c>, <c>"udp"</c>, <c>"quic"</c>.</value>
    /// <remarks>
    /// This tag is most useful when your runtime exposes transport details (e.g., HTTP/3 over QUIC).
    /// </remarks>
    public const string NetTransport = "network.transport";

    /// <summary>
    /// Peer server port number used for the connection.
    /// </summary>
    /// <value>Examples: <c>"80"</c>, <c>"443"</c>, <c>"8443"</c>.</value>
    /// <remarks>
    /// Use only when non-default ports are significant in your analysis; otherwise omit to limit cardinality.
    /// </remarks>
    public const string PeerPort = "net.peer.port";

    /// <summary>
    /// The concrete exception type observed for a failed attempt.
    /// </summary>
    /// <value>Examples: <c>"System.TimeoutException"</c>, <c>"System.Net.Sockets.SocketException"</c>.</value>
    /// <remarks>
    /// Prefer fully qualified type names. Consider sampling or coarser bucketing if exception diversity is high.
    /// </remarks>
    public const string Exception = "exception.type";

    /// <summary>
    /// A generic code for custom classification scenarios defined by the instrumentation.
    /// </summary>
    /// <value>Free-form categorical value (e.g., <c>"policy_blocked"</c>, <c>"cache_hit"</c>).</value>
    /// <remarks>
    /// Use sparingly and document producer-specific semantics; avoid high-cardinality or unbounded values.
    /// </remarks>
    public const string Code = "code";
}
