// <copyright file="AdvancedSocketsHandlerFactory.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.HttpClient.Handlers;

/// <summary>
/// Factory utilities for creating a <see cref="SocketsHttpHandler"/> that exposes
/// low-level network timing (DNS resolution and TCP connect) via a custom
/// <see cref="HttpMessageHandler"/> pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The handler produced by this factory installs a <see cref="SocketsHttpHandler.ConnectCallback"/>
/// to measure and export DNS and TCP connect timings, and configures
/// <see cref="SocketsHttpHandler.PlaintextStreamFilter"/> where supported (.NET 8/9).
/// TLS handshake latency is not measured here; it can be observed via
/// <c>System.Net.Security</c> <see cref="System.Diagnostics.DiagnosticSource"/> events.
/// </para>
/// <para>
/// <strong>Defaults &amp; design choices:</strong>
/// <list type="bullet">
/// <item><description><see cref="SocketsHttpHandler.AllowAutoRedirect"/> = <c>true</c></description></item>
/// <item><description><see cref="SocketsHttpHandler.UseProxy"/> = <c>false</c> (proxy is bypassed to guarantee custom connect path)</description></item>
/// <item><description><see cref="SocketsHttpHandler.AutomaticDecompression"/> = <see cref="DecompressionMethods.All"/></description></item>
/// </list>
/// IPv4 is preferred when available; otherwise the first resolved address is used.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> The returned handler is safe for concurrent use across requests,
/// matching the standard <see cref="HttpMessageHandler"/> guidelines.
/// </para>
/// </remarks>
/// <seealso cref="SocketsHttpHandler"/>
/// <seealso cref="System.Net.Dns"/>
/// <seealso cref="System.Net.Sockets.Socket"/>
public static class AdvancedSocketsHandlerFactory
{
    /// <summary>
    /// Creates and configures a <see cref="SocketsHttpHandler"/> that records
    /// DNS and TCP connect phase timings via the provided <paramref name="metrics"/>.
    /// TLS handshake timing is expected to be captured through <c>System.Net.Security</c>
    /// diagnostics rather than this callback.
    /// </summary>
    /// <param name="metrics">
    /// The metric set used to record phase latencies. The factory emits:
    /// <list type="bullet">
    /// <item><description><c>"dns"</c> — time to resolve the target host.</description></item>
    /// <item><description><c>"connect"</c> — time to establish the TCP connection.</description></item>
    /// </list>
    /// Phase labels include host, HTTP method, and scheme for cardinality control.
    /// </param>
    /// <returns>
    /// A configured <see cref="SocketsHttpHandler"/> whose <see cref="SocketsHttpHandler.ConnectCallback"/>
    /// performs explicit DNS resolution and <see cref="Socket.ConnectAsync(System.Net.EndPoint,System.Threading.CancellationToken)"/>
    /// while observing timings, and whose <see cref="SocketsHttpHandler.PlaintextStreamFilter"/> is set on supported frameworks.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>.NET version behavior:</strong>
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <strong>.NET 9+</strong>: assigns <see cref="SocketsHttpHandler.PlaintextStreamFilter"/>
    /// with signature <c>Func&lt;SocketsHttpPlaintextStreamFilterContext, CancellationToken, ValueTask&lt;Stream&gt;&gt;</c>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <strong>.NET 8</strong>: assigns <see cref="SocketsHttpHandler.PlaintextStreamFilter"/>
    /// with signature <c>Func&lt;SocketsHttpPlaintextStreamFilterContext, Stream, CancellationToken, ValueTask&lt;Stream&gt;&gt;</c>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Earlier frameworks: no plaintext filter is set.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <strong>Proxy:</strong> <see cref="SocketsHttpHandler.UseProxy"/> is disabled to ensure the
    /// custom connect path (and timings) are used. If your application requires proxies,
    /// build a separate handler configuration that does not override <see cref="SocketsHttpHandler.ConnectCallback"/>.
    /// </para>
    /// <para>
    /// <strong>Cancellation:</strong>
    /// The connect operation honors the request <see cref="CancellationToken"/>; cancellation propagates
    /// as <see cref="OperationCanceledException"/> / <see cref="TaskCanceledException"/>. In .NET 8+,
    /// DNS resolution also accepts the token; on earlier TFMs, DNS cancellation is cooperative at socket connect time.
    /// </para>
    /// <para>
    /// <strong>Exceptions:</strong> DNS and connect failures surface as typical network exceptions
    /// (e.g., <see cref="SocketException"/>); this factory does not swallow them.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="metrics"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="SocketException">
    /// Thrown if the TCP connection cannot be established.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown if the connection is canceled via the request's <see cref="CancellationToken"/>.
    /// </exception>
    /// <example>
    /// Create the handler and plug it into an <see cref="HttpClient"/>:
    /// <code language="csharp"><![CDATA[
    /// var handler = AdvancedSocketsHandlerFactory.Create(metricSet);
    /// using var http = new HttpClient(handler, disposeHandler: true);
    /// var resp = await http.GetAsync("https://example.com");
    /// ]]></code>
    /// </example>
    public static SocketsHttpHandler Create(HttpClientMetricSet metrics)
    {
        // Ensure documented behavior matches runtime behavior.
        ArgumentNullException.ThrowIfNull(metrics);

        var h = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            UseProxy = false, // IMPORTANT: no proxy support on this path
            AutomaticDecompression = DecompressionMethods.All,
        };

        h.ConnectCallback = async (ctx, ct) =>
        {
            var host = ctx.DnsEndPoint!.Host;
            var port = ctx.DnsEndPoint.Port;
            var scheme = ctx.InitialRequestMessage?.RequestUri?.Scheme ?? "http";
            var method = ctx.InitialRequestMessage?.Method.Method ?? "GET";

            // DNS
            var t0 = Stopwatch.GetTimestamp();
#if NET8_0_OR_GREATER
            IPAddress[] addrs = await Dns.GetHostAddressesAsync(host, AddressFamily.Unspecified, ct).ConfigureAwait(false);
#else
            IPAddress[] addrs = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
#endif
            var dnsMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            metrics.GetPhase(host, method, scheme, "dns").Observe(dnsMs);

            var addr = addrs.FirstOrDefault(a => a.AddressFamily is AddressFamily.InterNetwork) ?? addrs.First();
            var socket = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            // CONNECT
            var t1 = Stopwatch.GetTimestamp();
            await socket.ConnectAsync(new IPEndPoint(addr, port), ct).ConfigureAwait(false);
            var connectMs = (Stopwatch.GetTimestamp() - t1) * 1000.0 / Stopwatch.Frequency;
            metrics.GetPhase(host, method, scheme, "connect").Observe(connectMs);

            return new NetworkStream(socket, ownsSocket: true);
        };

        // IMPORTANT: place NET9 check FIRST, otherwise NET8_OR_GREATER also matches .NET 9
#if NET9_0_OR_GREATER
        // .NET 9: Func<SocketsHttpPlaintextStreamFilterContext, CancellationToken, ValueTask<Stream>>
        h.PlaintextStreamFilter = static (ctx, ct) =>
        {
            return ValueTask.FromResult(ctx.PlaintextStream);
        };
#elif NET8_0
        // .NET 8: Func<SocketsHttpPlaintextStreamFilterContext, Stream, CancellationToken, ValueTask<Stream>>
        h.PlaintextStreamFilter = static (ctx, stream, ct) =>
        {
            return ValueTask.FromResult(stream);
        };
#endif
        return h;
    }
}
