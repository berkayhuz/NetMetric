// <copyright file="InMemoryTimerSink.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Timer.Adapters;

/// <summary>
/// A minimal in-memory implementation of <see cref="ITimerSink"/> used for testing and development purposes.
/// It stores the last N samples per unique metric <c>id</c>, where N is defined by the <see cref="InMemoryTimerSink"/>
/// constructor (which defaults to 256).
/// </summary>
public sealed class InMemoryTimerSink : ITimerSink
{
    private readonly int _capacityPerKey;

    // Concurrent dictionary to store a queue of samples for each metric id.
    private readonly ConcurrentDictionary<string, Entry> _map = new(StringComparer.Ordinal);

    // Private nested class to hold the queue and sample count for each metric id.
    private sealed class Entry
    {
        // Private queue to store the sample values.
        private readonly ConcurrentQueue<double> _q = new();
        private int _count; // Sample count managed via Interlocked operations.

        /// <summary>
        /// Enqueues a new sample value to the queue.
        /// </summary>
        /// <param name="v">The sample value to enqueue.</param>
        public void Enqueue(double v)
        {
            _q.Enqueue(v);
            System.Threading.Interlocked.Increment(ref _count);
        }

        /// <summary>
        /// Attempts to dequeue a sample from the queue.
        /// </summary>
        /// <returns><c>true</c> if a sample was dequeued; otherwise, <c>false</c>.</returns>
        public bool TryDequeue()
        {
            var ok = _q.TryDequeue(out _);
            if (ok)
                System.Threading.Interlocked.Decrement(ref _count);
            return ok;
        }

        /// <summary>
        /// Gets the current sample count.
        /// </summary>
        public int Count => System.Threading.Volatile.Read(ref _count);

        /// <summary>
        /// Returns a snapshot of the current queue as an array of samples.
        /// </summary>
        /// <returns>An array of the current samples.</returns>
        public double[] Snapshot() => _q.ToArray();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryTimerSink"/> class with a specified capacity
    /// for each metric id. The capacity defines the maximum number of samples retained per id.
    /// </summary>
    /// <param name="capacityPerKey">The maximum number of samples to store per metric id (minimum 1).</param>
    /// <remarks>
    /// The capacity ensures that only the most recent samples are retained. Older samples are discarded
    /// once the capacity is exceeded. This parameter is specific to the constructor.
    /// </remarks>
    public InMemoryTimerSink(int capacityPerKey = 256)
        => _capacityPerKey = Math.Max(1, capacityPerKey);

    /// <inheritdoc />
    /// <summary>
    /// Records a new timing sample for a specific metric identified by <paramref name="id"/>.
    /// If the metric does not exist, it is created. The sample is added to the in-memory store, and if the
    /// capacity is exceeded, the oldest samples are discarded.
    /// </summary>
    /// <param name="id">The unique identifier for the metric.</param>
    /// <param name="name">The name of the metric.</param>
    /// <param name="elapsedMs">The elapsed time in milliseconds to record.</param>
    /// <param name="tags">Optional tags associated with the metric.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="id"/> is null or whitespace.</exception>
    /// <remarks>
    /// This method allows you to store the timing data for a given metric. The number of samples stored
    /// for each metric is limited by the capacity specified in the constructor.
    /// </remarks>
    public void Record(string id, string name, double elapsedMs, IReadOnlyDictionary<string, string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        // Get or create an entry for the specified metric id.
        var e = _map.GetOrAdd(id, _ => new Entry());
        e.Enqueue(elapsedMs);

        // Trim to the defined capacity, discarding older samples if necessary.
        while (e.Count > _capacityPerKey && e.TryDequeue())
        { /* loop */
        }
    }

    /// <summary>
    /// Retrieves the current samples stored for the specified metric id.
    /// </summary>
    /// <param name="id">The unique identifier for the metric.</param>
    /// <returns>A read-only list of the current samples for the metric.</returns>
    public IReadOnlyList<double> GetSamples(string id)
        => _map.TryGetValue(id, out var e) ? e.Snapshot() : Array.Empty<double>();
}
