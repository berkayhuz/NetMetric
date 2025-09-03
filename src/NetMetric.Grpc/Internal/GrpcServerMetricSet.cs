// <copyright file="GrpcServerMetricSet.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Grpc.Internal;

/// <summary>
/// Creates and caches gRPC server metrics (counters and histograms) keyed by service,
/// method, call type, and additional dimensions such as status code, direction, or exception type.
/// </summary>
/// <remarks>
/// <para>
/// This type is intended to be a lightweight, allocation-aware helper for gRPC server
/// instrumentation. It exposes factory methods that return strongly-typed metric
/// instruments (counters and histograms) with a stable set of tag keys
/// defined in <see cref="GrpcTagKeys"/> and metric names defined in <see cref="GrpcMetricNames"/>.
/// </para>
/// <para>
/// Histogram bucket boundaries for latency (milliseconds) and message size (bytes) are
/// provided at construction time. If the boundaries appear to be a linear sequence
/// (constant step), they are applied via <see cref="IBucketHistogramBuilder.Linear(double, double, int)"/>; otherwise,
/// they are applied via <see cref="IBucketHistogramBuilder.WithBounds(double[])"/>.
/// </para>
/// </remarks>
/// <threadsafety>
/// This type is thread-safe. All metric instances are cached in <see cref="ConcurrentDictionary{TKey, TValue}"/>
/// and may be concurrently accessed from multiple gRPC request pipelines.
/// </threadsafety>
/// <example>
/// The following example shows how to observe call duration and increment the call counter:
/// <code language="csharp">
/// var set = new GrpcServerMetricSet(factory,
///     latencyBucketsMs: new double[] { 1, 5, 10, 20, 50, 100, 250, 500, 1000 },
///     sizeBuckets: new double[]   { 64, 128, 256, 512, 1024, 4096, 16384 });
///
/// var svc    = "MyCompany.UserService";
/// var method = "GetUser";
/// var type   = "unary";
/// var code   = "0"; // StatusCode.OK
///
/// var swTs = Stopwatch.GetTimestamp();
/// // ... handle the call ...
/// var elapsedMs = Stopwatch.GetElapsedTime(swTs).TotalMilliseconds;
///
/// set.Duration(svc, method, type, code).Observe(elapsedMs);
/// set.Calls(svc, method, type, code).Increment();
/// </code>
/// </example>
/// <seealso cref="GrpcMetricNames"/>
/// <seealso cref="GrpcTagKeys"/>
public sealed class GrpcServerMetricSet
{
    private readonly IMetricFactory _factory;
    private readonly double[] _latencyBucketsMs;
    private readonly double[] _sizeBuckets;

    private readonly ConcurrentDictionary<(string svc, string method, string type, string code), IBucketHistogramMetric> _dur = new();
    private readonly ConcurrentDictionary<(string svc, string method, string type, string code), ICounterMetric> _tot = new();
    private readonly ConcurrentDictionary<(string svc, string method, string type, string dir), IBucketHistogramMetric> _size = new();
    private readonly ConcurrentDictionary<(string svc, string method, string type, string dir), ICounterMetric> _msg = new();
    private readonly ConcurrentDictionary<(string svc, string method, string type, string ex), ICounterMetric> _err = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcServerMetricSet"/> class.
    /// </summary>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to create metric instruments.</param>
    /// <param name="latencyBucketsMs">Histogram bucket boundaries for request latency in milliseconds.</param>
    /// <param name="sizeBuckets">Histogram bucket boundaries for message sizes in bytes.</param>
    /// <remarks>
    /// <para>
    /// Passing <see langword="null"/> for <paramref name="latencyBucketsMs"/> or
    /// <paramref name="sizeBuckets"/> results in an empty boundary list, leaving the
    /// histogram builder to its defaults.
    /// </para>
    /// <para>
    /// Bucket detection is performed per instrument creation. Linear sequences are applied with
    /// <see cref="IBucketHistogramBuilder.Linear(double, double, int)"/>; otherwise the exact boundaries are used.
    /// </para>
    /// </remarks>
    public GrpcServerMetricSet(IMetricFactory factory, double[] latencyBucketsMs, double[] sizeBuckets)
    {
        _factory = factory;
        _latencyBucketsMs = latencyBucketsMs?.ToArray() ?? Array.Empty<double>();
        _sizeBuckets = sizeBuckets?.ToArray() ?? Array.Empty<double>();
    }

