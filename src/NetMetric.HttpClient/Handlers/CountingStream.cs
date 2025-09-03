// <copyright file="CountingStream.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.HttpClient.Handlers;

/// <summary>
/// A <see cref="Stream"/> wrapper that counts the total number of bytes read and
/// reports the elapsed time when the stream is disposed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CountingStream"/> forwards all operations to an inner stream while
/// tracking how many bytes were read via <see cref="Read(byte[], int, int)"/>,
/// <see cref="Read(Span{byte})"/>, <see cref="ReadAsync(byte[], int, int, CancellationToken)"/>, and
/// <see cref="ReadAsync(Memory{byte}, CancellationToken)"/>. When the wrapper is disposed, it invokes:
/// </para>
/// <list type="bullet">
///   <item><description><c>Action&lt;long&gt; onBytes</c> with the total number of bytes read.</description></item>
///   <item><description><c>Action&lt;double&gt; onCompleteMs</c> with the total elapsed milliseconds since construction.</description></item>
/// </list>
/// <para>
/// Only read operations are counted. Write operations are delegated to the inner stream
/// but are not included in the byte count.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> This type is not thread-safe. If multiple threads read from the same instance
/// concurrently, the byte counter may not represent a consistent snapshot.
/// </para>
/// </remarks>
/// <example>
/// Wrap a response stream to observe the downloaded size and download duration:
/// <code language="csharp"><![CDATA[
/// var start = Stopwatch.GetTimestamp();
/// using var wrapped = new CountingStream(
///     inner: responseStream,
///     onBytes: bytes => metrics.ObserveSize(bytes),
///     onCompleteMs: ms => metrics.ObservePhase("download", ms));
///
/// // Consume downstream (counting happens transparently)
/// await wrapped.CopyToAsync(Stream.Null, cancellationToken);
/// ]]></code>
/// </example>
internal sealed class CountingStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<long> _onBytes;
    private readonly Action<double> _onCompleteMs;
    private long _bytes;
    private readonly long _startTs;

    /// <summary>
    /// Initializes a new instance of the <see cref="CountingStream"/> class.
    /// </summary>
    /// <param name="inner">The inner readable stream to wrap and delegate to.</param>
    /// <param name="onBytes">Callback invoked on dispose with the total number of bytes read.</param>
    /// <param name="onCompleteMs">Callback invoked on dispose with total elapsed milliseconds since construction.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="inner"/>, <paramref name="onBytes"/>, or <paramref name="onCompleteMs"/> is <see langword="null"/>.
    /// </exception>
    public CountingStream(Stream inner, Action<long> onBytes, Action<double> onCompleteMs)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onBytes = onBytes ?? throw new ArgumentNullException(nameof(onBytes));
        _onCompleteMs = onCompleteMs ?? throw new ArgumentNullException(nameof(onCompleteMs));
        _startTs = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Disposes the wrapper, reports final metrics, and disposes the inner stream.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to dispose managed resources; otherwise, <see langword="false"/>.
    /// </param>
    /// <remarks>
    /// Invokes the supplied callbacks before disposing the inner stream. Safe to call multiple times;
    /// subsequent calls have no additional effect beyond the base implementation.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var ms = Stopwatch.GetElapsedTime(_startTs).TotalMilliseconds;
            _onBytes(_bytes);
            _onCompleteMs(ms);
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Asynchronously disposes the wrapper and the inner stream, reporting final metrics.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    /// <remarks>
    /// Invokes the callbacks before disposing the inner stream asynchronously.
    /// </remarks>
    public override async ValueTask DisposeAsync()
    {
        var ms = Stopwatch.GetElapsedTime(_startTs).TotalMilliseconds;
        _onBytes(_bytes);
        _onCompleteMs(ms);
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a sequence of bytes from the current stream asynchronously into the specified array segment.
    /// Delegates to the memory-based overload for efficiency.
    /// </summary>
    /// <param name="buffer">The buffer to write the data into.</param>
    /// <param name="offset">The byte offset in <paramref name="buffer"/> at which to begin writing.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous read operation and returns the total number of bytes read.</returns>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <summary>
    /// Reads a sequence of bytes from the current stream asynchronously into the specified memory.
    /// </summary>
    /// <param name="buffer">The region of memory to write the data into.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A value task that represents the asynchronous read operation and returns the number of bytes read.
    /// </returns>
    /// <remarks>
    /// If the operation completes synchronously, the result is returned without allocating an additional state machine.
    /// </remarks>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var vt = _inner.ReadAsync(buffer, cancellationToken);
        if (vt.IsCompletedSuccessfully)
        {
            var read = vt.Result;
            if (read > 0) _bytes += read;
            return ValueTask.FromResult(read);
        }
        return Awaited(vt);

        static async ValueTask<int> Awaited(ValueTask<int> pending)
        {
            var read = await pending.ConfigureAwait(false);
            return read;
        }
    }

    /// <summary>
    /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
    /// </summary>
    /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between <paramref name="offset"/> and (<paramref name="offset"/> + <paramref name="count"/> - 1) replaced by the bytes read from the current source.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin storing the data read from the current stream.</param>
    /// <param name="count">The maximum number of bytes to be read from the current stream.</param>
    /// <returns>The total number of bytes read into the buffer.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _inner.Read(buffer, offset, count);
        if (read > 0) _bytes += read;
        return read;
    }

    /// <summary>
    /// Reads a sequence of bytes from the current stream into the specified span.
    /// </summary>
    /// <param name="buffer">The span to write the data into.</param>
    /// <returns>The total number of bytes read into the span.</returns>
    public override int Read(Span<byte> buffer)
    {
        int read = _inner.Read(buffer);
        if (read > 0) _bytes += read;
        return read;
    }

    // Pass-through members

    /// <inheritdoc/>
    public override bool CanRead => _inner.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => _inner.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => _inner.CanWrite;

    /// <inheritdoc/>
    public override long Length => _inner.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    /// <inheritdoc/>
    public override void Flush() => _inner.Flush();

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    /// <inheritdoc/>
    public override void SetLength(long value) => _inner.SetLength(value);

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.WriteAsync(buffer, cancellationToken);

    /// <inheritdoc/>
    public override bool CanTimeout => _inner.CanTimeout;

    /// <inheritdoc/>
    public override int ReadTimeout
    {
        get => _inner.ReadTimeout;
        set => _inner.ReadTimeout = value;
    }

    /// <inheritdoc/>
    public override int WriteTimeout
    {
        get => _inner.WriteTimeout;
        set => _inner.WriteTimeout = value;
    }
}
