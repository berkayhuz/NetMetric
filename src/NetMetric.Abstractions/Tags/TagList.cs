// <copyright file="TagList.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Represents a list of key-value tags that can be converted into a read-only dictionary.
/// Useful for attaching metadata to metrics or events.
/// </summary>
public sealed class TagList
{
    /// <summary>
    /// Internal storage for tag key-value pairs.
    /// </summary>
    private readonly Collection<KeyValuePair<string, string>> _items = new();

    /// <summary>
    /// Adds a new tag to the list.
    /// </summary>
    /// <param name="key">The tag key. Must not be null.</param>
    /// <param name="value">The tag value. Must not be null.</param>
    /// <returns>The current <see cref="TagList"/> instance to allow method chaining.</returns>
    public TagList Add(string key, string value)
    {
        _items.Add(new(key, value));
        return this;
    }

    /// <summary>
    /// Converts the tag list into a read-only dictionary.
    /// If no tags exist, returns an empty dictionary.
    /// </summary>
    /// <returns>
    /// An <see cref="IReadOnlyDictionary{TKey, TValue}"/> containing the tags.
    /// </returns>
    public FrozenDictionary<string, string> ToReadOnly() =>
    _items.Count == 0
        ? FrozenDictionary<string, string>.Empty
        : _items.ToFrozenDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
}
