// <copyright file="MultiGaugeMetric.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.MultiGauge;

/// <summary>
/// A multi-sample gauge metric that collects multiple <see cref="GaugeValue"/> samples,
/// each optionally carrying its own tags and even its own identity (<c>id</c>/<c>name</c>) as a sibling.
/// </summary>
/// <remarks>
/// <para>
/// Use a multi-gauge when you need to publish a set of related instantaneous values under a single
/// logical metric, e.g. per-disk usage, per-endpoint concurrency, or per-tenant queue depth.
/// </para>
/// <para>
/// Key characteristics:
/// <list type="bullet">
///   <item><description><b>Thread-safe</b>: writers (<see cref="SetValue(double, IReadOnlyDictionary{string, string}?)"/> / <see cref="AddSibling(string, string, double, IReadOnlyDictionary{string, string}?)"/>) and readers (<see cref="GetValue"/>) are synchronized.</description></item>
///   <item><description><b>Allocation-aware</b>: snapshot creation uses a swap-buffer strategy when <see cref="ResetOnGet"/> is true, minimizing allocations under lock.</description></item>
///   <item><description><b>Optional reset</b>: when <see cref="ResetOnGet"/> is <c>true</c> (default), samples are cleared after <see cref="GetValue"/>; when <c>false</c>, they persist.</description></item>
///   <item><description><b>Immutable tags</b>: sample tag dictionaries are converted to <see cref="FrozenDictionary{TKey, TValue}"/> for efficient reuse.</description></item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example 1: Per-disk usage with reset-on-get (default)
/// var mg = factory.MultiGauge("system.disk.usage", "Disk Usage")
///                  .WithUnit("%")
///                  .WithDescription("Per-disk usage percentage")
///                  .WithInitialCapacity(128)
///                  .Build();
///
/// mg.AddOrUpdate("disk.c", 73.5);    // implementation typically exposes AddOrUpdate
/// mg.AddOrUpdate("disk.d", 41.2);
///
/// var snapshot = (MultiSampleValue)mg.GetValue(); // clears internal buffer by default
/// foreach (var s in snapshot.Samples)
///     Console.WriteLine($"{s.Id}: {s.Value.Value}");
///
/// // buffer is empty after GetValue() when ResetOnGet = true
/// </code>
/// </example>
/// <example>
/// <code>
/// // Example 2: Persist values across reads (no reset on get)
/// var mg = factory.MultiGauge("svc.connections", "Active Connections")
///                  .WithUnit("count")
///                  .WithResetOnGet(false)
///                  .Build();
///
/// mg.AddOrUpdate("api", 17);
/// var s1 = (MultiSampleValue)mg.GetValue();  // values persist
/// var s2 = (MultiSampleValue)mg.GetValue();  // still present
/// </code>
/// </example>
public sealed class MultiGaugeMetric : MetricBase, IMultiGauge
{
    private readonly object _lock = new object();

    // Active buffer for incoming samples
    private Collection<MultiSampleItem> _buffer;

    // Reusable list to avoid allocations when ResetOnGet = true (swap-buffer pattern)
    private Collection<MultiSampleItem>? _scratch;

