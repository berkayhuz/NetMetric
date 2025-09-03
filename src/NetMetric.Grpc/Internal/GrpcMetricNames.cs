// <copyright file="GrpcMetricNames.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Grpc.Core;

namespace NetMetric.Grpc.Internal;

/// <summary>
/// Provides well-known metric name constants for gRPC server instrumentation.
/// </summary>
/// <remarks>
/// <para>
/// These metric names follow OpenTelemetry/Prometheus-style naming conventions.
/// </para>
/// <para>
/// They are typically combined with dimensions such as:
/// <list type="bullet">
/// <item><description><c>service</c> – The fully qualified gRPC service name.</description></item>
/// <item><description><c>method</c> – The gRPC method name.</description></item>
/// <item><description><c>type</c> – The gRPC call type (unary, client_streaming, etc.).</description></item>
/// <item><description><c>code</c> – The gRPC status code (e.g., <c>"0"</c> for OK).</description></item>
/// <item><description><c>direction</c> – The message direction (<c>request</c> or <c>response</c>).</description></item>
/// <item><description><c>exception</c> – The short exception type name (for error classification).</description></item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Example usage when defining a counter:
/// <code language="csharp">
/// var calls = factory.Counter(GrpcMetricNames.CallsTotal, "gRPC calls total")
///     .WithTags(tags => {
///         tags.Add("service", "MyCompany.UserService");
///         tags.Add("method", "GetUser");
///         tags.Add("type", "unary");
///         tags.Add("code", "0"); // OK
///     })
///     .Build();
/// calls.Increment();
/// </code>
/// </example>
internal static class GrpcMetricNames
{
    /// <summary>
    /// Metric name for gRPC call duration in milliseconds.
    /// </summary>
    /// <remarks>
    /// Reported as a <b>histogram</b> and tagged with
    /// <c>{service, method, type, code}</c>.
    /// </remarks>
    public const string CallDuration = "grpc.server.call.duration";

    /// <summary>
    /// Metric name for total number of gRPC calls.
    /// </summary>
    /// <remarks>
    /// Reported as a <b>counter</b> and tagged with
    /// <c>{service, method, type, code}</c>.
    /// </remarks>
    public const string CallsTotal = "grpc.server.calls.total";

    /// <summary>
    /// Metric name for gRPC message sizes in bytes.
    /// </summary>
    /// <remarks>
    /// Reported as a <b>histogram</b> and tagged with
    /// <c>{service, method, type, direction}</c>.
    /// </remarks>
    /// <example>
    /// This metric can be used to track payload sizes for
    /// both request and response messages:
    /// <code language="csharp">
    /// metrics.SizeHistogram("UserService", "GetUser", "unary", "request")
    ///        .Observe(requestSizeBytes);
    /// </code>
    /// </example>
    public const string MessageSize = "grpc.server.message.size";

    /// <summary>
    /// Metric name for total number of gRPC messages.
    /// </summary>
    /// <remarks>
    /// Reported as a <b>counter</b> and tagged with
    /// <c>{service, method, type, direction}</c>.
    /// </remarks>
    public const string MessagesTotal = "grpc.server.messages.total";

    /// <summary>
    /// Metric name for total number of gRPC errors.
    /// </summary>
    /// <remarks>
    /// Reported as a <b>counter</b> and tagged with
    /// <c>{service, method, type, exception}</c>.
    /// </remarks>
    /// <example>
    /// When an <see cref="RpcException"/> is thrown,
    /// this counter can be incremented with the exception type:
    /// <code language="csharp">
    /// metrics.ErrorsCounter("UserService", "GetUser", "unary", nameof(RpcException))
    ///        .Increment();
    /// </code>
    /// </example>
    public const string ErrorsTotal = "grpc.server.errors.total";
}