    /// <summary>
    /// Gets or creates the counter instrument that tracks the total number of gRPC calls.
    /// </summary>
    /// <param name="svc">The fully qualified gRPC service name (e.g., <c>MyCompany.UserService</c>).</param>
    /// <param name="method">The gRPC method name (e.g., <c>GetUser</c>).</param>
    /// <param name="type">The gRPC call type (e.g., <c>unary</c>, <c>server_streaming</c>, <c>client_streaming</c>, <c>duplex</c>).</param>
    /// <param name="code">The gRPC status code as a numeric string (e.g., <c>"0"</c> for OK).</param>
    /// <returns>An <see cref="ICounterMetric"/> representing the call count.</returns>
    /// <remarks>
    /// Tags applied: <c>{service, method, type, code}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// set.Calls("MyCompany.UserService", "GetUser", "unary", "0").Increment();
    /// </code>
    /// </example>
    public ICounterMetric Calls(string svc, string method, string type, string code)
    {
        var key = (svc, method, type, code);
        return _tot.GetOrAdd(key, _ =>
            _factory.Counter(GrpcMetricNames.CallsTotal, "gRPC server calls total")
                .WithTags(t =>
                {
                    t.Add(GrpcTagKeys.Service, svc);
                    t.Add(GrpcTagKeys.Method, method);
                    t.Add(GrpcTagKeys.Type, type);
                    t.Add(GrpcTagKeys.Code, code);
                })
                .Build());
    }

    /// <summary>
    /// Gets or creates the histogram instrument that measures gRPC call duration.
    /// </summary>
    /// <param name="svc">The fully qualified gRPC service name.</param>
    /// <param name="method">The gRPC method name.</param>
    /// <param name="type">The gRPC call type.</param>
    /// <param name="code">The gRPC status code as a numeric string.</param>
    /// <returns>An <see cref="IBucketHistogramMetric"/> measuring call latency in milliseconds.</returns>
    /// <remarks>
    /// <para>Unit: <c>ms</c>.</para>
    /// <para>Tags applied: <c>{service, method, type, code}</c>.</para>
    /// Buckets are taken from <see cref="_latencyBucketsMs"/> and applied via <see cref="ApplyBounds(IInstrumentBuilder{IBucketHistogramMetric}, double[])"/>.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var h = set.Duration("MyCompany.UserService", "GetUser", "unary", "0");
    /// h.Observe(12.7);
    /// </code>
    /// </example>
    public IBucketHistogramMetric Duration(string svc, string method, string type, string code)
    {
        var key = (svc, method, type, code);
        return _dur.GetOrAdd(key, _ =>
            ApplyBounds(
                _factory.Histogram(GrpcMetricNames.CallDuration, "gRPC server call duration (ms)")
                        .WithUnit("ms")
                        .WithTags(t =>
                        {
                            t.Add(GrpcTagKeys.Service, svc);
                            t.Add(GrpcTagKeys.Method, method);
                            t.Add(GrpcTagKeys.Type, type);
                            t.Add(GrpcTagKeys.Code, code);
                        }),
                _latencyBucketsMs)
            .Build());
    }

    /// <summary>
    /// Gets or creates the histogram instrument that measures gRPC message sizes.
    /// </summary>
    /// <param name="svc">The fully qualified gRPC service name.</param>
    /// <param name="method">The gRPC method name.</param>
    /// <param name="type">The gRPC call type.</param>
    /// <param name="dir">The message direction: <c>request</c> or <c>response</c>.</param>
    /// <returns>An <see cref="IBucketHistogramMetric"/> measuring message size in bytes.</returns>
    /// <remarks>
    /// <para>Unit: <c>bytes</c>.</para>
    /// <para>Tags applied: <c>{service, method, type, direction}</c>.</para>
    /// Buckets are taken from <see cref="_sizeBuckets"/> and applied via <see cref="ApplyBounds(IInstrumentBuilder{IBucketHistogramMetric}, double[])"/>.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// set.SizeHistogram("MyCompany.UserService", "StreamUsers", "server_streaming", "response")
    ///    .Observe(2048);
    /// </code>
    /// </example>
    public IBucketHistogramMetric SizeHistogram(string svc, string method, string type, string dir)
    {
        var key = (svc, method, type, dir);
        return _size.GetOrAdd(key, _ =>
            ApplyBounds(
                _factory.Histogram(GrpcMetricNames.MessageSize, "gRPC server message size (bytes)")
                        .WithUnit("bytes")
                        .WithTags(t =>
                        {
                            t.Add(GrpcTagKeys.Service, svc);
                            t.Add(GrpcTagKeys.Method, method);
                            t.Add(GrpcTagKeys.Type, type);
                            t.Add(GrpcTagKeys.Direction, dir);
                        }),
                _sizeBuckets)
            .Build());
    }

