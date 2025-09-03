// <copyright file="RequestRouteResolver.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace NetMetric.AspNetCore.Internal;

/// <summary>
/// Provides helpers to resolve and normalize ASP.NET Core routing patterns
/// for consistent metric labeling.
/// </summary>
/// <remarks>
/// <para>
/// Normalization strips route parameter constraints to keep metric cardinality manageable.
/// For example, <c>"/users/{id:int}"</c> becomes <c>"/users/{id}"</c>.
/// </para>
/// <para>
/// If no <see cref="T:Microsoft.AspNetCore.Routing.RouteEndpoint" /> is resolved for the request,
/// a fallback label (e.g., <see cref="T:NetMetric.AspNetCore.AspNetCoreMetricOptions" />.OtherRouteLabel) is returned.
/// </para>
/// <para><strong>Thread Safety:</strong> The helpers are stateless; the compiled regex is immutable and safe to use concurrently.</para>
/// </remarks>
/// <seealso cref="T:Microsoft.AspNetCore.Routing.RouteEndpoint" />
/// <seealso cref="T:Microsoft.AspNetCore.Routing.Patterns.RoutePattern" />
/// <seealso cref="T:NetMetric.AspNetCore.AspNetCoreMetricOptions" />
internal static class RequestRouteResolver
{
    /// <summary>
    /// A compiled pattern that matches parameter constraints inside a route token,
    /// e.g., transforms <c>{id:int}</c> into <c>{id}</c>.
    /// </summary>
    private static readonly Regex s_paramConstraintRegex = new(@"\{([^}:]+):[^}]+\}", RegexOptions.Compiled);

    /// <summary>
    /// Resolves the normalized route template for the current request.
    /// </summary>
    /// <param name="context">The current <see cref="T:Microsoft.AspNetCore.Http.HttpContext" />.</param>
    /// <param name="otherLabel">
    /// The fallback label to use when no route pattern is available.
    /// </param>
    /// <returns>
    /// A normalized route pattern string with parameter constraints removed,
    /// or <paramref name="otherLabel"/> if no route endpoint was matched.
    /// </returns>
    /// <example>
    /// <code>
    /// var route = RequestRouteResolver.ResolveNormalizedRoute(httpContext, "other");
    /// // "/users/{id:int}" -> "/users/{id}"
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// When the request has been mapped via endpoint routing, the method inspects
    /// <see cref="P:Microsoft.AspNetCore.Routing.RouteEndpoint.RoutePattern" /> and uses
    /// <see cref="P:Microsoft.AspNetCore.Routing.Patterns.RoutePattern.RawText" /> when available,
    /// falling back to <see cref="M:System.Object.ToString" /> otherwise.
    /// </para>
    /// <para>
    /// This method assumes <paramref name="context"/> is non-null; passing <see langword="null"/> will result
    /// in a <see cref="T:System.NullReferenceException" /> at call sites.
    /// </para>
    /// </remarks>
    public static string ResolveNormalizedRoute(HttpContext context, string otherLabel)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is RouteEndpoint re)
        {
            // Use RawText if present, otherwise fall back to ToString()
            var raw = (re.RoutePattern.RawText ?? re.RoutePattern.ToString())!;
            return Normalize(raw);
        }
        return otherLabel;

        static string Normalize(string routePattern)
            => s_paramConstraintRegex.Replace(routePattern, "{$1}");
    }
}