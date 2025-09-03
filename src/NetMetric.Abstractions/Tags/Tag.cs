// <copyright file="Tag.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Represents a simple key/value tag attached to a metric sample.
/// <para>
/// Tags enrich metrics with contextual information, enabling filtering, grouping,
/// and correlation in observability systems.  
/// Example: <c>"endpoint=/api/orders"</c> or <c>"status=success"</c>.
/// </para>
/// </summary>
/// <param name="Key">
/// The tag key. Keys should be non-empty strings representing the dimension name
/// (e.g., <c>"region"</c>, <c>"method"</c>).
/// </param>
/// <param name="Value">
/// The tag value. Values provide the concrete instance of the dimension
/// (e.g., <c>"us-east"</c>, <c>"GET"</c>).
/// </param>
public sealed record Tag(string Key, string Value)
{
    /// <summary>
    /// Returns the string representation of the tag in <c>key=value</c> format.
    /// </summary>
    public override string ToString() => $"{Key}={Value}";
}
