// <copyright file="PrometheusEndpointExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using NetMetric.Export.Prometheus.AspNetCore.Services;
using NetMetric.Export.Prometheus.AspNetCore.Util;
using NetMetric.Export.Prometheus.Formatting;
using NetMetric.Export.Prometheus.Options;

namespace NetMetric.Export.Prometheus.AspNetCore;

/// <summary>
/// Provides endpoint-mapping extensions that expose <c>NetMetric</c> metrics
/// in Prometheus' text exposition format via ASP.NET Core routing.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint applies multiple defense-in-depth measures:
/// </para>
/// <list type="bullet">
///   <item><description>Client IP allow/deny using CIDR ranges (proxy-aware).</description></item>
///   <item><description>Host allowlist validation, with optional enforcement of forwarded host.</description></item>
///   <item><description>Forwarded-proto checks for proxy scenarios.</description></item>
///   <item><description>Optional mutual TLS (mTLS): issuer, thumbprint, and SPKI pin validation.</description></item>
///   <item><description>Optional HTTP Basic authentication (pluggable validator).</description></item>
///   <item><description>Token-bucket rate limiting per client IP.</description></item>
/// </list>
/// <para>
/// Responses set <c>Cache-Control: no-store</c> and the canonical Prometheus content type:
/// <c>text/plain; version=0.0.4; charset=utf-8</c>.
/// </para>
/// <para>
/// When trimming is enabled, ensure public surface members of your metric types are preserved
/// (for example via a linker descriptor or <see cref="DynamicDependencyAttribute"/>), because
/// <see cref="PrometheusFormatter"/> uses limited reflection for serialization.
/// </para>
/// </remarks>
/// <example>
/// <para>
/// Minimal API registration (Program.cs):
/// </para>
/// <code language="csharp"><![CDATA[
/// var builder = WebApplication.CreateBuilder(args);
///
/// // Configure exporter options (can also use IOptions)
/// builder.Services.AddSingleton(new PrometheusExporterOptions
/// {
///     EndpointPath = "/metrics",
///     ExporterMetricsEnabled = true,
///     ExporterMetricsPrefix = "netmetric_exporter_",
///     // Security examples:
///     AllowedHosts = { "metrics.example.com" },
///     AllowedNetworks = { "10.0.0.0/8", "192.168.1.0/24" },
///     RequireBasicAuth = true,
///     BasicAuthUsername = "prom",
///     BasicAuthPassword = "secret",
///     RequireClientCertificate = false,
///     RateLimitBucketCapacity = 60,
///     RateLimitRefillRatePerSecond = 1
/// });
///
/// // Required services for scraping/formatting/rate limiting
/// builder.Services.AddSingleton<IpRateLimiter>();
/// builder.Services.AddSingleton<PrometheusScrapeService>();
///
/// var app = builder.Build();
///
/// // Expose endpoint at /metrics
/// app.MapNetMetricPrometheus(); // or app.MapNetMetricPrometheus("/custom-metrics");
///
/// app.Run();
/// ]]></code>
/// </example>
/// <seealso cref="PrometheusExporterOptions"/>
/// <seealso cref="PrometheusFormatter"/>
/// <seealso cref="PrometheusScrapeService"/>
/// <seealso cref="IpRateLimiter"/>
public static class PrometheusEndpointExtensions
{
    private static readonly string[] HeadOnly = ["HEAD"];

