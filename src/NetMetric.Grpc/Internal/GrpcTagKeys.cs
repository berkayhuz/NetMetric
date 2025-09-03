// <copyright file="GrpcTagKeys.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Grpc.Core;

namespace NetMetric.Grpc.Internal;

/// <summary>
/// Provides constant tag keys for gRPC server metrics,
/// following semantic conventions for telemetry labeling.
/// </summary>
/// <remarks>
/// These keys are used as metric dimensions when recording gRPC server calls,
/// durations, message sizes, counts, and errors.  
/// They align with OpenTelemetry semantic conventions to ensure
/// consistent labeling across metrics and exporters.
/// </remarks>
/// <example>
/// Example of applying tags when creating a counter:
/// <code language="csharp">
/// var calls = factory.Counter(GrpcMetricNames.CallsTotal, "gRPC calls total")
///     .WithTags(tags => {
///         tags.Add(GrpcTagKeys.Service, "MyCompany.UserService");
///         tags.Add(GrpcTagKeys.Method, "GetUser");
///         tags.Add(GrpcTagKeys.Type, "unary");
///         tags.Add(GrpcTagKeys.Code, "0"); // OK
///     })
///     .Build();
/// calls.Increment();
/// </code>
/// </example>
internal static class GrpcTagKeys
{
    /// <summary>
    /// Tag key for the gRPC service name.  
    /// </summary>
    /// <remarks>
    /// Example: <c>"MyCompany.MyApi.UserService"</c>.
    /// </remarks>
    public const string Service = "service";

    /// <summary>
    /// Tag key for the gRPC method name.
    /// </summary>
    /// <remarks>
    /// Example: <c>"GetUser"</c>.
    /// </remarks>
    public const string Method = "method";

    /// <summary>
    /// Tag key for the gRPC call type.
    /// </summary>
    /// <remarks>
    /// Possible values include:
    /// <list type="bullet">
    /// <item><description><c>unary</c></description></item>
    /// <item><description><c>server_streaming</c></description></item>
    /// <item><description><c>client_streaming</c></description></item>
    /// <item><description><c>duplex</c></description></item>
    /// </list>
    /// </remarks>
    public const string Type = "type";

    /// <summary>
    /// Tag key for the gRPC status code.
    /// </summary>
    /// <remarks>
    /// Recommended to use numeric string values matching
    /// <see cref="StatusCode"/> (e.g., <c>"0"</c> for OK, <c>"14"</c> for Unavailable).
    /// </remarks>
    public const string Code = "code";

    /// <summary>
    /// Tag key for message direction.
    /// </summary>
    /// <remarks>
    /// Possible values:
    /// <list type="bullet">
    /// <item><description><c>request</c> – messages sent by the client</description></item>
    /// <item><description><c>response</c> – messages sent by the server</description></item>
    /// </list>
    /// </remarks>
    public const string Direction = "direction";

    /// <summary>
    /// Tag key for exception type short name (used for error classification).
    /// </summary>
    /// <remarks>
    /// Example values:
    /// <list type="bullet">
    /// <item><description><c>"InvalidOperationException"</c></description></item>
    /// <item><description><c>"RpcException"</c></description></item>
    /// </list>
    /// This tag is optional but useful for tracking error categories.
    /// </remarks>
    public const string Exception = "exception";
}