    /// <summary>
    /// Gets or creates the counter instrument that tracks the number of gRPC messages.
    /// </summary>
    /// <param name="svc">The fully qualified gRPC service name.</param>
    /// <param name="method">The gRPC method name.</param>
    /// <param name="type">The gRPC call type.</param>
    /// <param name="dir">The message direction: <c>request</c> or <c>response</c>.</param>
    /// <returns>An <see cref="ICounterMetric"/> representing the message count.</returns>
    /// <remarks>
    /// Tags applied: <c>{service, method, type, direction}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// set.MessagesCounter("MyCompany.UserService", "Upload", "client_streaming", "request").Increment();
    /// </code>
    /// </example>
    public ICounterMetric MessagesCounter(string svc, string method, string type, string dir)
    {
        var key = (svc, method, type, dir);
        return _msg.GetOrAdd(key, _ =>
            _factory.Counter(GrpcMetricNames.MessagesTotal, "gRPC server messages total")
                .WithTags(t =>
                {
                    t.Add(GrpcTagKeys.Service, svc);
                    t.Add(GrpcTagKeys.Method, method);
                    t.Add(GrpcTagKeys.Type, type);
                    t.Add(GrpcTagKeys.Direction, dir);
                })
                .Build());
    }

    /// <summary>
    /// Gets or creates the counter instrument that tracks gRPC error occurrences.
    /// </summary>
    /// <param name="svc">The fully qualified gRPC service name.</param>
    /// <param name="method">The gRPC method name.</param>
    /// <param name="type">The gRPC call type.</param>
    /// <param name="exceptionType">The short exception type name (e.g., <c>RpcException</c>).</param>
    /// <returns>An <see cref="ICounterMetric"/> representing the error count.</returns>
    /// <remarks>
    /// Tags applied: <c>{service, method, type, exception}</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// set.ErrorsCounter("MyCompany.UserService", "GetUser", "unary", "RpcException").Increment();
    /// </code>
    /// </example>
    public ICounterMetric ErrorsCounter(string svc, string method, string type, string exceptionType)
    {
        var key = (svc, method, type, exceptionType);
        return _err.GetOrAdd(key, _ =>
            _factory.Counter(GrpcMetricNames.ErrorsTotal, "gRPC server errors total")
                .WithTags(t =>
                {
                    t.Add(GrpcTagKeys.Service, svc);
                    t.Add(GrpcTagKeys.Method, method);
                    t.Add(GrpcTagKeys.Type, type);
                    t.Add(GrpcTagKeys.Exception, exceptionType);
                })
                .Build());
    }

    /// <summary>
    /// Applies bucket bounds to a histogram builder, falling back to explicit bounds if a linear sequence cannot be established.
    /// </summary>
    /// <param name="b">The histogram instrument builder.</param>
    /// <param name="bounds">The bucket boundary array to apply.</param>
    /// <returns>
    /// The same builder instance with bucket bounds applied. If the bounds form a linear sequence,
    /// <see cref="IBucketHistogramBuilder.Linear(double, double, int)"/> is used; otherwise,
    /// <see cref="IBucketHistogramBuilder.WithBounds(double[])"/> is used.
    /// </returns>
    /// <remarks>
    /// If <paramref name="bounds"/> is <see langword="null"/> or has fewer than two entries,
    /// no bounds are applied and the builder is returned unchanged.
    /// </remarks>
    private static IInstrumentBuilder<IBucketHistogramMetric> ApplyBounds(
        IInstrumentBuilder<IBucketHistogramMetric> b, double[] bounds)
    {
        if (bounds is { Length: >= 2 } && b is IBucketHistogramBuilder hb)
        {
            var step = bounds[1] - bounds[0];
            if (step > 0)
            {
                bool linear = true;
                for (int i = 2; i < bounds.Length; i++)
                {
                    var d = bounds[i] - bounds[i - 1];
                    if (Math.Abs(d - step) > 1e-9)
                    {
                        linear = false;
                        break;
                    }
                }
                if (linear)
                    return hb.Linear(bounds[0], step, bounds.Length);
            }
            return hb.WithBounds(bounds);
        }
        return b;
    }
}
