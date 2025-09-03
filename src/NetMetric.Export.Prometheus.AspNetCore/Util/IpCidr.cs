// <copyright file="IpCidr.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace NetMetric.Export.Prometheus.AspNetCore.Util;

/// <summary>
/// Provides utility methods for testing whether an <see cref="IPAddress"/> is contained
/// within a CIDR (Classless Inter-Domain Routing) network range.
/// </summary>
/// <remarks>
/// <para>
/// The helpers in this type support both IPv4 and IPv6 addresses and accept CIDR strings in
/// the canonical <c>address/maskBits</c> form (for example, <c>"192.168.1.0/24"</c> or <c>"::1/128"</c>).
/// </para>
/// <para>
/// Parsing and membership checks are performed without allocations beyond the byte arrays returned by
/// <see cref="IPAddress.GetAddressBytes"/>. The methods perform exact bitwise matching of the network prefix
/// defined by the mask length (<em>maskBits</em>).
/// </para>
/// <para>
/// These APIs are <strong>thread-safe</strong> and contain no shared mutable state.
/// </para>
/// </remarks>
internal static class IpCidr
{
    /// <summary>
    /// Determines whether the specified <paramref name="ip"/> address is contained in
    /// <em>any</em> of the provided CIDR ranges.
    /// </summary>
    /// <param name="cidrs">A sequence of CIDR strings (for example, <c>"10.0.0.0/8"</c>).</param>
    /// <param name="ip">The IP address to test.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="ip"/> is contained in at least one CIDR range;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="cidrs"/> or <paramref name="ip"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Each element in <paramref name="cidrs"/> is parsed independently. Invalid CIDR strings are ignored
    /// for containment purposes (i.e., treated as non-matching) and do not throw.
    /// </para>
    /// <para>
    /// Address family must match (IPv4 with IPv4, IPv6 with IPv6) for a positive result.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var ranges = new[] { "192.168.0.0/16", "10.0.0.0/8", "::1/128" };
    /// var ip1 = IPAddress.Parse("192.168.10.25");
    /// var ip2 = IPAddress.Parse("172.16.5.1");
    ///
    /// bool r1 = IpCidr.AnyContains(ranges, ip1); // true
    /// bool r2 = IpCidr.AnyContains(ranges, ip2); // false
    /// ]]></code>
    /// </example>
    public static bool AnyContains(IEnumerable<string> cidrs, IPAddress ip)
    {
        ArgumentNullException.ThrowIfNull(cidrs);
        ArgumentNullException.ThrowIfNull(ip);

        foreach (var c in cidrs)
        {
            if (Contains(c, ip))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Determines whether the specified <paramref name="ip"/> address is contained in the
    /// given CIDR range.
    /// </summary>
    /// <param name="cidr">The CIDR string (for example, <c>"192.168.0.0/16"</c> or <c>"2001:db8::/32"</c>).</param>
    /// <param name="ip">The IP address to test.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="ip"/> is within the CIDR block; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="cidr"/> or <paramref name="ip"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// If <paramref name="cidr"/> cannot be parsed or its address family does not match that of
    /// <paramref name="ip"/>, the method returns <see langword="false"/>.
    /// </para>
    /// <para>
    /// The match is performed by comparing the network prefix bytes and (if necessary) the remaining
    /// partial byte defined by the mask length.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var cidr = "10.0.0.0/8";
    /// var a = IPAddress.Parse("10.12.34.56");
    /// var b = IPAddress.Parse("11.0.0.1");
    ///
    /// bool ia = IpCidr.Contains(cidr, a); // true
    /// bool ib = IpCidr.Contains(cidr, b); // false
    /// ]]></code>
    /// </example>
    public static bool Contains(string cidr, IPAddress ip)
    {
        ArgumentNullException.ThrowIfNull(cidr);
        ArgumentNullException.ThrowIfNull(ip);

        if (!TryParseCidr(cidr, out var network, out var maskBits))
        {
            return false;
        }

        if (network.AddressFamily != ip.AddressFamily)
        {
            return false;
        }

        var ipBytes = ip.GetAddressBytes();
        var netBytes = network.GetAddressBytes();

        int fullBytes = maskBits / 8;
        int remainingBits = maskBits % 8;

        // Compare fully masked bytes.
        for (int i = 0; i < fullBytes; i++)
        {
            if (ipBytes[i] != netBytes[i])
            {
                return false;
            }
        }

        // Compare remaining bits of the next byte, if any.
        if (remainingBits > 0)
        {
            // Example for remainingBits = 4: mask = 11110000 (0xF0)
            int mask = (byte)~(0xFF >> remainingBits);

            if ((ipBytes[fullBytes] & mask) != (netBytes[fullBytes] & mask))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Attempts to parse a CIDR string into its network address and mask length.
    /// </summary>
    /// <param name="s">The CIDR string (for example, <c>"172.16.0.0/12"</c> or <c>"fe80::/10"</c>).</param>
    /// <param name="network">
    /// When this method returns <see langword="true"/>, contains the parsed network <see cref="IPAddress"/>;
    /// otherwise, <see langword="null"/>.
    /// </param>
    /// <param name="maskBits">When this method returns <see langword="true"/>, contains the mask length in bits.</param>
    /// <returns>
    /// <see langword="true"/> if the CIDR string was successfully parsed; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="s"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The method accepts both IPv4 and IPv6 addresses and validates that the mask length is within the
    /// appropriate range: <c>0–32</c> for IPv4 (<see cref="AddressFamily.InterNetwork"/>) and
    /// <c>0–128</c> for IPv6 (<see cref="AddressFamily.InterNetworkV6"/>).
    /// </para>
    /// <para>
    /// Parsing of the mask portion uses <see cref="int.TryParse(string, NumberStyles, IFormatProvider, out int)"/>
    /// with <see cref="NumberStyles.Integer"/> and <see cref="CultureInfo.InvariantCulture"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// if (IpCidr.TryParseCidr("192.168.100.0/22", out var net, out var bits))
    /// {
    ///     Console.WriteLine($"{net} / {bits}"); // 192.168.100.0 / 22
    /// }
    /// ]]></code>
    /// </example>
    private static bool TryParseCidr(
        string s,
        [NotNullWhen(true)] out IPAddress? network,
        out int maskBits)
    {
        ArgumentNullException.ThrowIfNull(s);

        // initialize outs
        network = null;
        maskBits = 0;

        // normalize/trim
        s = s.Trim();
        if (s.Length == 0)
        {
            return false;
        }

        // Use overload with StringComparison for clarity and analyzer compliance.
        int idx = s.IndexOf('/', StringComparison.Ordinal);
        if (idx <= 0 || idx == s.Length - 1) // no slash, or nothing after slash
        {
            return false;
        }

        var ipPart = s[..idx].Trim();
        var maskPart = s[(idx + 1)..].Trim();

        if (!IPAddress.TryParse(ipPart, out var parsedIp))
        {
            return false;
        }

        // Parse mask bits culture-invariant.
        if (!int.TryParse(maskPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out maskBits))
        {
            return false;
        }

        // Validate mask ranges per address family.
        bool maskOk =
            (parsedIp.AddressFamily == AddressFamily.InterNetwork && maskBits is >= 0 and <= 32) ||
            (parsedIp.AddressFamily == AddressFamily.InterNetworkV6 && maskBits is >= 0 and <= 128);

        if (!maskOk)
        {
            return false;
        }

        network = parsedIp;
        return true;
    }
}
