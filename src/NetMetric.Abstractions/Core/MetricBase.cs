// <copyright file="MetricBase.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Base class for metric instruments with common properties like Id, Name, Unit, and Tags.
/// </summary>
[DebuggerDisplay("{Id} ({Name}), Tags={{{Tags.Count}}}")]
public abstract class MetricBase : IMetric
{
    private readonly FrozenDictionary<string, string> _tags;

    /// <summary>
    /// Unique identifier of the metric.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Human-readable name of the metric.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Tags associated with the metric (exposed as <see cref="IReadOnlyDictionary{TKey,TValue}"/> to satisfy <see cref="IMetric"/>).
    /// Internally backed by a <see cref="FrozenDictionary{TKey,TValue}"/> for immutability and performance.
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags => _tags;

    /// <summary>
    /// Strongly-typed frozen view of tags for high-performance internal use.
    /// </summary>
    public FrozenDictionary<string, string> FrozenTags => _tags;

    /// <summary>
    /// Optional unit of measurement (e.g., ms, bytes).
    /// </summary>
    public string? Unit { get; }

    /// <summary>
    /// Optional description of the metric.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// The kind of metric instrument (counter, histogram, gauge, etc.).
    /// </summary>
    public InstrumentKind Kind { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="MetricBase"/>.
    /// </summary>
    protected MetricBase(
        string id,
        string name,
        InstrumentKind kind,
        IReadOnlyDictionary<string, string>? tags = null,
        string? unit = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        Kind = kind;
        Unit = unit;
        Description = description;

        _tags = tags is null
            ? FrozenDictionary<string, string>.Empty
            : Freeze(tags);
    }

    /// <summary>
    /// Gets the current value of the metric.
    /// </summary>
    public abstract object? GetValue();

    public override string ToString() => $"{Id} ({Name}), Tags={_tags.Count}";

    /// <summary>
    /// Creates a frozen dictionary from the given dictionary for immutability and fast lookups.
    /// </summary>
    private static FrozenDictionary<string, string> Freeze(IReadOnlyDictionary<string, string> src)
    {
        if (src is FrozenDictionary<string, string> f)
        {
            return f;
        }

        if (src is IDictionary<string, string> d)
        {
            return d.ToFrozenDictionary(StringComparer.Ordinal);
        }

        return new Dictionary<string, string>(src, StringComparer.Ordinal)
            .ToFrozenDictionary(StringComparer.Ordinal);
    }
}
