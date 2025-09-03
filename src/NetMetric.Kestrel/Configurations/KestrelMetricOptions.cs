// <copyright file="KestrelMetricOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kestrel.Configurations;

/// <summary>
/// Provides configuration options for Kestrel metrics collection.
/// </summary>
/// <remarks>
/// <para>
/// These options define histogram bucket boundaries used when constructing
/// metric instruments such as TLS handshake latency histograms.
/// </para>
/// <para>
/// By default, <see cref="TlsHandshakeBucketsMs"/> is set to
/// <see cref="DefaultTlsHandshakeBucketsMs"/>, which covers a wide range of
/// handshake times (from sub-millisecond to 30 seconds).
/// </para>
/// </remarks>
/// <example>
/// Example of configuring custom handshake latency buckets during service registration:
/// <code language="csharp"><![CDATA[
/// services.Configure<KestrelMetricOptions>(options =>
/// {
///     options.TlsHandshakeBucketsMs = ImmutableArray.Create(
///         1.0,   // 1 ms
///         5.0,   // 5 ms
///         10.0,  // 10 ms
///         50.0,  // 50 ms
///         100.0, // 100 ms
///         500.0, // 500 ms
///         1000.0 // 1 second
///     );
/// });
/// ]]></code>
/// </example>
public sealed class KestrelMetricOptions
{
    /// <summary>
    /// Gets the histogram bucket boundaries for TLS handshake duration, in milliseconds.
    /// </summary>
    /// <value>
    /// An immutable array of bucket boundaries. Defaults to
    /// <see cref="DefaultTlsHandshakeBucketsMs"/>.
    /// </value>
    /// <remarks>
    /// This property is initialized with <see cref="DefaultTlsHandshakeBucketsMs"/>,
    /// which covers typical and outlier handshake durations, ensuring
    /// observability across both fast LAN handshakes and slow WAN/handshake failures.
    /// </remarks>
    public ImmutableArray<double> TlsHandshakeBucketsMs { get; init; } = DefaultTlsHandshakeBucketsMs;

    /// <summary>
    /// Gets the default TLS handshake histogram bucket boundaries in milliseconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These boundaries span from 0.5 ms up to 30,000 ms (30 seconds), covering:
    /// <list type="bullet">
    ///   <item><description>Sub-millisecond fast handshakes on loopback (<c>0.5</c>–<c>2</c> ms).</description></item>
    ///   <item><description>Common LAN scenarios (<c>4</c>–<c>60</c> ms).</description></item>
    ///   <item><description>WAN and degraded conditions (<c>120</c>–<c>8000</c> ms).</description></item>
    ///   <item><description>Extreme slow or failed handshakes (<c>15000</c>–<c>30000</c> ms).</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static readonly ImmutableArray<double> DefaultTlsHandshakeBucketsMs =
        ImmutableArray.Create(0.5, 1, 2, 4, 8, 15, 30, 60, 120, 250, 500,
                              1000, 2000, 4000, 8000, 15000, 30000);
}
