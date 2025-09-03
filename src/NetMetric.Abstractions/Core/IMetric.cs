// <copyright file="IMetric.cs" company="NetMetric"
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Generic interface for all metric types, providing strong typing for values.
/// </summary>
/// <typeparam name="TValue">The type of the metric value.</typeparam>
public interface IMetric<out TValue> : IMetric
{
    /// <summary>
    /// Gets the strongly typed metric value.
    /// </summary>
    /// <returns>The metric value as <typeparamref name="TValue"/>.</returns>
    new TValue GetValue();
}

/// <summary>
/// Non-generic surface kept for backward compatibility.
/// </summary>
public interface IMetric
{
    /// <summary>
    /// Gets the unique identifier of the metric.
    /// </summary>
    string Id
    {
        get;
    }

    /// <summary>
    /// Gets the display name of the metric.
    /// </summary>
    string Name
    {
        get;
    }

    /// <summary>
    /// Gets the tags (key-value pairs) associated with the metric.
    /// </summary>
    IReadOnlyDictionary<string, string> Tags
    {
        get;
    }

    /// <summary>
    /// Gets the metric value as an object.
    /// </summary>
    /// <returns>The metric value boxed as an <see cref="object"/>.</returns>
    object? GetValue();
}
