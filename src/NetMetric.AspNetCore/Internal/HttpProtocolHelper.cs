// <copyright file="HttpProtocolHelper.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Http;

namespace NetMetric.AspNetCore.Internal;

/// <summary>
/// Provides helper methods for resolving HTTP protocol versions ("flavors")
/// from <see cref="HttpContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// This utility maps ASP.NET Core's <see cref="HttpRequest.Protocol"/> values
/// (e.g., <c>HTTP/1.1</c>, <c>HTTP/2</c>, <c>HTTP/3</c>) to simplified strings:
/// </para>
/// <list type="bullet">
///   <item><description><c>"1.1"</c> for HTTP/1.1</description></item>
///   <item><description><c>"2"</c> for HTTP/2</description></item>
///   <item><description><c>"3"</c> for HTTP/3</description></item>
/// </list>
/// <para>
/// If the protocol string does not match known constants exactly, a fallback path attempts
/// to infer the version by searching for <c>'2'</c> or <c>'3'</c>. If neither is found,
/// <c>"1.1"</c> is returned as a safe default.
/// </para>
/// <para><strong>Thread Safety:</strong> The helper has no shared mutable state and is safe to call concurrently.</para>
/// </remarks>
/// <seealso cref="HttpProtocol"/>
internal static class HttpProtocolHelper
{
    /// <summary>
    /// Resolves the HTTP protocol "flavor" for the given <see cref="HttpContext"/>.
    /// </summary>
    /// <param name="ctx">
    /// The current <see cref="HttpContext"/> containing request metadata.
    /// </param>
    /// <returns>
    /// A simplified string representation of the HTTP version (<c>"1.1"</c>, <c>"2"</c>, or <c>"3"</c>).
    /// </returns>
    /// <example>
    /// <code>
    /// var flavor = HttpProtocolHelper.GetFlavor(httpContext);
    /// // e.g. "2" for HTTP/2 requests
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// Comparison against <see cref="HttpProtocol.Http11"/>, <see cref="HttpProtocol.Http2"/>,
    /// and <see cref="HttpProtocol.Http3"/> is performed using <see cref="StringComparison.Ordinal"/>.
    /// When no exact match is found, <see cref="string.Contains(char, StringComparison)"/> is used
    /// to detect <c>'2'</c> or <c>'3'</c> in <see cref="HttpRequest.Protocol"/>.
    /// </para>
    /// <para>
    /// This method is tolerant to unexpected protocol strings and will default to <c>"1.1"</c>
    /// rather than throwing; however, a <see cref="ArgumentNullException"/> is thrown if
    /// <paramref name="ctx"/> is <see langword="null"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="ctx"/> is <see langword="null"/>.
    /// </exception>
    public static string GetFlavor(HttpContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var p = ctx.Request.Protocol;

        if (string.Equals(p, HttpProtocol.Http3, StringComparison.Ordinal))
        {
            return "3";
        }

        if (string.Equals(p, HttpProtocol.Http2, StringComparison.Ordinal))
        {
            return "2";
        }

        if (string.Equals(p, HttpProtocol.Http11, StringComparison.Ordinal))
        {
            return "1.1";
        }

        if (p.Contains('3', StringComparison.Ordinal))
        {
            return "3";
        }

        if (p.Contains('2', StringComparison.Ordinal))
        {
            return "2";
        }

        return "1.1";
    }
}
