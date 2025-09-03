// <copyright file="IAsyncTextWriter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.JsonConsole.Abstractions;

/// <summary>
/// Defines an abstraction for asynchronously writing lines of text.
/// </summary>
/// <remarks>
/// <para>
/// This interface is designed to provide a uniform abstraction for writing
/// text output asynchronously.  
/// </para>
/// <para>
/// Implementations may target various sinks such as:
/// </para>
/// <list type="bullet">
///   <item><description>Console output (e.g., <c>Console.WriteLine</c>).</description></item>
///   <item><description>File systems (e.g., <c>StreamWriter</c>).</description></item>
///   <item><description>Network sockets or HTTP streams.</description></item>
///   <item><description>In-memory buffers for testing or aggregation.</description></item>
/// </list>
/// <para>
/// Exporters in the <c>NetMetric</c> library rely on this abstraction to
/// decouple metric serialization from the underlying transport or storage
/// mechanism.
/// </para>
/// <para>
/// Implementations should ensure thread-safety if multiple concurrent
/// write operations are expected.
/// </para>
/// </remarks>
/// <example>
/// The following example demonstrates how to implement a simple
/// <see cref="IAsyncTextWriter"/> that writes to the console:
/// <code language="csharp">
/// public sealed class ConsoleTextWriter : IAsyncTextWriter
/// {
///     public Task WriteLineAsync(string text, CancellationToken ct = default)
///     {
///         Console.WriteLine(text);
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </example>
public interface IAsyncTextWriter
{
    /// <summary>
    /// Writes a single line of text asynchronously.
    /// </summary>
    /// <param name="text">
    /// The text line to write.  
    /// Implementations should write the line followed by a newline terminator.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the write operation
    /// before it completes. Implementations should respect cancellation
    /// requests where applicable.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous write operation.
    /// The task completes when the line has been successfully written or
    /// when the operation is canceled.
    /// </returns>
    Task WriteLineAsync(string text, CancellationToken ct = default);
}
