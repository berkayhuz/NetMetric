// <copyright file="TimedReadStream.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Network.Http;

/// <summary>
/// A custom <see cref="Stream"/> that tracks the timing of data transfer during reading operations.
/// It records metrics for the total transfer time and the time spent reading the response headers.
/// </summary>
internal sealed class TimedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly ITimerSink _sink;
    private readonly string _idTotal, _nameTotal;
    private readonly string _idTransfer, _nameTransfer;
    private readonly IReadOnlyDictionary<string, string> _baseTags;
    private readonly long _startTicks;
    private readonly long _headersTicks;
    private readonly bool _tagBytes;

    private long _bytes;
    private int _finished;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimedReadStream"/> class.
    /// </summary>
    /// <param name="inner">The inner <see cref="Stream"/> to wrap.</param>
    /// <param name="sink">The <see cref="ITimerSink"/> to record timing metrics.</param>
    /// <param name="total">The total timing metric ID and name.</param>
    /// <param name="transfer">The transfer timing metric ID and name.</param>
    /// <param name="tags">The base tags to be used for metrics.</param>
    /// <param name="startTicks">The starting timestamp for the entire transfer.</param>
    /// <param name="headersTicks">The timestamp for when headers were received.</param>
    /// <param name="tagBytes">Whether to include the byte count in the tags.</param>
    /// <exception cref="ArgumentNullException">Thrown if any required parameter is null.</exception>
    public TimedReadStream(Stream inner, ITimerSink sink, (string id, string name) total, (string id, string name) transfer, IReadOnlyDictionary<string, string> tags, long startTicks, long headersTicks, bool tagBytes)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        (_idTotal, _nameTotal) = total;
        (_idTransfer, _nameTransfer) = transfer;
        _baseTags = tags ?? throw new ArgumentNullException(nameof(tags));
        _startTicks = startTicks;
        _headersTicks = headersTicks;
        _tagBytes = tagBytes;
    }

    /// <summary>
    /// Asynchronously reads a sequence of bytes from the current stream and advances the position within the stream.
    /// It also tracks the number of bytes read and calculates the transfer time when reading is complete.
    /// </summary>
    /// <param name="buffer">The buffer to read the data into.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous read operation. The value of the task is the number of bytes read.</returns>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (n > 0)
        {
            _bytes += n;
        }
        else if (n == 0)
        {
            FinishOnce();
        }

        return n;
    }

    /// <summary>
    /// Reads a sequence of bytes from the current stream and advances the position within the stream.
    /// It also tracks the number of bytes read and calculates the transfer time when reading is complete.
    /// </summary>
    /// <param name="buffer">The buffer to read the data into.</param>
    /// <param name="offset">The zero-based byte offset in the buffer at which to begin storing data.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = _inner.Read(buffer, offset, count);

        if (n > 0)
        {
            _bytes += n;
        }

        else if (n == 0)
        {
            FinishOnce();
        }

        return n;
    }

    /// <summary>
    /// Marks the stream as finished by recording the total and transfer times to the <see cref="ITimerSink"/>.
    /// This method is called once after the stream is fully read.
    /// </summary>
    private void FinishOnce()
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0)
        {
            return;
        }

        var end = Stopwatch.GetTimestamp();
        var transferMs = (end - _headersTicks) * TimeUtil.TicksToMs;
        var totalMs = (end - _startTicks) * TimeUtil.TicksToMs;

        IReadOnlyDictionary<string, string> tagsToSend = _baseTags;

        if (_tagBytes)
        {
            var dict = _baseTags is FrozenDictionary<string, string> fz ? new Dictionary<string, string>(fz) : new Dictionary<string, string>(_baseTags);

            dict["bytes"] = _bytes.ToString();

            tagsToSend = dict;
        }

        // Record transfer time and total time
        _sink.Record(_idTransfer, _nameTransfer, transferMs, tagsToSend);
        _sink.Record(_idTotal, _nameTotal, totalMs, tagsToSend);
    }

    /// <summary>
    /// Releases all resources used by the current instance of the <see cref="TimedReadStream"/>.
    /// </summary>
    /// <param name="disposing">Indicates whether the method is being called from the <see cref="Dispose"/> method.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            FinishOnce();
        }

        _inner.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>
    /// Asynchronously releases all resources used by the current instance of the <see cref="TimedReadStream"/>.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public override async ValueTask DisposeAsync()
    {
        FinishOnce();

        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a value indicating whether the current stream can be read.
    /// </summary>
    public override bool CanRead => _inner.CanRead;

    /// <summary>
    /// Gets a value indicating whether the current stream supports seeking.
    /// </summary>
    public override bool CanSeek => _inner.CanSeek;

    /// <summary>
    /// Gets a value indicating whether the current stream supports writing.
    /// </summary>
    public override bool CanWrite => false;

    /// <summary>
    /// Gets the length of the current stream in bytes.
    /// </summary>
    public override long Length => _inner.Length;

    /// <summary>
    /// Gets or sets the current position within the stream.
    /// </summary>
    public override long Position
    {
        get => _inner.Position; set => _inner.Position = value;
    }

    /// <summary>
    /// Flushes all buffers for the current stream to the underlying device.
    /// </summary>
    public override void Flush() => _inner.Flush();

    /// <summary>
    /// Asynchronously flushes all buffers for the current stream to the underlying device.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous flush operation.</returns>
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <summary>
    /// Sets the position within the current stream.
    /// </summary>
    /// <param name="offset">The new position within the stream.</param>
    /// <param name="origin">The reference point used to calculate the new position.</param>
    /// <returns>The new position within the stream.</returns>
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    /// <summary>
    /// Sets the length of the current stream.
    /// </summary>
    /// <param name="value">The desired length of the current stream.</param>
    public override void SetLength(long value) => _inner.SetLength(value);

    /// <summary>
    /// Throws a <see cref="NotSupportedException"/> as writing is not supported by this stream.
    /// </summary>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="offset">The offset in the buffer at which to start writing.</param>
    /// <param name="count">The number of bytes to write.</param>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// Throws a <see cref="NotSupportedException"/> as writing is not supported by this stream.
    /// </summary>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException());
}