    /// <summary>
    /// Maps the NetMetric Prometheus scrape endpoint onto the supplied route builder.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder used to register routes.</param>
    /// <param name="pattern">
    /// Optional route pattern. When <see langword="null"/> or whitespace, the value of
    /// <see cref="PrometheusExporterOptions.EndpointPath"/> is used.
    /// </param>
    /// <returns>
    /// An <see cref="IEndpointConventionBuilder"/> for further customization (for example,
    /// adding metadata).
    /// </returns>
    /// <remarks>
    /// <para>
    /// Both <c>GET</c> and <c>HEAD</c> handlers are registered for the same path.
    /// The <c>HEAD</c> handler returns only headers (no body), which is useful for health probes.
    /// </para>
    /// <para>
    /// The handler enforces any configured security, proxy, and rate-limiting policies defined
    /// in <see cref="PrometheusExporterOptions"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// app.MapNetMetricPrometheus("/metrics");
    /// // Optionally attach metadata:
    /// app.MapNetMetricPrometheus("/metrics")
    ///    .WithMetadata(new MyCustomMetadata());
    /// ]]></code>
    /// </example>
    [RequiresUnreferencedCode(
        "PrometheusFormatter uses reflection. With trimming, ensure public members of metric implementations are preserved (linker descriptor or DynamicDependency).")]
    public static IEndpointConventionBuilder MapNetMetricPrometheus(
        this IEndpointRouteBuilder endpoints,
        string? pattern = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var sp = endpoints.ServiceProvider;
        var opts = sp.GetService<PrometheusExporterOptions>() ?? new PrometheusExporterOptions();
        var path = string.IsNullOrWhiteSpace(pattern) ? opts.EndpointPath : pattern;

        var b = endpoints.MapGet(path, HandleScrape);   // HandleScrape already has RUC
        endpoints.MapMethods(path, HeadOnly, HandleHead);
        return b;
    }

