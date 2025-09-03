// <copyright file="TagKeys.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AspNetCore.Internal;

/// <summary>
/// Provides constant keys for metric tags used in ASP.NET Core HTTP server instrumentation.
/// </summary>
/// <remarks>
/// <para>
/// These tag keys align with the
/// <a href="https://opentelemetry.io/docs/specs/semconv/http/">OpenTelemetry HTTP semantic conventions</a>,
/// ensuring consistent labeling of metrics across different telemetry backends such as Prometheus,
/// OpenTelemetry Collector, or Application Insights.
/// </para>
/// <para>
/// Consistent tag keys allow for aggregation, filtering, and dashboarding across multiple stages
/// of the ASP.NET Core request pipeline.
/// </para>
/// </remarks>
/// <seealso cref="MetricNames"/>
/// <seealso cref="MvcMetricNames"/>
internal static class TagKeys
{
    /// <summary>
    /// Tag key for the HTTP route template (e.g., <c>"/users/{id}"</c>).
    /// </summary>
    /// <remarks>
    /// Used to group metrics by logical route rather than raw URL paths,
    /// preventing high cardinality from variable segments.
    /// </remarks>
    public const string Route = "route";

    /// <summary>
    /// Tag key for the HTTP method (e.g., <c>GET</c>, <c>POST</c>).
    /// </summary>
    /// <remarks>
    /// Enables filtering and aggregation of metrics by request method.
    /// </remarks>
    public const string Method = "method";

    /// <summary>
    /// Tag key for the HTTP response status code (e.g., <c>200</c>, <c>404</c>).
    /// </summary>
    /// <remarks>
    /// Applied only to counters such as <see cref="MetricNames.RequestsTotal"/>
    /// to distinguish successful and failed responses.
    /// </remarks>
    public const string Code = "code";

    /// <summary>
    /// Tag key for the HTTP scheme (e.g., <c>http</c>, <c>https</c>).
    /// </summary>
    /// <remarks>
    /// Useful for distinguishing clear-text traffic from encrypted traffic
    /// when analyzing performance or failures.
    /// </remarks>
    public const string Scheme = "scheme";

    /// <summary>
    /// Tag key for the HTTP protocol flavor (e.g., <c>1.1</c>, <c>2</c>, <c>3</c>).
    /// </summary>
    /// <remarks>
    /// Allows comparison of request performance across HTTP/1.1, HTTP/2, and HTTP/3.
    /// </remarks>
    public const string Flavor = "http.flavor"; // 1.1 / 2 / 3
}
