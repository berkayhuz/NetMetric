// <copyright file="Label.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Represents a key/value label (also known as a dimension or tag) attached to a metric sample.
/// <para>
/// Labels enrich metrics with contextual information, enabling filtering, grouping,
/// and correlation in observability backends.  
/// For example: <c>"host=server1"</c>, <c>"region=us-east"</c>, or <c>"method=GET"</c>.
/// </para>
/// </summary>
public sealed record Label
{
    /// <summary>
    /// Gets the key of the label.
    /// <para>
    /// Keys must be non-empty strings and are typically used to denote
    /// the dimension name (e.g., <c>"host"</c>, <c>"region"</c>).
    /// </para>
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the value of the label.
    /// <para>
    /// Values provide the concrete instance of the dimension (e.g., <c>"server1"</c>, <c>"us-east"</c>).
    /// </para>
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Label"/> record.
    /// </summary>
    /// <param name="key">The label key. Must not be null, empty, or whitespace.</param>
    /// <param name="value">The label value. Must not be null.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is null, empty, or consists only of whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is null.
    /// </exception>
    public Label(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        Key = key;
        Value = value;
    }

    /// <summary>
    /// Returns the string representation of the label in <c>key=value</c> format.
    /// </summary>
    public override string ToString() => $"{Key}={Value}";
}
