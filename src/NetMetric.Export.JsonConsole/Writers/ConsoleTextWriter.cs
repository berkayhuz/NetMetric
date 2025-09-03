// <copyright file="ConsoleTextWriter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.JsonConsole.Writers;

/// <summary>
/// Provides a concrete implementation of <see cref="IAsyncTextWriter"/> that writes text lines directly to the system console.
/// </summary>
/// <remarks>
/// <para>
/// This implementation leverages <see cref="Console.WriteLine(string?)"/> for output, which is inherently synchronous.  
/// However, it presents an asynchronous API surface by returning a completed <see cref="Task"/>.  
/// This design choice ensures compatibility with asynchronous pipelines while avoiding unnecessary thread usage.
/// </para>
/// <para>
/// The cancellation token parameter in <see cref="WriteLineAsync"/> is accepted for consistency with asynchronous method signatures,
/// but is currently ignored since <see cref="Console"/> I/O does not support cancellation.
/// </para>
/// <para>
/// Typical usage includes debugging, local development, or lightweight metric logging where
/// writing directly to the console is sufficient and performance considerations are minimal.
/// For high-throughput or production scenarios, consider using more advanced exporters
/// (e.g., file, network, or buffered writers).
/// </para>
/// </remarks>
/// <example>
/// Example usage:
/// <code language="csharp">
/// using NetMetric.Export.JsonConsole.Writers;
///
/// IAsyncTextWriter writer = new ConsoleTextWriter();
/// await writer.WriteLineAsync("{ \"metric\": \"requests\", \"value\": 42 }");
/// </code>
/// </example>
public sealed class ConsoleTextWriter : IAsyncTextWriter
{
    /// <summary>
    /// Writes a single line of text to the system console.
    /// </summary>
    /// <param name="text">The text to write. If <see langword="null"/>, an empty line will be written.</param>
    /// <param name="ct">
    /// A <see cref="CancellationToken"/> for cooperative cancellation.  
    /// Currently ignored as <see cref="Console.WriteLine(string?)"/> does not support cancellation.
    /// </param>
    /// <returns>
    /// A completed <see cref="Task"/> once the text has been written to the console.
    /// </returns>
    /// <remarks>
    /// This method is safe to call repeatedly and is thread-safe because <see cref="Console.WriteLine(string?)"/>
    /// synchronizes access internally.
    /// </remarks>
    public Task WriteLineAsync(string text, CancellationToken ct = default)
    {
        Console.WriteLine(text);
        return Task.CompletedTask;
    }
}
