// <copyright file="MetricPayload.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Exporters;

/// <summary>
/// Represents a single metric entry formatted for JSON export.
/// </summary>
/// <remarks>
/// <para>
/// This type is a simple data container optimized for serialization (e.g., via
/// <see cref="System.Text.Json.JsonSerializer"/>). All properties are get-only
/// and are populated through the constructor to encourage immutability patterns.
/// </para>
/// <para>
/// Common fields such as identifier, name, kind, and unit are exposed directly,
/// while metric-specific details are carried in <see cref="Extra"/> to keep the
/// core model compact and forward-compatible. Optional metadata can be attached
/// using <see cref="Tags"/>.
/// </para>
/// <para>
/// <strong>Thread-safety:</strong> Instances are immutable after construction and are
/// therefore safe to share across threads. However, references supplied to the
/// constructor (such as dictionaries) should not be mutated by the caller after
/// being passed in.
/// </para>
/// </remarks>
/// <example>
/// The following example shows how to construct a <see cref="MetricPayload"/> and
/// serialize it as a JSON line:
/// <code language="csharp"><![CDATA[
/// using System.Text.Json;
/// using System.Collections.Generic;
///
/// var payload = new MetricPayload(
///     ts: DateTimeOffset.UtcNow.ToString("O"),
///     id: "req.count",
///     name: "Request Count",
///     kind: "counter",
///     unit: "1",
///     desc: "Total number of HTTP requests processed",
///     tags: new Dictionary<string, string>
///     {
///         ["service"] = "checkout",
///         ["region"] = "europe-west1"
///     },
///     extra: new Dictionary<string, object?>
///     {
///         ["value"] = 1
///     });
///
/// var json = System.Text.Json.JsonSerializer.Serialize(payload);
/// Console.WriteLine(json); // one JSON object (suitable for a JSON Lines stream)
/// ]]></code>
/// </example>
/// <seealso cref="System.Text.Json.JsonSerializer"/>
/// <seealso cref="System.Collections.Generic.IReadOnlyDictionary{TKey,TValue}"/>
public sealed class MetricPayload
{
    /// <summary>
    /// Gets the UTC timestamp of the metric in ISO 8601 (round-trip) format.
    /// </summary>
    /// <remarks>
    /// Use <c>DateTimeOffset.UtcNow.ToString("O")</c> or an equivalent method to produce a
    /// stable ISO 8601 value. Exporters typically assume Coordinated Universal Time.
    /// </remarks>
    /// <value>An ISO 8601 timestamp such as <c>2025-09-02T15:04:05.1234567Z</c>.</value>
    public string Ts { get; }

    /// <summary>
    /// Gets the unique metric identifier.
    /// </summary>
    /// <remarks>
    /// This identifier is intended for programmatic correlation (e.g., a key or code).
    /// Human-friendly naming can be provided separately via <see cref="Name"/>.
    /// </remarks>
    /// <value>A unique identifier, or <see langword="null"/> if not provided.</value>
    public string? Id { get; }

    /// <summary>
    /// Gets the human-readable metric name.
    /// </summary>
    /// <value>The metric name, or <see langword="null"/> if not specified.</value>
    public string? Name { get; }

    /// <summary>
    /// Gets the metric type or category (for example, <c>"counter"</c>, <c>"gauge"</c>, <c>"histogram"</c>).
    /// </summary>
    /// <value>The metric kind, or <see langword="null"/> if unspecified.</value>
    public string? Kind { get; }

    /// <summary>
    /// Gets the unit of measurement for the metric value.
    /// </summary>
    /// <remarks>
    /// Prefer UCUM-style or otherwise consistent units (e.g., <c>"1"</c> for dimensionless,
    /// <c>"ms"</c> for milliseconds, <c>"By"</c> for bytes).
    /// </remarks>
    /// <value>The unit string, or <see langword="null"/> if not applicable.</value>
    public string? Unit { get; }

    /// <summary>
    /// Gets an optional description providing additional human-readable context.
    /// </summary>
    /// <value>A free-form description, or <see langword="null"/> if not provided.</value>
    public string? Desc { get; }

    /// <summary>
    /// Gets the metric tags (labels) as key/value pairs.
    /// </summary>
    /// <remarks>
    /// Tags can be used to partition, filter, or aggregate series (for example, by
    /// service name, region, or instance identifier).
    /// </remarks>
    /// <value>
    /// A read-only dictionary of labels, or <see langword="null"/> when no labels apply.
    /// </value>
    public System.Collections.Generic.IReadOnlyDictionary<string, string>? Tags { get; }

    /// <summary>
    /// Gets the additional metric-specific fields.
    /// </summary>
    /// <remarks>
    /// The shape of this dictionary depends on the metric kind. For example, counters
    /// may include a <c>"value"</c>, histograms might include <c>"buckets"</c> and
    /// <c>"counts"</c>, and summaries could include <c>"quantiles"</c>.
    /// </remarks>
    /// <value>A non-null read-only dictionary of extra fields.</value>
    public System.Collections.Generic.IReadOnlyDictionary<string, object?> Extra { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricPayload"/> class.
    /// </summary>
    /// <param name="ts">The ISO 8601 UTC timestamp (for example, <c>"2025-09-02T15:04:05.1234567Z"</c>).</param>
    /// <param name="id">The unique metric identifier, or <see langword="null"/>.</param>
    /// <param name="name">The human-readable metric name, or <see langword="null"/>.</param>
    /// <param name="kind">The metric type/category (e.g., <c>"counter"</c>), or <see langword="null"/>.</param>
    /// <param name="unit">The unit of measurement (e.g., <c>"ms"</c>, <c>"1"</c>), or <see langword="null"/>.</param>
    /// <param name="desc">Optional description text, or <see langword="null"/>.</param>
    /// <param name="tags">Optional tags (labels) attached to the metric, or <see langword="null"/>.</param>
    /// <param name="extra">Additional fields specific to the metric. Must not be <see langword="null"/>.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="ts"/> or <paramref name="extra"/> is <see langword="null"/>.
    /// </exception>
    public MetricPayload(
        string ts,
        string? id,
        string? name,
        string? kind,
        string? unit,
        string? desc,
        System.Collections.Generic.IReadOnlyDictionary<string, string>? tags,
        System.Collections.Generic.IReadOnlyDictionary<string, object?> extra)
    {
        Ts = ts ?? throw new System.ArgumentNullException(nameof(ts));
        Id = id;
        Name = name;
        Kind = kind;
        Unit = unit;
        Desc = desc;
        Tags = tags;
        Extra = extra ?? throw new System.ArgumentNullException(nameof(extra));
    }
}
