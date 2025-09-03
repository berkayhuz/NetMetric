// <copyright file="CachedMetricTimerSink.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Timer.Adapters;

/// <summary>
/// A timer sink that caches a single <see cref="ITimerMetric"/> per unique metric <c>id</c>.
/// <para><b>Behavior:</b> On the first call to <see cref="Record"/> for a given <c>id</c>, a new metric is created
/// using the provided <c>name</c> and <c>tags</c>. Subsequent calls with the same <c>id</c> will reuse the cached 
/// metric and ignore any new <c>name</c> or <c>tags</c> values.</para>
/// </summary>
public sealed class CachedMetricTimerSink : ITimerSink, IDisposable
{
    private readonly ConcurrentDictionary<string, ITimerMetric> _byId;
    private readonly Func<string, string, IReadOnlyDictionary<string, string>?, ITimerMetric> _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedMetricTimerSink"/> using an <see cref="ITimerFactory"/>
    /// to create the metrics.
    /// </summary>
    /// <param name="factory">The <see cref="ITimerFactory"/> used to create new timer metrics.</param>
    /// <param name="comparer">An optional custom comparer for the <c>id</c> string values. Defaults to <see cref="StringComparer.Ordinal"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> is null.</exception>
    /// <remarks>
    /// This constructor enables the use of a factory to generate metrics for a given <c>id</c>, <c>name</c>, and <c>tags</c>.
    /// The resulting metrics will be cached and reused based on their <c>id</c>.
    /// </remarks>
    public CachedMetricTimerSink(ITimerFactory factory, IEqualityComparer<string>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = (id, name, tags) => factory.CreateTimer(id, name, tags);
        _byId = new ConcurrentDictionary<string, ITimerMetric>(comparer ?? StringComparer.Ordinal);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedMetricTimerSink"/> with a custom factory delegate
    /// to create the metrics.
    /// </summary>
    /// <param name="factory">A delegate that creates an <see cref="ITimerMetric"/> based on the given <c>id</c>, <c>name</c>, and <c>tags</c>.</param>
    /// <param name="comparer">An optional custom comparer for the <c>id</c> string values. Defaults to <see cref="StringComparer.Ordinal"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="factory"/> is null.</exception>
    /// <remarks>
    /// This constructor allows the use of a custom factory method for creating <see cref="ITimerMetric"/> instances,
    /// enabling flexibility in how metrics are instantiated and cached.
    /// </remarks>
    public CachedMetricTimerSink(
        Func<string, string, IReadOnlyDictionary<string, string>?, ITimerMetric> factory,
        IEqualityComparer<string>? comparer = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _byId = new ConcurrentDictionary<string, ITimerMetric>(comparer ?? StringComparer.Ordinal);
    }

    /// <inheritdoc />
    /// <summary>
    /// Records a timing measurement for a specific metric identified by <paramref name="id"/>.
    /// If the metric does not exist, it is created using the provided <paramref name="name"/> and <paramref name="tags"/>.
    /// Subsequent calls for the same <paramref name="id"/> will reuse the cached metric.
    /// </summary>
    /// <param name="id">The unique identifier for the metric.</param>
    /// <param name="name">The name of the metric.</param>
    /// <param name="elapsedMs">The elapsed time in milliseconds to record.</param>
    /// <param name="tags">Optional tags to associate with the metric.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> or <paramref name="name"/> is null or whitespace, or if <paramref name="elapsedMs"/>
    /// is not a valid non-negative number.
    /// </exception>
    public void Record(string id, string name, double elapsedMs, IReadOnlyDictionary<string, string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (double.IsNaN(elapsedMs) || double.IsInfinity(elapsedMs) || elapsedMs < 0)
            throw new ArgumentException("elapsedMs must be a non-negative finite number", nameof(elapsedMs));

        // Get or create the metric and record the elapsed time.
        var timer = _byId.GetOrAdd(id, key => _factory(key, name, tags));
        timer.RecordMilliseconds(elapsedMs);
    }

    /// <summary>
    /// Tries to retrieve the cached metric for a given <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="metric">The resulting <see cref="ITimerMetric"/> instance if found.</param>
    /// <returns><c>true</c> if the metric was found; otherwise, <c>false</c>.</returns>
    public bool TryGet(string id, out ITimerMetric metric)
        => _byId.TryGetValue(id, out metric!);

    /// <inheritdoc />
    /// <summary>
    /// Disposes of the cached metrics and clears the cache.
    /// </summary>
    public void Dispose()
    {
        foreach (var m in _byId.Values)
            (m as IDisposable)?.Dispose();
        _byId.Clear();
    }
}
