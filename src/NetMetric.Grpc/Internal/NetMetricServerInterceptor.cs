// <copyright file="NetMetricServerInterceptor.cs" company="NetMetric"
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace NetMetric.Grpc.Internal;

/// <summary>
/// A gRPC server interceptor that records metrics for calls, including
/// durations, message sizes, message counts, and errors.
/// </summary>
/// <remarks>
/// <para>
/// This interceptor wraps all gRPC server handler types (unary, server streaming,
/// client streaming, and duplex streaming) to capture metrics defined in
/// <see cref="GrpcServerMetricSet"/>.
/// </para>
/// <para>
/// Metrics collected include:
/// <list type="bullet">
/// <item><description>Call duration histograms</description></item>
/// <item><description>Call counters</description></item>
/// <item><description>Message size histograms (request/response)</description></item>
/// <item><description>Message counters (request/response)</description></item>
/// <item><description>Error counters (tagged by exception type)</description></item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Example of registering the interceptor in a gRPC server:
/// <code language="csharp">
/// var metrics = new GrpcServerMetricSet(factory, latencyBuckets, sizeBuckets);
/// var interceptor = new NetMetricServerInterceptor(metrics);
///
/// var server = new Server
/// {
///     Services = { MyService.BindService(new MyServiceImpl()).Intercept(interceptor) },
///     Ports = { new ServerPort("localhost", 5000, ServerCredentials.Insecure) }
/// };
/// server.Start();
/// </code>
/// </example>
public sealed class NetMetricServerInterceptor : Interceptor
{
    private readonly GrpcServerMetricSet _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetMetricServerInterceptor"/> class.
    /// </summary>
    /// <param name="metrics">The <see cref="GrpcServerMetricSet"/> used to record gRPC metrics.</param>
    public NetMetricServerInterceptor(GrpcServerMetricSet metrics) => _metrics = metrics;

    // ------------------------ Unary ------------------------

    /// <inheritdoc />
    /// <summary>
    /// Intercepts unary server calls to measure request and response message sizes,
    /// call duration, and errors.
    /// </summary>
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        var (svc, method) = Parse(context.Method);
        const string type = "unary";
        var ts = Stopwatch.GetTimestamp();

        // Record request
        ObserveMessageIfAny(svc, method, type, direction: "request", request);

