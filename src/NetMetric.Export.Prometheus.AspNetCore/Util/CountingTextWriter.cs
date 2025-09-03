// <copyright file="CountingTextWriter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Text;

namespace NetMetric.Export.Prometheus.AspNetCore.Util;

/// <summary>
/// A specialized <see cref="StreamWriter"/> that tracks the total number of bytes
/// that would be produced when the written text is encoded as UTF-8,
/// regardless of the writer's configured <see cref="Encoding"/>.
/// </summary>
/// <remarks>
/// <para>
/// This writer is useful in scenarios where the serialized payload size must be tracked,
/// such as Prometheus metric scrapes or any endpoint with explicit byte budgets or limits.
/// </para>
/// <para>
/// <b>Counting model.</b> The <see cref="TotalBytes"/> counter is incremented on every
/// <see cref="Write(char)"/>, <see cref="Write(char[])"/>, <see cref="Write(char[], int, int)"/>,
/// and <see cref="Write(string)"/> call based on the <em>UTF-8</em> byte length of the provided data
/// (<see cref="Encoding.GetByteCount(string)"/> and related APIs). This means:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <see cref="TotalBytes"/> reflects the number of bytes the <em>text content</em>
///     would occupy when encoded as UTF-8.
///     </description>
///   </item>
///   <item>
///     <description>
///     If the underlying writer's <see cref="StreamWriter.Encoding"/> is not UTF-8,
///     <see cref="TotalBytes"/> may differ from the actual bytes the base writer emits.
///     For accurate accounting, configure the writer with <see cref="Encoding.UTF8"/>.
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>Thread safety.</b> Instances of <see cref="CountingTextWriter"/> are not thread-safe.
/// Coordinate external synchronization if accessed concurrently.
/// </para>
/// <para>
/// <b>Performance notes.</b> Counting uses span-based overloads and avoids intermediate string allocations.
/// A small <c>stackalloc</c> is used for single-character writes.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using System.Text;
/// using NetMetric.Export.Prometheus.AspNetCore.Util;
///
/// // Measure the UTF-8 payload size of a metrics response
/// var ms = new MemoryStream(capacity: 64 * 1024);
/// await using var writer = new CountingTextWriter(ms, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
///
/// writer.Write("# HELP http_requests_total Total HTTP requests\n");
/// writer.Write("# TYPE http_requests_total counter\n");
/// writer.Write("http_requests_total{method=\"GET\",code=\"200\"} 42\n");
/// writer.Flush(); // ensure data is pushed to the underlying stream
///
/// long utf8Bytes = writer.TotalBytes; // number of UTF-8 bytes written
/// // Optionally, assert a limit:
/// // if (utf8Bytes &gt; 1_000_000) { /* apply backpressure or truncate */ }
/// </code>
/// </example>
/// <seealso cref="StreamWriter"/>
/// <seealso cref="Encoding.UTF8"/>
internal sealed class CountingTextWriter : StreamWriter
{
    /// <summary>
    /// Gets the cumulative number of bytes (as if encoded using <see cref="Encoding.UTF8"/>)
    /// accounted for by all successful <c>Write</c> operations since construction.
    /// </summary>
    /// <value>
    /// A non-negative, monotonically increasing byte count. The value does not decrease
    /// and is not reset by <see cref="StreamWriter.Flush()"/> or disposal.
    /// </value>
    public long TotalBytes { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CountingTextWriter"/> class.
    /// </summary>
    /// <param name="stream">The underlying output <see cref="Stream"/>.</param>
    /// <param name="encoding">
    /// The character <see cref="Encoding"/> for the base <see cref="StreamWriter"/>.
    /// For accurate byte accounting against actual output, use <see cref="Encoding.UTF8"/>.
    /// </param>
    /// <param name="bufferSize">The internal buffer size, in bytes, for the base writer.</param>
    /// <param name="leaveOpen">
    /// When <see langword="true"/>, the underlying <paramref name="stream"/> remains open after the writer is disposed;
    /// otherwise, disposing the writer also disposes the stream.
    /// </param>
    public CountingTextWriter(Stream stream, Encoding encoding, int bufferSize, bool leaveOpen)
        : base(stream, encoding, bufferSize, leaveOpen) { }

    /// <inheritdoc/>
    public override void Write(char value)
    {
        base.Write(value);
        TotalBytes += Encoding.UTF8.GetByteCount(stackalloc char[] { value });
    }

    /// <inheritdoc/>
    public override void Write(char[]? buffer)
    {
        if (buffer is not null)
            TotalBytes += Encoding.UTF8.GetByteCount(buffer);
        base.Write(buffer);
    }

    /// <inheritdoc/>
    public override void Write(char[] buffer, int index, int count)
    {
        if (count > 0)
            TotalBytes += Encoding.UTF8.GetByteCount(buffer.AsSpan(index, count));
        base.Write(buffer, index, count);
    }

    /// <inheritdoc/>
    public override void Write(string? value)
    {
        if (value is not null)
            TotalBytes += Encoding.UTF8.GetByteCount(value);
        base.Write(value);
    }
}
