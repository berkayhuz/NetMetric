// <copyright file="InstrumentBuilderBase.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using TagList = NetMetric.Abstractions.TagList;

namespace NetMetric.Metrics.Builders;

/// <summary>
/// Provides a fluent base for building metric instruments with configurable metadata
/// (unit, description), dimension tags, and window policies.
/// </summary>
/// <typeparam name="TMetric">The concrete metric type produced by the builder.</typeparam>
/// <remarks>
/// <para>
/// This base type is responsible for collecting user-specified metadata and tags,
/// merging them with global and resource-level tags from <see cref="MetricOptions"/>,
/// and then applying sanitization constraints via <see cref="TagSanitizer"/>.
/// The result is a compact, immutable tag set ready to embed into the metric instance.
/// </para>
/// <para>
/// Derived builders should call <see cref="MaterializeTags"/> exactly once during
/// <see cref="Build"/> to obtain the final tag dictionary. Repeated calls are inexpensive
/// because the merged/sanitized set is cached and returned as a frozen dictionary.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example: creating a gauge with enriched tags and description
/// var metric = factory.Gauge("svc.users.active", "Active Users")
///     .WithUnit("count")
///     .WithDescription("Number of currently active users")
///     .WithTag("app", "checkout")
///     .WithTags(tags => {
///         tags.Add("region", "eu-central-1");
///         tags.Add("tier", "frontend");
///     })
///     .WithWindow(MetricWindowPolicy.Cumulative)
///     .Build();
/// </code>
/// </example>
internal abstract class InstrumentBuilderBase<TMetric> : IInstrumentBuilder<TMetric>
    where TMetric : IMetric
{
    /// <summary>
    /// Gets the stable unique identifier of the metric (e.g., <c>"service.request.duration"</c>).
    /// </summary>
    protected string Id { get; }

    /// <summary>
    /// Gets the human-readable name of the metric (e.g., <c>"Request Duration"</c>).
    /// </summary>
    protected string Name { get; }

    /// <summary>
    /// Gets or sets the unit of measurement (e.g., <c>"seconds"</c>, <c>"bytes"</c>, <c>"ms"</c>).
    /// </summary>
    protected string? Unit { get; private set; }

    /// <summary>
    /// Gets or sets the metric description shown in UIs or documentation.
    /// </summary>
    protected string? Description { get; private set; }

    /// <summary>
    /// Gets or sets the optional window policy for the metric (e.g., cumulative or tumbling).
    /// </summary>
    protected IMetricWindowPolicy? Window { get; private set; }

    private Dictionary<string, string>? _tagDict;
    private IReadOnlyDictionary<string, string>? _tagsFrozen;

    /// <summary>
    /// Gets the <see cref="MetricOptions"/> applied to this builder; provides global tags,
    /// resource attributes, and limits used during tag sanitization.
    /// </summary>
    protected MetricOptions Options { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InstrumentBuilderBase{TMetric}"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the metric. Must not be null or whitespace.</param>
    /// <param name="name">The display name of the metric. Must not be null or whitespace.</param>
    /// <param name="options">
    /// Optional configuration; when <c>null</c>, a new <see cref="MetricOptions"/> instance is used.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="id"/> or <paramref name="name"/> is null or whitespace.
    /// </exception>
    protected InstrumentBuilderBase(string id, string name, MetricOptions? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
        Options = options ?? new MetricOptions();
    }

    /// <summary>
    /// Specifies the unit of the metric.
    /// </summary>
    /// <param name="unit">
    /// A short, conventional unit string (e.g., <c>"ms"</c>, <c>"bytes"</c>, <c>"%"</c>/>) 
    /// </param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <remarks>
    /// This value is metadata only; it does not affect validation or scaling of input values.
    /// </remarks>
    public IInstrumentBuilder<TMetric> WithUnit(string unit)
    {
        Unit = unit;
        return this;
    }

    /// <summary>
    /// Provides a human-readable description for the metric.
    /// </summary>
    /// <param name="description">Free-form text describing the metric’s purpose and semantics.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public IInstrumentBuilder<TMetric> WithDescription(string description)
    {
        Description = description;
        return this;
    }

    /// <summary>
    /// Adds or overwrites a single tag on this metric.
    /// </summary>
    /// <param name="key">Tag key (case-sensitive, recommended ASCII/UTF-8).</param>
    /// <param name="value">Tag value; empty is allowed but null is normalized to empty.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <remarks>
    /// If the same <paramref name="key"/> is provided multiple times, the last value wins.
    /// </remarks>
    public IInstrumentBuilder<TMetric> WithTag(string key, string value)
    {
        (_tagDict ??= new(StringComparer.Ordinal))[key] = value;
        _tagsFrozen = null;
        return this;
    }

    /// <summary>
    /// Adds multiple tags using a <see cref="TagList"/> helper.
    /// </summary>
    /// <param name="build">Action that fills the <see cref="TagList"/> with desired keys/values.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="build"/> is <c>null</c>.</exception>
    /// <remarks>
    /// <para>
    /// This overload is convenient when building a set of tags conditionally, e.g.:
    /// </para>
    /// <code>
    /// builder.WithTags(t => {
    ///     t.Add("env", env);
    ///     if (isCanary) t.Add("deployment", "canary");
    /// });
    /// </code>
    /// </remarks>
    public IInstrumentBuilder<TMetric> WithTags(Action<TagList> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var tl = new TagList();
        build(tl);

        _tagDict ??= new(StringComparer.Ordinal);
        foreach (var kv in tl.ToReadOnly())
        {
            _tagDict[kv.Key] = kv.Value;
        }

        _tagsFrozen = null;
        return this;
    }

    /// <summary>
    /// Sets the window policy controlling how values accumulate and reset.
    /// </summary>
    /// <param name="window">An <see cref="IMetricWindowPolicy"/> such as <see cref="MetricWindowPolicy.Cumulative"/> or a tumbling policy.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <remarks>
    /// Some metric types may ignore the window (e.g., pure counters), while others (e.g., histograms) may support both cumulative and tumbling.
    /// The interpretation is defined by the concrete metric implementation.
    /// </remarks>
    public IInstrumentBuilder<TMetric> WithWindow(IMetricWindowPolicy window)
    {
        Window = window;
        return this;
    }

    /// <summary>
    /// Produces the final, immutable tag set by merging local tags with global and resource tags
    /// and then applying sanitization rules (length and count limits).
    /// </summary>
    /// <returns>A frozen, read-only dictionary of tags safe to embed into the metric.</returns>
    /// <remarks>
    /// <list type="number">
    /// <item><description>Merging precedence: <b>local</b> &gt; <b>resource</b> &gt; <b>global</b>.</description></item>
    /// <item><description>Sanitization trims overly-long keys/values and limits the total number of tags based on <see cref="MetricOptions"/>.</description></item>
    /// <item><description>Result is cached and returned on subsequent calls.</description></item>
    /// </list>
    /// </remarks>
    protected IReadOnlyDictionary<string, string> MaterializeTags()
    {
        if (_tagsFrozen is not null)
            return _tagsFrozen;

        var local = _tagDict is null || _tagDict.Count == 0
            ? FrozenDictionary<string, string>.Empty
            : _tagDict.ToFrozenDictionary(StringComparer.Ordinal);

        // Merge: local > resource > global (TagUtil applies the precedence)
        var merged = TagUtil.MergeGlobalTags(local, Options);

        // Ensure we have a FrozenDictionary for efficient, thread-safe reuse
        var mergedFrozen = merged as FrozenDictionary<string, string>
                           ?? merged.ToFrozenDictionary(StringComparer.Ordinal);

        // Apply limits from MetricOptions (0/negative disables a given limit)
        _tagsFrozen = TagSanitizer.Sanitize(
            mergedFrozen,
            Options.MaxTagKeyLength,
            Options.MaxTagValueLength,
            Options.MaxTagsPerMetric);

        return _tagsFrozen;
    }

    /// <summary>
    /// Creates the concrete metric instance using the accumulated configuration.
    /// </summary>
    /// <returns>The constructed metric.</returns>
    /// <remarks>
    /// Implementations typically pass <see cref="Id"/>, <see cref="Name"/>, and <see cref="MaterializeTags"/> to the metric’s constructor.
    /// Some builders may also propagate <see cref="Unit"/>, <see cref="Description"/>, or <see cref="Window"/>.
    /// </remarks>
    public abstract TMetric Build();
}
