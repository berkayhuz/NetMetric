using System.Net;
using Microsoft.AspNetCore.Http;

namespace NetMetric.Export.Prometheus.AspNetCore.Util;

/// <summary>
/// Provides safe helper methods for extracting client-facing values from proxy-related HTTP headers
/// such as <c>X-Forwarded-*</c> and the RFC <c>Forwarded</c> header.
/// </summary>
/// <remarks>
/// <para>
/// Many production environments terminate TLS and perform request routing at one or more reverse proxies
/// (e.g., Nginx, HAProxy, AWS ALB). In such topologies, the immediate <see cref="HttpContext.Connection"/>
/// may reflect the proxy rather than the original client. This utility reads <c>X-Forwarded-For</c>,
/// <c>X-Forwarded-Proto</c>, <c>X-Forwarded-Host</c>, and/or the standardized <c>Forwarded</c> header
/// to recover client-facing attributes <em>only when</em> the request is known to arrive from a
/// <strong>trusted proxy</strong>.
/// </para>
/// <para>
/// Trust is enforced by checking whether the remote IP belongs to any of the configured CIDR blocks.
/// If the connection is not from a trusted proxy, the methods fall back to values directly exposed by
/// <see cref="HttpContext"/> (e.g., <see cref="ConnectionInfo.RemoteIpAddress"/>), ignoring
/// potentially spoofed headers. This prevents header-injection attacks by untrusted clients.
/// </para>
/// <para>
/// All methods in this type are thread-safe. The class is <c>internal</c> and static by design—no
/// instance state is kept.
/// </para>
/// <para>
/// <strong>Header precedence</strong>:
/// <list type="number">
///   <item><description>Prefer vendor headers (<c>X-Forwarded-*</c>) when present.</description></item>
///   <item><description>Otherwise examine the standardized <c>Forwarded</c> header (RFC 7239).</description></item>
///   <item><description>As a last resort, use connection-local values (e.g., <see cref="ConnectionInfo.RemoteIpAddress"/>).</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Security note:</strong> Only enable forwarding logic for networks you control. Treat all header
/// values as untrusted input unless proven otherwise. This class applies basic normalization but does not
/// perform DNS lookups or hostname verification.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Example usage inside middleware:
/// if (ProxyHeaders.TryGetClientIp(context, options.TrustedProxyCidrs, out var clientIp))
/// {
///     // Use clientIp for logging, rate-limiting, etc.
/// }
///
/// var scheme = ProxyHeaders.TryGetForwardedProto(context, options.TrustedProxyCidrs)
///              ?? context.Request.Scheme;
///
/// var host = ProxyHeaders.TryGetForwardedHost(context, options.TrustedProxyCidrs)
///            ?? context.Request.Host.Value;
/// ]]></code>
/// </example>
/// <seealso href="https://www.rfc-editor.org/rfc/rfc7239">RFC 7239: Forwarded HTTP Extension</seealso>
internal static class ProxyHeaders
{
    /// <summary>
    /// Attempts to resolve the original client IP address, taking into account <c>X-Forwarded-For</c> and
    /// <c>Forwarded</c> headers <em>only</em> when the connection originates from a trusted proxy network.
    /// </summary>
    /// <param name="ctx">The current HTTP context that contains the request and connection information.</param>
    /// <param name="trustedProxyCidrs">
    /// A set of CIDR blocks (IPv4 and/or IPv6) that identify trusted proxy networks (e.g., <c>"10.0.0.0/8"</c>, <c>"fd00::/8"</c>).
    /// When <see langword="null"/> or empty, the method treats the connection as untrusted and ignores forwarding headers.
    /// </param>
    /// <param name="ip">When this method returns, contains the resolved client IP address on success; otherwise <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> if an IP address could be resolved (either from forwarding headers or from
    /// <see cref="ConnectionInfo.RemoteIpAddress"/>); otherwise <see langword="false"/> if the connection has no remote IP.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// If multiple values are present in <c>X-Forwarded-For</c>, the left-most entry is considered the original client,
    /// per common proxy conventions (<c>"client, proxy1, proxy2"</c>).
    /// </para>
    /// <para>
    /// For the standardized <c>Forwarded</c> header, this method reads the <c>for=</c> parameter of the first element.
    /// Values may be quoted and IPv6 literals may be bracketed (e.g., <c>"[2001:db8::1]"</c>); both forms are normalized.
    /// </para>
    /// </remarks>
    public static bool TryGetClientIp(HttpContext ctx, string[]? trustedProxyCidrs, out IPAddress? ip)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        ip = null;
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null)
            return false;

        if (trustedProxyCidrs is { Length: > 0 } && IpCidr.AnyContains(trustedProxyCidrs, remote))
        {
            // X-Forwarded-For: "client, proxy1, proxy2"
            var fwd = ctx.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(fwd))
            {
                var first = fwd.Split(',')[0].Trim();
                first = CleanForwardedAddress(first);
                if (IPAddress.TryParse(first, out var parsed))
                {
                    ip = parsed;
                    return true;
                }
            }

            // RFC 7239 Forwarded: for=<client-ip>;proto=https;host=...
            var forwarded = ctx.Request.Headers["Forwarded"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var forValue = GetForwardedParam(forwarded, "for");
                if (!string.IsNullOrWhiteSpace(forValue))
                {
                    var v = CleanForwardedAddress(forValue!);
                    if (IPAddress.TryParse(v, out var parsed))
                    {
                        ip = parsed;
                        return true;
                    }
                }
            }
        }

        // Fallback: not from a trusted proxy; use the connection remote IP
        ip = remote;
        return true;
    }

    /// <summary>
    /// Attempts to obtain the client-facing URL scheme (protocol) from <c>X-Forwarded-Proto</c> or
    /// the <c>Forwarded</c> header, when the request traversed a trusted proxy.
    /// </summary>
    /// <param name="ctx">The HTTP context for the current request.</param>
    /// <param name="trustedProxyCidrs">
    /// Trusted proxy CIDR ranges used to decide whether forwarding headers are honored.
    /// </param>
    /// <returns>
    /// The forwarded protocol (e.g., <c>"http"</c>, <c>"https"</c>) if reliably determined; otherwise <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// When the connection is not from a trusted proxy or the header is absent, consider using
    /// <see cref="HttpRequest.Scheme"/> as the authoritative value.
    /// </remarks>
    public static string? TryGetForwardedProto(HttpContext ctx, string[]? trustedProxyCidrs)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null)
            return null;

        if (trustedProxyCidrs is { Length: > 0 } && IpCidr.AnyContains(trustedProxyCidrs, remote))
        {
            var proto = ctx.Request.Headers["X-Forwarded-Proto"].ToString();
            if (!string.IsNullOrWhiteSpace(proto))
                return proto.Split(',')[0].Trim();

            var forwarded = ctx.Request.Headers["Forwarded"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return GetForwardedParam(forwarded, "proto");
        }

        return null;
    }

    /// <summary>
    /// Attempts to obtain the client-facing host from <c>X-Forwarded-Host</c> or the <c>Forwarded</c> header,
    /// when the request traversed a trusted proxy.
    /// </summary>
    /// <param name="ctx">The HTTP context for the current request.</param>
    /// <param name="trustedProxyCidrs">
    /// Trusted proxy CIDR ranges used to decide whether forwarding headers are honored.
    /// </param>
    /// <returns>
    /// The forwarded host (for example, <c>"example.com"</c> or <c>"example.com:8443"</c>) if present; otherwise <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method does not perform DNS validation or SNI checks. If you need strong guarantees (e.g., for redirects),
    /// validate the returned host against an allowlist.
    /// </remarks>
    public static string? TryGetForwardedHost(HttpContext ctx, string[]? trustedProxyCidrs)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null)
            return null;

        if (trustedProxyCidrs is { Length: > 0 } && IpCidr.AnyContains(trustedProxyCidrs, remote))
        {
            var host = ctx.Request.Headers["X-Forwarded-Host"].ToString();
            if (!string.IsNullOrWhiteSpace(host))
                return host.Split(',')[0].Trim();

            var forwarded = ctx.Request.Headers["Forwarded"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return GetForwardedParam(forwarded, "host");
        }

        return null;
    }

    // ---- helpers ----

    /// <summary>
    /// Normalizes a forwarded address value by removing surrounding quotes, IPv6 brackets, and
    /// stripping an IPv4/hostname port suffix when unambiguous.
    /// </summary>
    /// <param name="s">The raw forwarded address token (possibly quoted or bracketed).</param>
    /// <returns>
    /// A cleaned address string suitable for <see cref="IPAddress.TryParse(string, out IPAddress)"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Examples:
    /// <list type="bullet">
    ///   <item><description><c>"203.0.113.10"</c> → <c>203.0.113.10</c></description></item>
    ///   <item><description><c>"203.0.113.10:1234"</c> → <c>203.0.113.10</c></description></item>
    ///   <item><description><c>"[2001:db8::1]"</c> → <c>2001:db8::1</c></description></item>
    ///   <item><description><c>"&quot;[2001:db8::1]&quot;"</c> → <c>2001:db8::1</c></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// For IPv6 literals, the method honors bracketed forms per RFC. It avoids stripping segments for IPv6 because
    /// multiple colons are expected and a trailing port cannot be disambiguated without brackets.
    /// </para>
    /// </remarks>
    private static string CleanForwardedAddress(string s)
    {
        ArgumentNullException.ThrowIfNull(s);

        var v = s.Trim().Trim('"');
        if (v.Length == 0)
            return v;

        if (v[0] == '[')
        {
            var end = v.IndexOf(']', StringComparison.Ordinal);
            if (end > 0)
                return v.Substring(1, end - 1);
            return v.Trim('[', ']');
        }

        // IPv4:port or hostname:port — if a single colon exists and a dot is present, strip the port
        var firstColon = v.IndexOf(':', StringComparison.Ordinal);
        var lastColon = v.LastIndexOf(':');
        if (firstColon == lastColon && firstColon > 0 && v.Contains('.', StringComparison.Ordinal))
            return v[..firstColon];

        return v;
    }

    /// <summary>
    /// Extracts the value of a named parameter (e.g., <c>for</c>, <c>proto</c>, or <c>host</c>) from the RFC
    /// <c>Forwarded</c> header. The first matching element wins.
    /// </summary>
    /// <param name="forwardedHeader">The full <c>Forwarded</c> header string (possibly containing multiple elements).</param>
    /// <param name="paramName">The parameter name to retrieve (case-insensitive).</param>
    /// <returns>The parameter value if found; otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="forwardedHeader"/> is <see langword="null"/> or <paramref name="paramName"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The header may consist of multiple comma-separated elements; each element may contain multiple semicolon-separated
    /// key/value pairs. Values may be quoted. For example:
    /// <code>
    /// Forwarded: for="[2001:db8::1]";proto=https;host=example.com, for=203.0.113.10
    /// </code>
    /// </para>
    /// </remarks>
    private static string? GetForwardedParam(string forwardedHeader, string paramName)
    {
        ArgumentNullException.ThrowIfNull(forwardedHeader);
        ArgumentNullException.ThrowIfNull(paramName);

        foreach (var element in forwardedHeader.Split(','))
        {
            foreach (var part in element.Split(';'))
            {
                var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2 && kv[0].Equals(paramName, StringComparison.OrdinalIgnoreCase))
                    return kv[1].Trim().Trim('"');
            }
        }
        return null;
    }
}
