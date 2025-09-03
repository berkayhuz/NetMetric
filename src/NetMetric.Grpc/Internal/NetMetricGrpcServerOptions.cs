// <copyright file="NetMetricGrpcServerOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Grpc.Internal;

/// <summary>
/// Provides configuration options for gRPC server metrics instrumentation in NetMetric.
/// </summary>
/// <remarks>
/// <para>
/// This options class defines histogram bucket boundaries for:
/// </para>
/// <list type="bullet">
/// <item><description>gRPC call latency in milliseconds</description></item>
/// <item><description>gRPC message size in bytes</description></item>
/// </list>
/// <para>
/// Default boundaries are provided, but can be overridden at registration time
/// to better suit the expected workload characteristics.
/// </para>
/// </remarks>
/// <example>
/// Example of customizing options during registration:
/// <code language="csharp">
/// services.AddNetMetricGrpc(options =>
/// {
///     options.LatencyBucketsMs = new double[] { 1, 5, 10, 25, 50, 100, 250, 500 };
///     options.SizeBuckets = new double[] { 128, 512, 1024, 8192, 65536 };
/// });
/// </code>
/// </example>
public sealed class NetMetricGrpcServerOptions
{
    private static readonly double[] DefaultLatencyBuckets =
        { 1d, 2d, 5d, 10d, 20d, 50d, 100d, 200d, 500d, 1000d, 2000d, 5000d };

    private static readonly double[] DefaultSizeBuckets =
        { 64d, 128d, 256d, 512d, 1024d, 4096d, 16384d, 65536d, 262144d };

    /// <summary>
    /// Histogram bucket boundaries (in milliseconds) used for measuring gRPC call latency.
    /// </summary>
    /// <value>
    /// A read-only list of bucket boundary values expressed in milliseconds.
    /// Defaults: <c>{1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000}</c>.
    /// </value>
    /// <remarks>
    /// Buckets are applied when building latency histograms in <see cref="GrpcServerMetricSet"/>.
    /// </remarks>
    public IReadOnlyList<double> LatencyBucketsMs { get; init; } = DefaultLatencyBuckets;

    /// <summary>
    /// Histogram bucket boundaries (in bytes) used for measuring gRPC message sizes.
    /// </summary>
    /// <value>
    /// A read-only list of bucket boundary values expressed in bytes.
    /// Defaults: <c>{64, 128, 256, 512, 1024, 4096, 16384, 65536, 262144}</c>.
    /// </value>
    /// <remarks>
    /// Buckets are applied when building message size histograms in <see cref="GrpcServerMetricSet"/>.
    /// </remarks>
    public IReadOnlyList<double> SizeBuckets { get; init; } = DefaultSizeBuckets;
}
