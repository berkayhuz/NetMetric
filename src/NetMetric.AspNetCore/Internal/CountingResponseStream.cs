// <copyright file="CountingResponseStream.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AspNetCore.Internal;

/// <summary>
/// A <see cref="Stream"/> wrapper that delegates writes to an inner stream
/// while counting the total number of bytes written.
/// </summary>
/// <remarks>
/// <para>
/// This stream is write-only: read and seek operations are not supported.  
/// It is typically used to wrap <see cref="Microsoft.AspNetCore.Http.HttpResponse.Body"/>
/// in order to measure response payload size in ASP.NET Core middleware or filters.
/// </para>
/// <para>
/// The byte counter is updated using <see cref="Interlocked"/> so increments are thread-safe.
/// Note, however, that thread safety of the underlying I/O depends on the wrapped <see cref="Stream"/>.
/// </para>
/// <para>
/// <strong>Ownership:</strong> Disposing this stream also disposes the inner stream.
/// </para>
/// </remarks>
internal sealed class CountingResponseStream : Stream
{
    private readonly Stream _inner;
    private long _bytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="CountingResponseStream"/> class.
    /// </summary>
    /// <param name="inner">The underlying <see cref="Stream"/> to which writes are forwarded.</param>
    /// <remarks>
    /// The constructor does not validate <paramref name="inner"/> for <see langword="null"/>.
    /// Passing <see langword="null"/> will lead to failures when members are accessed.
    /// </remarks>
    public CountingResponseStream(Stream inner) => _inner = inner;

    /// <summary>
    /// Gets the total number of bytes written to the stream so far.
    /// </summary>
    /// <remarks>
    /// This value is updated atomically and can be read safely from concurrent contexts.
    /// </remarks>
    public long BytesWritten => Interlocked.Read(ref _bytes);

    /// <inheritdoc/>
    /// <summary>
    /// Gets a value indicating whether the current stream supports reading.
    /// </summary>
    /// <value>Always <see langword="false"/>.</value>
    public override bool CanRead => false;

    /// <inheritdoc/>
    /// <summary>
    /// Gets a value indicating whether the current stream supports seeking.
    /// </summary>
    /// <value>Always <see langword="false"/>.</value>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    /// <summary>
    /// Gets a value indicating whether the current stream supports writing.
    /// </summary>
    /// <value>Mirrors <see cref="_inner"/>.<see cref="Stream.CanWrite"/>.</value>
    public override bool CanWrite => _inner.CanWrite;

    /// <inheritdoc/>
    /// <summary>
    /// Gets the length in bytes of the stream.
    /// </summary>
    /// <value>Mirrors <see cref="_inner"/>.<see cref="Stream.Length"/>.</value>
    public override long Length => _inner.Length;

    /// <inheritdoc/>
    /// <summary>
    /// Gets or sets the current position within the stream.
    /// </summary>
    /// <value>Returns the position reported by the inner stream.</value>
    /// <exception cref="NotSupportedException">Always thrown when attempting to set the position.</exception>
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    /// <summary>
    /// Clears all buffers for this stream and causes any buffered data to be written to the underlying device.
    /// </summary>
    public override void Flush() => _inner.Flush();

    /// <inheritdoc/>
    /// <summary>
    /// Asynchronously clears all buffers for this stream and causes any buffered data to be written to the underlying device.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    /// <summary>
    /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
    /// </summary>
    /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between <paramref name="offset"/> and (<paramref name="offset"/> + <paramref name="count"/> - 1) replaced by the bytes read from the current source.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin storing the data read from the current stream.</param>
    /// <param name="count">The maximum number of bytes to be read from the current stream.</param>
    /// <returns>The total number of bytes read into the buffer.</returns>
    /// <exception cref="NotSupportedException">Always thrown; reading is not supported.</exception>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    /// <summary>
    /// Sets the position within the current stream.
    /// </summary>
    /// <param name="offset">A byte offset relative to the <paramref name="origin"/> parameter.</param>
    /// <param name="origin">A value of type <see cref="SeekOrigin"/> indicating the reference point used to obtain the new position.</param>
    /// <returns>The new position within the current stream.</returns>
    /// <exception cref="NotSupportedException">Always thrown; seeking is not supported.</exception>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    /// <summary>
    /// Sets the length of the current stream.
    /// </summary>
    /// <param name="value">The desired length of the current stream in bytes.</param>
    public override void SetLength(long value) => _inner.SetLength(value);

    /// <inheritdoc/>
    /// <summary>
    /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
    /// </summary>
    /// <param name="buffer">An array of bytes. This method copies <paramref name="count"/> bytes from <paramref name="buffer"/> to the current stream.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin copying bytes to the current stream.</param>
    /// <param name="count">The number of bytes to be written to the current stream.</param>
    /// <remarks>
    /// The byte counter is incremented by <paramref name="count"/> using <see cref="Interlocked.Add(ref long, long)"/>.
    /// </remarks>
    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        Interlocked.Add(ref _bytes, count);
    }

    /// <inheritdoc/>
    /// <summary>
    /// Asynchronously writes a sequence of bytes to the current stream, advances the current position, and updates the byte counter.
    /// </summary>
    /// <param name="buffer">The region of memory to write to the stream.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous write operation.</returns>
    /// <remarks>
    /// The byte counter is incremented by <see cref="ReadOnlyMemory{T}.Length"/> regardless of cancellation results; if the write is canceled or fails,
    /// the counter may not reflect the actual number of bytes committed by the underlying stream.
    /// </remarks>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var vt = _inner.WriteAsync(buffer, cancellationToken);
        Interlocked.Add(ref _bytes, buffer.Length);
        return vt;
    }

#if NET9_0_OR_GREATER
    /// <inheritdoc/>
    /// <summary>
    /// Writes a sequence of bytes from a read-only span to the current stream and advances the position accordingly.
    /// </summary>
    /// <param name="buffer">A read-only span of bytes to write to the current stream.</param>
    /// <remarks>
    /// The byte counter is incremented by <paramref name="buffer"/>.<see cref="ReadOnlySpan{T}.Length"/>.
    /// </remarks>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _inner.Write(buffer);
        Interlocked.Add(ref _bytes, buffer.Length);
    }
#endif

    /// <inheritdoc/>
    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="CountingResponseStream"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.
    /// </param>
    /// <remarks>
    /// Disposes the inner stream when <paramref name="disposing"/> is <see langword="true"/>.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

#if NET9_0_OR_GREATER
    /// <inheritdoc/>
    /// <summary>
    /// Asynchronously releases the unmanaged resources used by the <see cref="CountingResponseStream"/> and optionally releases the managed resources.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous dispose operation.</returns>
    /// <remarks>
    /// Disposes the inner stream asynchronously before calling the base implementation.
    /// </remarks>
    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
#endif
}
