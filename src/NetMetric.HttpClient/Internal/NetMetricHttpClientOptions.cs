// <copyright file="NetMetricHttpClientOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.HttpClient.Internal;

/// <summary>
/// Configuration options for NetMetric's HTTP client metrics collection.
/// </summary>
/// <remarks>
/// <para>
/// These options control the histogram bucket boundaries for request <em>latency</em> (milliseconds)
/// and payload <em>size</em> (bytes). They are applied when building metric instruments so that
/// observations are aggregated into meaningful ranges for dashboards and alerts.
/// </para>
/// <para>
/// Buckets should be specified as <strong>ascending</strong> sequences. This type does not re-order
/// or validate the monotonicity of the provided values; provide sensible, increasing thresholds for
/// accurate aggregation downstream.
/// </para>
/// <para>
/// Typical usage is via the Options pattern during DI configuration:
/// </para>
/// <example>
/// <code language="csharp"><![CDATA[
/// services.Configure<NetMetricHttpClientOptions>(o =>
/// {
///     // 1 ms .. 2 s (ms)
///     o.LatencyBucketsMs = new[] { 1d, 5, 10, 20, 50, 100, 200, 500, 1000, 2000 };
///
///     // 128 B .. 1 MiB (bytes)
///     o.SizeBuckets = new[] { 128d, 512, 1024, 4096, 16384, 65536, 262144, 1048576 };
/// });
/// ]]></code>
/// </example>
/// <para>
/// Alternatively, values can be supplied from configuration (e.g., <c>appsettings.json</c>):
/// </para>
/// <example>
/// <code language="json"><![CDATA[
/// {
///   "NetMetricHttpClient": {
///     "LatencyBucketsMs": [1, 5, 10, 20, 50, 100, 200, 500, 1000],
///     "SizeBuckets":      [128, 512, 1024, 4096, 16384, 65536]
///   }
/// }
/// ]]></code>
/// </example>
/// <para>
/// Units:
/// <list type="bullet">
/// <item><description><see cref="LatencyBucketsMs"/> are in <strong>milliseconds</strong>.</description></item>
/// <item><description><see cref="SizeBuckets"/> are in <strong>bytes</strong>.</description></item>
/// </list>
/// </para>
/// </remarks>
/// <threadsafety>
/// This type is immutable in practice from the perspective of consumers after options are bound.
/// Setters perform null checks but do not lock; configure during startup.
/// </threadsafety>
/// <seealso cref="HttpClientMetricSet"/>
public sealed class NetMetricHttpClientOptions
{
    private double[] _latencyBucketsMs = new double[] { 1, 5, 10, 20, 50, 100, 200, 500, 1000 };
    private double[] _sizeBuckets = new double[] { 128, 512, 1024, 4096, 16384, 65536 };

    /// <summary>
    /// Gets or sets the histogram bucket boundaries (in <strong>milliseconds</strong>) used for request
    /// latency metrics (e.g., <c>total</c>, <c>dns</c>, <c>connect</c>, <c>tls</c>, <c>download</c>).
    /// </summary>
    /// <value>
    /// An ascending sequence of latency thresholds in milliseconds.
    /// Default: <c>{1, 5, 10, 20, 50, 100, 200, 500, 1000}</c>.
    /// </value>
    /// <remarks>
    /// Provide values that match your SLOs and typical traffic profile. Very tight buckets on
    /// high-variance workloads can produce sparse distributions with limited utility.
    /// </remarks>
    /// <example>
    /// A request completing in 37 ms is counted in the <c>50</c> ms bucket.
    /// </example>
    /// <exception cref="ArgumentNullException">
    /// Thrown when attempting to set the property to <see langword="null"/>.
    /// </exception>
    public IReadOnlyList<double> LatencyBucketsMs
    {
        get => _latencyBucketsMs;
        set => _latencyBucketsMs = value is null ? throw new ArgumentNullException(nameof(value)) : value.ToArray();
    }

    /// <summary>
    /// Gets or sets the histogram bucket boundaries (in <strong>bytes</strong>) used for request/response
    /// size metrics.
    /// </summary>
    /// <value>
    /// An ascending sequence of size thresholds in bytes.
    /// Default: <c>{128, 512, 1024, 4096, 16384, 65536}</c>.
    /// </value>
    /// <remarks>
    /// Choose thresholds that reflect the payload sizes your services typically exchange.
    /// Consider adding larger buckets (e.g., 256 KiB, 1 MiB) for file-heavy endpoints.
    /// </remarks>
    /// <example>
    /// A payload of ~3 KiB (≈ 3072 bytes) is counted in the <c>4096</c> bucket.
    /// </example>
    /// <exception cref="ArgumentNullException">
    /// Thrown when attempting to set the property to <see langword="null"/>.
    /// </exception>
    public IReadOnlyList<double> SizeBuckets
    {
        get => _sizeBuckets;
        set => _sizeBuckets = value is null ? throw new ArgumentNullException(nameof(value)) : value.ToArray();
    }
}
