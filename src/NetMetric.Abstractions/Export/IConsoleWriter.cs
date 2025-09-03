// <copyright file="IConsoleWriter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Abstraction for writing text to a console or console-like output.
/// </summary>
public interface IConsoleWriter
{
    /// <summary>
    /// Writes a line of text asynchronously.
    /// </summary>
    /// <param name="text">The text to write.</param>
    Task WriteLineAsync(string text);
}