        try
        {
            var response = await continuation(request, context).ConfigureAwait(false);

            // Record response
            ObserveMessageIfAny(svc, method, type, direction: "response", response);
            ObserveCallEnd(svc, method, type, code: ((int)StatusCode.OK).ToString(), ts);
            return response;
        }
        catch (RpcException rpcEx)
        {
            ObserveCallEnd(svc, method, type, ((int)rpcEx.Status.StatusCode).ToString(), ts);
            if (rpcEx.Status.StatusCode != StatusCode.OK)
            {
                _metrics.ErrorsCounter(svc, method, type, nameof(RpcException)).Increment();
            }
            throw;
        }
        catch (Exception ex)
        {
            ObserveCallEnd(svc, method, type, ((int)StatusCode.Unknown).ToString(), ts);
            _metrics.ErrorsCounter(svc, method, type, ex.GetType().Name).Increment();
            throw;
        }
    }

    // ------------------------ Server Streaming ------------------------

    /// <inheritdoc />
    /// <summary>
    /// Intercepts server-streaming calls to measure request size, per-response
    /// message sizes, call duration, and errors.
    /// </summary>
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        var (svc, method) = Parse(context.Method);
        const string type = "server_streaming";
        var ts = Stopwatch.GetTimestamp();

        // Record single request
        ObserveMessageIfAny(svc, method, type, direction: "request", request);

        var countingWriter = new CountingServerStreamWriter<TResponse>(responseStream, bytes =>
        {
            _metrics.SizeHistogram(svc, method, type, "response").Observe(bytes);
            _metrics.MessagesCounter(svc, method, type, "response").Increment();
        });

        try
        {
            await continuation(request, countingWriter, context).ConfigureAwait(false);
            ObserveCallEnd(svc, method, type, ((int)StatusCode.OK).ToString(), ts);
        }
        catch (RpcException rpcEx)
        {
            ObserveCallEnd(svc, method, type, ((int)rpcEx.Status.StatusCode).ToString(), ts);
            if (rpcEx.Status.StatusCode != StatusCode.OK)
            {
                _metrics.ErrorsCounter(svc, method, type, nameof(RpcException)).Increment();
            }
            throw;
        }
        catch (Exception ex)
        {
            ObserveCallEnd(svc, method, type, ((int)StatusCode.Unknown).ToString(), ts);
            _metrics.ErrorsCounter(svc, method, type, ex.GetType().Name).Increment();
            throw;
        }
    }

    // ------------------------ Client Streaming ------------------------

    /// <inheritdoc />
    /// <summary>
    /// Intercepts client-streaming calls to measure per-request message sizes,
    /// response size, call duration, and errors.
    /// </summary>
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(requestStream);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        var (svc, method) = Parse(context.Method);
        const string type = "client_streaming";
        var ts = Stopwatch.GetTimestamp();

        var countingReader = new CountingAsyncStreamReader<TRequest>(requestStream, msgBytes =>
        {
            _metrics.SizeHistogram(svc, method, type, "request").Observe(msgBytes);
            _metrics.MessagesCounter(svc, method, type, "request").Increment();
        });

        try
        {
            var response = await continuation(countingReader, context).ConfigureAwait(false);
            ObserveMessageIfAny(svc, method, type, direction: "response", response);
            ObserveCallEnd(svc, method, type, ((int)StatusCode.OK).ToString(), ts);
            return response;
        }
        catch (RpcException rpcEx)
        {
            ObserveCallEnd(svc, method, type, ((int)rpcEx.Status.StatusCode).ToString(), ts);
            if (rpcEx.Status.StatusCode != StatusCode.OK)
            {
                _metrics.ErrorsCounter(svc, method, type, nameof(RpcException)).Increment();
            }
            throw;
        }
        catch (Exception ex)
        {
            ObserveCallEnd(svc, method, type, ((int)StatusCode.Unknown).ToString(), ts);
            _metrics.ErrorsCounter(svc, method, type, ex.GetType().Name).Increment();
            throw;
        }
    }

    // ------------------------ Duplex Streaming ------------------------

    /// <inheritdoc />
    /// <summary>
    /// Intercepts duplex-streaming calls to measure per-request and per-response
    /// message sizes, call duration, and errors.
    /// </summary>
    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(requestStream);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        var (svc, method) = Parse(context.Method);
        const string type = "duplex";
        var ts = Stopwatch.GetTimestamp();

        var countingReader = new CountingAsyncStreamReader<TRequest>(requestStream, msgBytes =>
        {
            _metrics.SizeHistogram(svc, method, type, "request").Observe(msgBytes);
            _metrics.MessagesCounter(svc, method, type, "request").Increment();
        });

        var countingWriter = new CountingServerStreamWriter<TResponse>(responseStream, bytes =>
        {
            _metrics.SizeHistogram(svc, method, type, "response").Observe(bytes);
            _metrics.MessagesCounter(svc, method, type, "response").Increment();
        });

        try
        {
            await continuation(countingReader, countingWriter, context).ConfigureAwait(false);
            ObserveCallEnd(svc, method, type, ((int)StatusCode.OK).ToString(), ts);
        }
        catch (RpcException rpcEx)
        {
            ObserveCallEnd(svc, method, type, ((int)rpcEx.Status.StatusCode).ToString(), ts);
            if (rpcEx.Status.StatusCode != StatusCode.OK)
            {
                _metrics.ErrorsCounter(svc, method, type, nameof(RpcException)).Increment();
            }
            throw;
        }
        catch (Exception ex)
        {
            ObserveCallEnd(svc, method, type, ((int)StatusCode.Unknown).ToString(), ts);
            _metrics.ErrorsCounter(svc, method, type, ex.GetType().Name).Increment();
            throw;
        }
    }

    // ------------------------ Helpers ------------------------

    /// <summary>
    /// Observes call end by recording duration and incrementing the call counter.
    /// </summary>
    private void ObserveCallEnd(string svc, string method, string type, string code, long tsStart)
    {
        var ms = Stopwatch.GetElapsedTime(tsStart).TotalMilliseconds;
        _metrics.Duration(svc, method, type, code).Observe(ms);
        _metrics.Calls(svc, method, type, code).Increment();
    }

    /// <summary>
    /// Observes a request or response message if available, recording size and count.
    /// </summary>
    private void ObserveMessageIfAny<T>(string svc, string method, string type, string direction, T? message)
    {
        if (message is null)
            return;
        var bytes = EstimateSize(message);
        if (bytes > 0)
        {
            _metrics.SizeHistogram(svc, method, type, direction).Observe(bytes);
        }
        _metrics.MessagesCounter(svc, method, type, direction).Increment();
    }

    /// <summary>
    /// Parses the gRPC method full name into service and method components.
    /// </summary>
    private static (string svc, string method) Parse(string fullName)
    {
        // Full name format is usually "/pkg.Service/Method"
        if (string.IsNullOrEmpty(fullName))
            return ("unknown", "unknown");
        var i = fullName.LastIndexOf('/');
        if (i <= 0 || i >= fullName.Length - 1)
            return (fullName.Trim('/'), "unknown");
        var svc = fullName.Substring(1, i - 1);
        var method = fullName[(i + 1)..];
        return (svc, method);
    }

    /// <summary>
    /// Estimates serialized size of a message, using Protobuf <see cref="IMessage"/> if available.
    /// </summary>
    private static int EstimateSize<T>(T message)
    {
        if (message is IMessage m)
            return m.CalculateSize();
        return 0;
    }
}