    /// <summary>
    /// Handles a <c>HEAD</c> request for the Prometheus endpoint by returning headers only.
    /// </summary>
    /// <param name="ctx">The current HTTP context.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// Sets <c>Cache-Control: no-store</c> and the Prometheus content type header.
    /// No response body is written for <c>HEAD</c> requests.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is <see langword="null"/>.</exception>
    private static Task HandleHead(HttpContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        ctx.Response.Headers[HeaderNames.CacheControl] = "no-store";
        ctx.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";

        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes a Prometheus scrape (<c>GET</c>) by enforcing policy and writing metrics.
    /// </summary>
    /// <param name="ctx">The current HTTP context.</param>
    /// <remarks>
    /// <para>
    /// Behavior overview:
    /// </para>
    /// <list type="number">
    ///   <item><description>Resolves options and required services from DI.</description></item>
    ///   <item><description>Determines client IP (proxy-aware). Applies token-bucket rate limiting.</description></item>
    ///   <item><description>Validates allowed hosts, allowed networks, and proxy headers (proto/host).</description></item>
    ///   <item><description>Optionally validates a client certificate (issuer, thumbprint, SPKI) and OS trust chain.</description></item>
    ///   <item><description>Optionally enforces Basic authentication.</description></item>
    ///   <item><description>Honors <c>X-Prometheus-Scrape-Timeout-Seconds</c> by linking cancellation tokens.</description></item>
    ///   <item><description>Collects metrics via <see cref="PrometheusScrapeService"/> and writes them using <see cref="PrometheusFormatter"/>.</description></item>
    ///   <item><description>Optionally appends exporter self-metrics when enabled in <see cref="PrometheusExporterOptions.ExporterMetricsEnabled"/>.</description></item>
    /// </list>
    /// <para>
    /// On timeout or exceptions, an appropriate 4xx/5xx status code is returned; the exporter
    /// self-metrics are updated to reflect successes, failures, durations, and payload sizes.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is <see langword="null"/>.</exception>
    /// <seealso cref="PrometheusExporterOptions"/>
    /// <seealso cref="PrometheusScrapeService"/>
    /// <seealso cref="PrometheusFormatter"/>
    [RequiresUnreferencedCode(
        "PrometheusFormatter uses reflection. With trimming, preserve public members on your metric types via a linker descriptor or DynamicDependency.")]
    private static async Task HandleScrape(HttpContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var sp = ctx.RequestServices;
        var options = sp.GetService<PrometheusExporterOptions>() ?? new PrometheusExporterOptions();
        var rate = sp.GetRequiredService<IpRateLimiter>();
        var scraper = sp.GetRequiredService<PrometheusScrapeService>();

        ctx.Response.Headers[HeaderNames.CacheControl] = "no-store";
        ctx.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";

        // ---- client ip (proxy-aware) ----
        var clientIp = ProxyHeaders.TryGetClientIp(
            ctx,
            options.TrustedProxyNetworksOrEmpty.ToArray(),
            out var ip)
            ? ip
            : ctx.Connection.RemoteIpAddress;

        if (clientIp is null)
        {
            ExporterMetrics.CountError(ExporterErrorReason.ClientIpUnknown);

            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;

            return;
        }

        // ---- rate limit ----
        if (!rate.TryConsume(clientIp, options.RateLimitBucketCapacity, options.RateLimitRefillRatePerSecond))
        {
            ExporterMetrics.CountRateLimited();

            ctx.Response.StatusCode = options.RateLimitReturn503
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status429TooManyRequests;
            return;
        }

        // ---- host allowlist (proxy-host included) ----
        if (options.AllowedHosts is { Count: > 0 })
        {
            var host = ProxyHeaders.TryGetForwardedHost(
                           ctx,
                           options.TrustedProxyNetworksOrEmpty.ToArray())
                       ?? ctx.Request.Host.Host;

            if (!options.AllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                ExporterMetrics.CountError(ExporterErrorReason.HostDenied);

                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        // ---- ip allowlist ----
        if (options.AllowedNetworks is { Count: > 0 })
        {
            if (!IpCidr.AnyContains(options.AllowedNetworks, clientIp))
            {
                ExporterMetrics.CountError(ExporterErrorReason.IpDenied);

                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        // ---- proxy proto/host rules ----
        if (options.ExpectedForwardedProto is { Length: > 0 })
        {
            var proto = ProxyHeaders.TryGetForwardedProto(
                ctx,
                options.TrustedProxyNetworksOrEmpty.ToArray());

            if (!string.Equals(proto, options.ExpectedForwardedProto, StringComparison.OrdinalIgnoreCase))
            {
                ExporterMetrics.CountError(ExporterErrorReason.ProxyViolation);

                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync("# invalid forwarded proto").ConfigureAwait(false);
                return;
            }
        }

        if (options.EnforceForwardedHostWithAllowList && options.AllowedHosts is { Count: > 0 })
        {
            var fwdHost = ProxyHeaders.TryGetForwardedHost(
                ctx,
                options.TrustedProxyNetworksOrEmpty.ToArray());

            if (fwdHost is not null && !options.AllowedHosts.Contains(fwdHost, StringComparer.OrdinalIgnoreCase))
            {
                ExporterMetrics.CountError(ExporterErrorReason.ProxyViolation);

                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        // ---- mTLS: client certificate pinning/issuer/thumbprint ----
        if (options.RequireClientCertificate)
        {
            var cert = ctx.Connection.ClientCertificate;
            if (!ValidateClientCertificate(cert, options, out var mtlsReason))
            {
                ExporterMetrics.CountError(mtlsReason);

                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        // ---- basic auth ----
        if (options.RequireBasicAuth)
        {
            if (!TryReadBasicAuth(ctx, out var user, out var pass))
            {
                ExporterMetrics.CountError(ExporterErrorReason.BasicAuthFailed);
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers[HeaderNames.WWWAuthenticate] = "Basic realm=\"NetMetric Prometheus\"";
                return;
            }

            var ok =
                (options.BasicAuthValidator is not null && options.BasicAuthValidator(user, pass)) ||
                (options.BasicAuthValidator is null &&
                 string.Equals(user, options.BasicAuthUsername, StringComparison.Ordinal) &&
                 string.Equals(pass, options.BasicAuthPassword, StringComparison.Ordinal));

            if (!ok)
            {
                ExporterMetrics.CountError(ExporterErrorReason.BasicAuthFailed);
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers[HeaderNames.WWWAuthenticate] = "Basic realm=\"NetMetric Prometheus\"";
                return;
            }
        }

        // ---- scrape timeout ----
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        var tHeader = ctx.Request.Headers["X-Prometheus-Scrape-Timeout-Seconds"].ToString();
        if (double.TryParse(tHeader, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0.5)
        {
            var ms = Math.Max(0, (int)(seconds * 1000) - 250);
            linkedCts.CancelAfter(ms);
        }

        // ---- metrics: inflight + duration + payload size ----
        ExporterMetrics.InflightIncrement();
        var sw = Stopwatch.StartNew();

        try
        {
            var metrics = await scraper.CollectAsync(linkedCts.Token).ConfigureAwait(false);

            CountingTextWriter? writer = null;
            try
            {
                writer = new CountingTextWriter(
                    stream: ctx.Response.Body,
                    encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 1024,
                    leaveOpen: true);

                var formatter = new PrometheusFormatter(writer, options);
                await formatter.WriteAsync(metrics, linkedCts.Token).ConfigureAwait(false);

                // exporter self-metrics
                if (options.ExporterMetricsEnabled)
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                    ExporterMetrics.WritePrometheus(writer, options.ExporterMetricsPrefix);
                }

                await writer.FlushAsync().ConfigureAwait(false);

                sw.Stop();
                ExporterMetrics.OnSuccess(sw.Elapsed.TotalSeconds, writer.TotalBytes);
            }
            finally
            {
                if (writer is not null)
                    await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            ExporterMetrics.CountError(ExporterErrorReason.Timeout);

            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
        catch (IOException)
        {
            sw.Stop();
            ExporterMetrics.CountError(ExporterErrorReason.Exception);

            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
        catch (InvalidOperationException)
        {
            sw.Stop();
            ExporterMetrics.CountError(ExporterErrorReason.Exception);

            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
        finally
        {
            ExporterMetrics.InflightDecrement();
        }
    }

    /// <summary>
    /// Attempts to parse HTTP Basic authentication credentials from the request.
    /// </summary>
    /// <param name="ctx">The HTTP context.</param>
    /// <param name="user">When this method returns, contains the parsed username if present; otherwise an empty string.</param>
    /// <param name="pass">When this method returns, contains the parsed password if present; otherwise an empty string.</param>
    /// <returns>
    /// <see langword="true"/> if a valid <c>Authorization: Basic ...</c> header is present and parsed;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// The method accepts only UTF-8 credentials and silently rejects malformed or non-Base64 input.
    /// It does not perform any credential verification—only parsing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is <see langword="null"/>.</exception>
    private static bool TryReadBasicAuth(HttpContext ctx, out string user, out string pass)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        user = pass = string.Empty;

        if (!ctx.Request.Headers.TryGetValue(HeaderNames.Authorization, out var auth) || auth.Count == 0)
            return false;

        var s = auth.ToString();
        if (!s.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var base64 = s.AsSpan("Basic ".Length).Trim();
            var bytes = Convert.FromBase64String(base64.ToString());
            var creds = Encoding.UTF8.GetString(bytes);
            var idx = creds.IndexOf(':', StringComparison.Ordinal);
            if (idx <= 0)
                return false;

            user = creds[..idx];
            pass = creds[(idx + 1)..];
            return true;
        }
        catch (FormatException) { return false; }
        catch (DecoderFallbackException) { return false; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// Validates a client certificate according to the configured mTLS policy.
    /// </summary>
    /// <param name="cert">The client certificate presented by the peer over TLS.</param>
    /// <param name="opt">Exporter options that specify mTLS validation constraints.</param>
    /// <param name="reason">On failure, receives the failure reason; otherwise <see cref="ExporterErrorReason.None"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the certificate passes all configured checks; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Checks (each enforced only if configured): thumbprint allowlist, issuer allowlist,
    /// SPKI SHA-256 pin (Base64), and OS trust chain validation with online revocation checking.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="opt"/> is <see langword="null"/>.</exception>
    private static bool ValidateClientCertificate(
         X509Certificate2? cert,
         PrometheusExporterOptions opt,
         out ExporterErrorReason reason)
    {
        if (cert is null)
        {
            reason = ExporterErrorReason.MtlsFailed;
            return false;
        }

        ArgumentNullException.ThrowIfNull(opt);

        // Thumbprint
        if (opt.AllowedClientThumbprints is { Count: > 0 })
        {
            var tp = cert.Thumbprint?.Replace(":", "", StringComparison.Ordinal).ToUpperInvariant();
            var ok = tp is not null && opt.AllowedClientThumbprints.Any(a =>
                string.Equals(a.Replace(":", "", StringComparison.Ordinal).ToUpperInvariant(), tp, StringComparison.Ordinal));
            if (!ok)
            {
                reason = ExporterErrorReason.MtlsFailed;
                return false;
            }
        }

        // Issuer
        if (opt.AllowedClientIssuers is { Count: > 0 })
        {
            var iss = cert.Issuer;
            var ok = opt.AllowedClientIssuers.Any(a => string.Equals(a, iss, StringComparison.Ordinal));
            if (!ok)
            {
                reason = ExporterErrorReason.MtlsFailed;
                return false;
            }
        }

        // SPKI pin (SHA-256, Base64)
        if (opt.AllowedSpkiSha256PinsBase64 is { Count: > 0 })
        {
            var spki = ExportSubjectPublicKeyInfo(cert);
            var hash = SHA256.HashData(spki);
            var b64 = Convert.ToBase64String(hash);

            var ok = opt.AllowedSpkiSha256PinsBase64.Contains(b64, StringComparer.Ordinal);
            if (!ok)
            {
                reason = ExporterErrorReason.MtlsFailed;
                return false;
            }
        }

        // Chain validation (OS trust store)
        using var chain = new X509Chain
        {
            ChainPolicy =
            {
                RevocationMode = X509RevocationMode.Online,
                RevocationFlag = X509RevocationFlag.ExcludeRoot,
                VerificationFlags = X509VerificationFlags.NoFlag
            }
        };
        var built = chain.Build(cert);
        reason = built ? ExporterErrorReason.None : ExporterErrorReason.MtlsFailed;
        return built;
    }

    /// <summary>
    /// Exports the certificate's public key in SubjectPublicKeyInfo (SPKI) DER encoding.
    /// </summary>
    /// <param name="cert">The certificate containing a public key.</param>
    /// <returns>The SPKI bytes in DER format.</returns>
    /// <remarks>
    /// Uses <see cref="PublicKey.ExportSubjectPublicKeyInfo()"/> when available; falls back to
    /// <see cref="X509Certificate.GetPublicKey()"/> on older runtimes.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="cert"/> is <see langword="null"/>.</exception>
    private static byte[] ExportSubjectPublicKeyInfo(X509Certificate2 cert)
    {
        ArgumentNullException.ThrowIfNull(cert);

        try
        {
            return cert.PublicKey.ExportSubjectPublicKeyInfo();
        }
        catch (CryptographicException)
        {
            return cert.GetPublicKey();
        }
    }

    /// <summary>
    /// Builds a DER-encoded SubjectPublicKeyInfo (SPKI) structure from a certificate.
    /// </summary>
    /// <param name="cert">The certificate whose SPKI will be constructed.</param>
    /// <returns>DER-encoded SPKI bytes.</returns>
    /// <remarks>
    /// Encodes <em>AlgorithmIdentifier</em> and the <em>subjectPublicKey</em> BIT STRING as per X.509.
    /// Typically unnecessary on modern runtimes that can export SPKI directly, but retained for compatibility.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="cert"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the certificate does not contain a public key or has missing public key metadata.
    /// </exception>
    private static byte[] BuildSubjectPublicKeyInfo(X509Certificate2 cert)
    {
        // AlgorithmIdentifier = SEQUENCE { algorithm OID, parameters ANY OPTIONAL }
        // SubjectPublicKeyInfo = SEQUENCE { AlgorithmIdentifier, subjectPublicKey BIT STRING }
        ArgumentNullException.ThrowIfNull(cert);

        var pk = cert.PublicKey ?? throw new InvalidOperationException("Certificate has no public key.");
        var oidValue = pk.Oid?.Value ?? throw new InvalidOperationException("PublicKey OID missing.");

        // OID TLV
        var oidTlv = EncodeOidTlv(oidValue);

        // Parameters TLV (DER). Some algorithms use NULL (e.g., RSA 05 00).
        var paramTlv = pk.EncodedParameters?.RawData;
        byte[] algIdInner = paramTlv is { Length: > 0 }
            ? Concat(oidTlv, paramTlv)
            : oidTlv;

        var algId = WrapSequence(algIdInner);

        // Public key BIT STRING TLV (already TLV)
        var subjectPublicKeyBitStringTlv = pk.EncodedKeyValue?.RawData
            ?? throw new InvalidOperationException("PublicKey EncodedKeyValue missing.");

        // SPKI = SEQUENCE(algId, bitString)
        return WrapSequence(Concat(algId, subjectPublicKeyBitStringTlv));
    }

    /// <summary>
    /// Encodes an OBJECT IDENTIFIER (OID) as a DER TLV sequence.
    /// </summary>
    /// <param name="dottedOid">The dotted-decimal OID value (for example, <c>"1.2.840.113549.1.1.1"</c>).</param>
    /// <returns>The DER TLV bytes for the OID.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dottedOid"/> is <see langword="null"/>.</exception>
    private static byte[] EncodeOidTlv(string dottedOid)
    {
        ArgumentNullException.ThrowIfNull(dottedOid);

        // OID content
        var content = EncodeOidContent(dottedOid);
        var len = EncodeDerLength(content.Length);
        return Concat(new byte[] { 0x06 }, len, content); // 0x06 = OBJECT IDENTIFIER
    }

    /// <summary>
    /// Encodes the content octets of an OBJECT IDENTIFIER (DER), excluding tag and length.
    /// </summary>
    /// <param name="dottedOid">The dotted-decimal OID.</param>
    /// <returns>DER-encoded content octets for the OID.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dottedOid"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the OID string is invalid.</exception>
    private static byte[] EncodeOidContent(string dottedOid)
    {
        ArgumentNullException.ThrowIfNull(dottedOid);

        // DER OID content (tag and length EXCLUDED)
        var parts = dottedOid.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            throw new ArgumentException("Invalid OID: " + dottedOid);

        int first = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int second = int.Parse(parts[1], CultureInfo.InvariantCulture);
        if (first < 0 || second < 0)
            throw new ArgumentException("Invalid OID arcs.");

        var bytes = new List<byte>(16) { (byte)(first * 40 + second) };

        // Reuse a single stackalloc scratch buffer across iterations to avoid analyzer warning.
        Span<byte> scratch = stackalloc byte[10]; // enough for 2^64

        for (int i = 2; i < parts.Length; i++)
        {
            ulong v = ulong.Parse(parts[i], CultureInfo.InvariantCulture);
            int idx = scratch.Length;
            do
            {
                scratch[--idx] = (byte)(v & 0x7Fu);
                v >>= 7;
            } while (v != 0);

            while (idx < scratch.Length - 1)
            {
                bytes.Add((byte)(scratch[idx++] | 0x80)); // continuation
            }
            bytes.Add(scratch[idx]);
        }

        return bytes.ToArray();
    }

    /// <summary>
    /// Wraps the specified inner bytes in a DER <em>SEQUENCE</em> TLV.
    /// </summary>
    /// <param name="inner">The TLV or raw content bytes to wrap.</param>
    /// <returns>DER TLV for a SEQUENCE containing <paramref name="inner"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    private static byte[] WrapSequence(byte[] inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        var len = EncodeDerLength(inner.Length);
        return Concat(new byte[] { 0x30 }, len, inner); // 0x30 = SEQUENCE
    }

    /// <summary>
    /// Encodes a definite length field in DER format.
    /// </summary>
    /// <param name="length">The non-negative length value to encode.</param>
    /// <returns>DER-encoded length octets.</returns>
    private static byte[] EncodeDerLength(int length)
    {
        // DER definite length
        if (length < 0x80)
            return new[] { (byte)length };

        Span<byte> buf = stackalloc byte[4]; // 32-bit length is sufficient here
        int i = buf.Length;
        int v = length;
        while (v > 0)
        {
            buf[--i] = (byte)(v & 0xFF);
            v >>= 8;
        }
        int num = buf.Length - i;
        var result = new byte[1 + num];
        result[0] = (byte)(0x80 | num);
        Buffer.BlockCopy(buf.ToArray(), i, result, 1, num);
        return result;
    }

    /// <summary>
    /// Concatenates one or more byte arrays into a newly allocated buffer.
    /// </summary>
    /// <param name="parts">The parts to concatenate in order.</param>
    /// <returns>A new array containing all parts in sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parts"/> is <see langword="null"/>.</exception>
    private static byte[] Concat(params byte[][] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        int len = 0;
        foreach (var p in parts)
            len += p.Length;
        var dst = new byte[len];
        int off = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, dst, off, p.Length);
            off += p.Length;
        }
        return dst;
    }
}
