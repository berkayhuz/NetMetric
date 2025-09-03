// <copyright file="MetricNames.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AspNetCore.Internal;

/// <summary>
/// Provides well-known metric name constants used by the ASP.NET Core
/// integration of NetMetric.
/// </summary>
/// <remarks>
/// <para>
/// These names follow conventional OpenTelemetry and Prometheus style naming
/// for HTTP server metrics. Using consistent, semantic names allows metrics
/// to be easily scraped, queried, and visualized by a wide range of
/// observability backends.
/// </para>
/// <para>
/// Each constant is intended to be used when constructing or tagging
/// <see cref="IMetric"/> instances so that dashboards and alerts remain uniform.
/// </para>
/// </remarks>
/// <seealso href="https://opentelemetry.io/docs/specs/semconv/http/">OpenTelemetry HTTP Semantic Conventions</seealso>
internal static class MetricNames
{
    /// <summary>
    /// Metric name for request duration (in milliseconds).
    /// Represents the total time taken for an HTTP request to complete
    /// from the server's perspective.
    /// </summary>
    /// <remarks>
    /// Typically reported as a histogram with route, method, scheme, and
    /// protocol flavor dimensions.
    /// </remarks>
    public const string RequestDuration = "http.server.request.duration";

    /// <summary>
    /// Metric name for total request body size (in bytes).
    /// </summary>
    /// <remarks>
    /// Usually recorded as a histogram with route and method dimensions.
    /// Corresponds to the size of the HTTP request payload as received by the server.
    /// </remarks>
    public const string RequestSize = "http.server.request.size";

    /// <summary>
    /// Metric name for total response body size (in bytes).
    /// </summary>
    /// <remarks>
    /// Usually reported as a histogram with route, method, and status code dimensions.
    /// Represents the number of bytes written in the HTTP response body.
    /// </remarks>
    public const string ResponseSize = "http.server.response.size";

    /// <summary>
    /// Metric name for total number of requests served.
    /// </summary>
    /// <remarks>
    /// Implemented as a counter metric. Dimensions typically include route,
    /// method, scheme, flavor, and status code.
    /// </remarks>
    public const string RequestsTotal = "http.server.requests.total";
}
