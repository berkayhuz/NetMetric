// <copyright file="HttpClientMetricNames.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.HttpClient.Internal;

/// <summary>
/// Canonical metric names used by NetMetric's HTTP client instrumentation.
/// </summary>
/// <remarks>
/// <para>
/// These constants define the <em>names</em> under which measurements are recorded. They are
/// consumed by <see cref="HttpClientMetricSet"/> when building instruments and are intended to
/// be stable over time for dashboarding and alerting.
/// </para>
/// <para>
/// Unless otherwise noted, label dimensions for HTTP client metrics follow
/// <see cref="HttpClientTagKeys"/> and typically include:
/// <c>http.host</c>, <c>http.method</c>, and <c>url.scheme</c>. Additional labels are
/// explicitly called out per metric below.
/// </para>
/// <para>
/// Units:
/// <list type="bullet">
/// <item><description>
/// <c>*.duration</c> histograms are recorded in <c>ms</c>.
/// </description></item>
/// <item><description>
/// <c>*.size</c> histograms are recorded in <c>bytes</c>.
/// </description></item>
/// <item><description>
/// <c>*.total</c> counters are dimensionless monotonically increasing counts.
/// </description></item>
/// </list>
/// </para>
/// </remarks>
/// <seealso cref="HttpClientMetricSet"/>
/// <seealso cref="HttpClientTagKeys"/>
internal static class HttpClientMetricNames
{
    /// <summary>
    /// Histogram that measures the duration of a specific HTTP/network <c>phase</c>
    /// (e.g., DNS resolution, TCP connect, TLS handshake, request send, response wait, download, total).
    /// </summary>
    /// <remarks>
    /// Labels: {<c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>, <c>phase</c>} — where
    /// <c>phase</c> is a canonical lower-case token such as <c>dns</c>, <c>connect</c>, <c>tls</c>,
    /// <c>total</c>, or <c>download</c>.
    /// Unit: <c>ms</c>.
    /// </remarks>
    /// <example>
    /// Recording a 37.5 ms TCP connect on <c>https://api.example.com</c>:
    /// <code language="csharp">
    /// metrics.GetPhase("api.example.com", "GET", "https", "connect").Observe(37.5);
    /// </code>
    /// </example>
    public const string PhaseDuration = "http.client.phase.duration";

    /// <summary>
    /// Counter for total HTTP requests, partitioned by status code.
    /// </summary>
    /// <remarks>
    /// Labels: {<c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>, <c>http.status_code</c>}.
    /// Unit: count (monotonic).
    /// </remarks>
    /// <example>
    /// Incrementing on a 200 OK response:
    /// <code language="csharp">
    /// metrics.GetTotal("api.example.com", "POST", "https", "200").Increment();
    /// </code>
    /// </example>
    public const string RequestsTotal = "http.client.requests.total";

    /// <summary>
    /// Histogram for observed HTTP <em>response</em> body size in bytes.
    /// </summary>
    /// <remarks>
    /// Labels: {<c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>}.
    /// Unit: <c>bytes</c>.
    /// </remarks>
    /// <example>
    /// Observing a downloaded payload of 12,345 bytes:
    /// <code language="csharp">
    /// metrics.GetSize("api.example.com", "GET", "https").Observe(12345);
    /// </code>
    /// </example>
    public const string ResponseSize = "http.client.response.size";

    /// <summary>
    /// Histogram for observed HTTP <em>request</em> body size in bytes.
    /// </summary>
    /// <remarks>
    /// Labels: {<c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>}.
    /// Unit: <c>bytes</c>.
    /// </remarks>
    /// <example>
    /// When sending a JSON POST body:
    /// <code language="csharp">
    /// // Suppose the serialized request content length is known:
    /// factory.Histogram(HttpClientMetricNames.RequestSize, "HTTP client request size (bytes)");
    /// // or via a higher-level helper that tags host/method/scheme appropriately.
    /// </code>
    /// </example>
    public const string RequestSize = "http.client.request.size";

    /// <summary>
    /// Counter for HTTP redirects followed by the client (3xx responses that result in a follow-up request).
    /// </summary>
    /// <remarks>
    /// Labels: {<c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>}.
    /// Unit: count (monotonic).
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// metrics.GetRedirects("example.com", "GET", "http").Increment();
    /// </code>
    /// </example>
    public const string RedirectsTotal = "http.client.redirects.total";

    /// <summary>
    /// Counter for retry attempts performed by the client for a given logical request.
    /// </summary>
    /// <remarks>
    /// Labels: {<c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>}.
    /// Unit: count (monotonic).
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// metrics.GetRetries("api.example.com", "GET", "https").Increment();
    /// </code>
    /// </example>
    public const string RetriesTotal = "http.client.retries.total";

    /// <summary>
    /// Counter for requests that timed out or were canceled.
    /// </summary>
    /// <remarks>
    /// Labels: {<c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>}.
    /// Unit: count (monotonic).
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// metrics.GetTimeouts("api.example.com", "DELETE", "https").Increment();
    /// </code>
    /// </example>
    public const string TimeoutsTotal = "http.client.timeouts.total";

    /// <summary>
    /// Counter for client-side errors surfaced as exceptions during the request pipeline.
    /// </summary>
    /// <remarks>
    /// Labels: {<c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>, <c>exception.type</c>}.
    /// Unit: count (monotonic).
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// // Example only — increment logic typically lives inside exception handling paths.
    /// factory.Counter(HttpClientMetricNames.ErrorsTotal, "HTTP client errors total")
    ///        .WithTags(t =&gt; {
    ///            t.Add(HttpClientTagKeys.Host, "api.example.com");
    ///            t.Add(HttpClientTagKeys.Method, "GET");
    ///            t.Add(HttpClientTagKeys.Scheme, "https");
    ///            t.Add(HttpClientTagKeys.Exception, typeof(HttpRequestException).FullName!);
    ///        })
    ///        .Build()
    ///        .Increment();
    /// </code>
    /// </example>
    public const string ErrorsTotal = "http.client.errors.total";

    /// <summary>
    /// Gauge reporting the current number of in-flight (active) HTTP requests.
    /// </summary>
    /// <remarks>
    /// Labels: {<c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>}.
    /// Unit: count (non-monotonic, sampled).
    /// </remarks>
    public const string Inflight = "http.client.inflight";

    /// <summary>
    /// Counter for connections reused from the pool (HTTP/1.1 keep-alive, HTTP/2/3 multiplexing heuristics may vary).
    /// </summary>
    /// <remarks>
    /// Labels: {<c>http.host</c>, <c>http.method</c>, <c>url.scheme</c>}.
    /// Unit: count (monotonic).
    /// </remarks>
    public const string ConnectionReused = "http.client.pool.connection.reused";
}