    private readonly int _initialCapacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiGaugeMetric"/> class.
    /// </summary>
    /// <param name="id">The parent metric id (e.g., <c>"service.queue.depth"</c>).</param>
    /// <param name="name">The parent metric name (human-readable).</param>
    /// <param name="tags">Optional base tags applied to the parent metric object (not to each sample).</param>
    /// <param name="initialCapacity">Initial capacity for the internal collection (default 64).</param>
    /// <param name="resetOnGet">
    /// If <c>true</c> (default), samples are cleared after each <see cref="GetValue"/> call.
    /// If <c>false</c>, samples accumulate until explicitly cleared via <see cref="Clear"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="initialCapacity"/> is negative.</exception>
    /// <remarks>
    /// The internal collection is preallocated to the requested capacity (clamped to a small minimum if zero)
    /// to reduce growth-related allocations in high-throughput scenarios.
    /// </remarks>
    public MultiGaugeMetric(
        string id,
        string name,
        IReadOnlyDictionary<string, string>? tags = null,
        int initialCapacity = 64,
        bool resetOnGet = true)
        : base(id, name, InstrumentKind.MultiSample, tags)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);

        _initialCapacity = initialCapacity == 0 ? 4 : initialCapacity;
        _buffer = new Collection<MultiSampleItem>(new List<MultiSampleItem>(_initialCapacity));
        ResetOnGet = resetOnGet;
    }

    /// <summary>
    /// When <c>true</c>, the internal buffer is cleared after each <see cref="GetValue"/> snapshot.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool ResetOnGet { get; }

    /// <summary>
    /// Current number of samples buffered (approximate under concurrency).
    /// </summary>
    /// <remarks>
    /// This value is provided for diagnostic purposes and might be slightly stale under contention,
    /// since it is computed under a short critical section but can change immediately after.
    /// </remarks>
    public int ApproximateCount
    {
        get
        {
            lock (_lock)
            {
                return _buffer.Count;
            }
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValue(double value, IReadOnlyDictionary<string, string>? tags = null)
    {
        Set(value, tags);
    }

    /// <summary>
    /// Appends a sample for the parent metric identity (uses <see cref="MetricBase.Id"/> and <see cref="MetricBase.Name"/>).
    /// </summary>
    /// <param name="value">Sample value. Must be a finite number.</param>
    /// <param name="tags">Optional tags specific to this sample. Converted to a frozen dictionary.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is NaN or Infinity.</exception>
    /// <remarks>
    /// Multi-gauge is an <i>append-only</i> collector by design. Each call adds a new sibling sample;
    /// exporters can represent them as separate series distinguished by <c>id/name</c> and tags.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(double value, IReadOnlyDictionary<string, string>? tags = null)
    {
        EnsureFinite(value);

        var frozenTags = FreezeTags(tags);
        var item = new MultiSampleItem(Id, Name, frozenTags, new GaugeValue(value));

        lock (_lock)
        {
            _buffer.Add(item);
        }
    }

    /// <summary>
    /// Adds a sibling sample with its own <paramref name="id"/> and <paramref name="name"/>.
    /// Useful for publishing multiple related gauges (e.g., per-resource) under one logical metric group.
    /// </summary>
    /// <param name="id">Sibling metric id (must be non-empty).</param>
    /// <param name="name">Sibling metric name (must be non-empty).</param>
    /// <param name="value">Sample value. Must be a finite number.</param>
    /// <param name="tags">Optional tags specific to this sibling; converted to a frozen dictionary.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> or <paramref name="name"/> is null/whitespace, or <paramref name="value"/> is not finite.
    /// </exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddSibling(string id, string name, double value, IReadOnlyDictionary<string, string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureFinite(value);

        var frozenTags = FreezeTags(tags);
        var item = new MultiSampleItem(id, name, frozenTags, new GaugeValue(value));

        lock (_lock)
        {
            _buffer.Add(item);
        }
    }

    /// <summary>
    /// Clears all buffered samples.
    /// </summary>
    /// <remarks>
    /// This operation drops all pending siblings. Use with care in long-lived processes if
    /// <see cref="ResetOnGet"/> is <c>false</c>.
    /// </remarks>
    public void Clear()
    {
        lock (_lock)
        {
            _buffer.Clear();
            _scratch?.Clear();
        }
    }

    /// <summary>
    /// Produces a snapshot of all current samples as a <see cref="MultiSampleValue"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="MultiSampleValue"/> capturing the current set of sibling samples.  
    /// When <see cref="ResetOnGet"/> is <c>true</c>, the internal buffer is swapped with a reusable
    /// collection (and then cleared) to minimize time spent under lock. Otherwise, a copy is created
    /// without altering the internal buffer.
    /// </returns>
    /// <remarks>
    /// The returned snapshot is immutable from the caller’s perspective (backed by an array),
    /// so it is safe to enumerate without synchronization.
    /// </remarks>
    public override object? GetValue()
    {
        Collection<MultiSampleItem> snapshot;

        lock (_lock)
        {
            if (_buffer.Count == 0)
            {
                return new MultiSampleValue(Array.Empty<MultiSampleItem>());
            }

            if (ResetOnGet)
            {
                snapshot = _buffer;
                _buffer = _scratch ?? new Collection<MultiSampleItem>(new List<MultiSampleItem>(_initialCapacity));
                _scratch = snapshot;
            }
            else
            {
                snapshot = new Collection<MultiSampleItem>(new List<MultiSampleItem>(_buffer));
            }
        }

        var snapshotArray = snapshot.ToArray();

        if (ResetOnGet)
        {
            snapshot.Clear();
        }

        return new MultiSampleValue(snapshotArray);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureFinite(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Value must be a valid finite number.", nameof(value));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static FrozenDictionary<string, string> FreezeTags(IReadOnlyDictionary<string, string>? src)
    {
        if (src is null)
        {
            return FrozenDictionary<string, string>.Empty;
        }
        else if (src is FrozenDictionary<string, string> frozen)
        {
            return frozen;
        }
        else if (src is IDictionary<string, string> dict)
        {
            return dict.ToFrozenDictionary(StringComparer.Ordinal);
        }

        return new Dictionary<string, string>(src, StringComparer.Ordinal)
            .ToFrozenDictionary(StringComparer.Ordinal);
    }
}
