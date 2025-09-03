// <copyright file="RealFileReader.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SystemIO.Abstractions;

internal sealed class RealFileReader : IFileReader
{
    /// <summary>
    /// Checks if a file exists at the specified path.
    /// </summary>
    /// <param name="path">The path to the file to check.</param>
    /// <returns>True if the file exists, otherwise false.</returns>
    public bool Exists(string path) => File.Exists(path);

    /// <summary>
    /// Reads all lines from a file at the specified path.
    /// </summary>
    /// <param name="path">The path to the file to read.</param>
    /// <returns>An enumerable collection of strings representing the lines in the file.</returns>
    public IEnumerable<string> ReadLines(string path) => File.ReadLines(path);

    /// <summary>
    /// Reads the entire content of a file at the specified path.
    /// </summary>
    /// <param name="path">The path to the file to read.</param>
    /// <returns>The full content of the file as a single string.</returns>
    public string ReadAllText(string path) => File.ReadAllText(path);
}