/// <summary>
/// Wraps an <see cref="IServerStreamWriter{T}"/> to measure message size and count
/// for each <c>WriteAsync</c> call.
/// </summary>
/// <typeparam name="T">The message type written to the stream.</typeparam>
/// <remarks>
/// Each call to <see cref="WriteAsync"/> computes the serialized size of the message
/// (if it implements <see cref="IMessage"/>) and invokes the callback.
/// </remarks>
internal sealed class CountingServerStreamWriter<T> : IServerStreamWriter<T>
{
    private readonly IServerStreamWriter<T> _inner;
    private readonly Action<int> _onBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="CountingServerStreamWriter{T}"/> class.
    /// </summary>
    public CountingServerStreamWriter(IServerStreamWriter<T> inner, Action<int> onBytes)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onBytes = onBytes ?? throw new ArgumentNullException(nameof(onBytes));
    }

    /// <inheritdoc />
    public WriteOptions? WriteOptions
    {
        get => _inner.WriteOptions;
        set => _inner.WriteOptions = value;
    }

    /// <inheritdoc />
    public async Task WriteAsync(T message)
    {
        var bytes = 0;
        if (message is IMessage m) bytes = m.CalculateSize();
        if (bytes > 0) _onBytes(bytes);
        await _inner.WriteAsync(message).ConfigureAwait(false);
    }
}

/// <summary>
/// Wraps an <see cref="IAsyncStreamReader{T}"/> to measure message size and count
/// for each message received via <see cref="MoveNext"/>.
/// </summary>
/// <typeparam name="T">The message type read from the stream.</typeparam>
/// <remarks>
/// Each call to <see cref="MoveNext"/> computes the serialized size of the current message
/// (if it implements <see cref="IMessage"/>) and invokes the callback.
/// </remarks>
internal sealed class CountingAsyncStreamReader<T> : IAsyncStreamReader<T>
{
    private readonly IAsyncStreamReader<T> _inner;
    private readonly Action<int> _onBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="CountingAsyncStreamReader{T}"/> class.
    /// </summary>
    /// <param name="inner">The underlying stream reader to wrap.</param>
    /// <param name="onBytes">Callback invoked with the serialized size of each message.</param>
    public CountingAsyncStreamReader(IAsyncStreamReader<T> inner, Action<int> onBytes)
    {
        _inner = inner;
        _onBytes = onBytes;
    }

    /// <inheritdoc />
    public T Current => _inner.Current;

    /// <inheritdoc />
    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        var has = await _inner.MoveNext(cancellationToken).ConfigureAwait(false);
        if (has)
        {
            var bytes = 0;
            if (_inner.Current is IMessage m)
                bytes = m.CalculateSize();
            if (bytes > 0)
                _onBytes(bytes);
        }
        return has;
    }
}
