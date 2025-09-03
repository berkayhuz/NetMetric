// <copyright file="HistogramMetric.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.Histogram;

/// <summary>
/// A sliding-window histogram that records numeric observations and reports distribution
/// statistics (<c>min</c>, <c>max</c>, <c>p50</c>, <c>p90</c>, <c>p99</c>) over the last N samples.
/// </summary>
/// <remarks>
/// <para>
/// The window is defined by <see cref="Capacity"/> (default 2048). Each call to <see cref="Record(double)"/>
/// overwrites the oldest value once the buffer is full (ring buffer). The metric also tracks a
/// lifetime <see cref="TotalCount"/> that never resets with the window.
/// </para>
/// <para>
/// Calling <see cref="GetValue"/> returns a consistent snapshot computed from the most recent
/// samples in the window. Percentiles are computed with linear interpolation on the sorted snapshot.
/// </para>
/// <para>
/// Thread safety: writes are synchronized with a lock to keep the ring buffer consistent; reads
/// copy the last window into a rented array from <see cref="ArrayPool{T}"/> inside the same lock
/// and then perform sorting and percentile math outside the lock to minimize contention.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Create a histogram for request latencies with a 4096-sample sliding window
/// var hist = new HistogramMetric(
///     id: "http.server.latency",
///     name: "HTTP Server Latency",
///     tags: new Dictionary&lt;string,string&gt; { ["service.name"] = "orders-api" },
///     capacity: 4096);
///
/// // Record latencies (ms)
/// hist.Record(12);
/// hist.Record(37);
/// hist.Record(9);
///
/// // Take a snapshot
/// var dist = (DistributionValue)hist.GetValue();
/// Console.WriteLine($"Count={dist.Count} p50={dist.P50} p90={dist.P90} p99={dist.P99} min={dist.Min} max={dist.Max}");
/// </code>
/// </example>
/// <example>
/// <code>
/// // Handling invalid values safely
/// if (!hist.TryRecord(double.NaN)) {
///     // log or ignore
/// }
/// </code>
/// </example>
public sealed class HistogramMetric : MetricBase
{
    private const int MinCapacity = 256;
    private readonly object _lock = new object();
    private readonly double[] _buffer;
    private int _index;
    private long _totalCount;

    /// <summary>
    /// Gets the maximum number of values retained in the sliding window.
    /// </summary>
    /// <remarks>
    /// The effective window size is <c>min(TotalCount, Capacity)</c>. Larger capacities reduce
    /// sampling error at the cost of more memory and sorting time when taking a snapshot.
    /// </remarks>
    public int Capacity { get; }

    /// <summary>
    /// Gets the total number of values recorded over the lifetime of this metric
    /// (not limited by <see cref="Capacity"/>).
    /// </summary>
    public long TotalCount => Interlocked.Read(ref _totalCount);

    /// <summary>
    /// Initializes a new instance of the <see cref="HistogramMetric"/> class.
    /// </summary>
    /// <param name="id">Stable unique identifier for the metric (e.g., <c>"http.server.latency"</c>).</param>
    /// <param name="name">Human-readable metric name (e.g., <c>"HTTP Server Latency"</c>).</param>
    /// <param name="tags">Optional dimension tags applied to this metric.</param>
    /// <param name="capacity">
    /// Maximum number of samples retained in the sliding window. Values less than an internal
    /// lower bound are clamped to <c>256</c>.
    /// </param>
    /// <remarks>
    /// The internal buffer is preallocated at construction time to ensure predictable memory usage.
    /// </remarks>
    public HistogramMetric(string id, string name, IReadOnlyDictionary<string, string>? tags = null, int capacity = 2048)
        : base(id, name, InstrumentKind.Histogram, tags)
    {
        Capacity = Math.Max(MinCapacity, capacity);
        _buffer = new double[Capacity];
    }

    /// <summary>
    /// Records a new value into the sliding window.
    /// </summary>
    /// <param name="value">The value to record. Must be finite.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is NaN or Infinity.</exception>
    /// <remarks>
    /// Oldest values are overwritten once the window is full. <see cref="TotalCount"/> increases
    /// monotonically and is not capped by <see cref="Capacity"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Value must be a valid finite number.", nameof(value));
        }

        lock (_lock)
        {
            _buffer[_index] = value;
            _index = (_index + 1) % Capacity;
            _totalCount++; // Lifetime counter
        }
    }

    /// <summary>
    /// Returns a distribution snapshot over the current window: count, min, max, p50, p90, p99.
    /// </summary>
    /// <returns>
    /// A <see cref="DistributionValue"/> with statistics computed from up to <see cref="Capacity"/>
    /// most recent samples. If no values were recorded yet, all fields are zero.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The snapshot process rents a temporary array from <see cref="ArrayPool{T}"/> and sorts only
    /// the portion that contains valid samples, avoiding unnecessary allocations.
    /// </para>
    /// <para>
    /// Percentiles are computed using linear interpolation between adjacent order statistics.
    /// </para>
    /// </remarks>
    public override object? GetValue()
    {
        double[] rented = Array.Empty<double>();
        int filled;

        lock (_lock)
        {
            if (_totalCount == 0)
            {
                return new DistributionValue(0, 0, 0, 0, 0, 0);
            }

            filled = (int)Math.Min(_totalCount, Capacity);
            rented = ArrayPool<double>.Shared.Rent(filled);

            int head = (_index - filled + Capacity) % Capacity;
            if (head + filled <= Capacity)
            {
                Array.Copy(_buffer, head, rented, 0, filled);
            }
            else
            {
                int first = Capacity - head;
                Array.Copy(_buffer, head, rented, 0, first);
                Array.Copy(_buffer, 0, rented, first, filled - first);
            }
        }

        try
        {
            Array.Sort(rented, 0, filled);

            double min = rented[0];
            double max = rented[filled - 1];

            double p50 = PercentileSorted(rented, filled, 0.50);
            double p90 = PercentileSorted(rented, filled, 0.90);
            double p99 = PercentileSorted(rented, filled, 0.99);

            return new DistributionValue(filled, min, max, p50, p90, p99);
        }
        finally
        {
            if (rented.Length > 0)
            {
                ArrayPool<double>.Shared.Return(rented, clearArray: false);
            }
        }
    }

    /// <summary>
    /// Computes the q-th percentile (0..1) from a sorted slice <c>[0..length)</c> using linear interpolation.
    /// </summary>
    /// <param name="sorted">Array containing sorted values in ascending order.</param>
    /// <param name="length">Number of valid elements to consider from <paramref name="sorted"/>.</param>
    /// <param name="q">Percentile in [0,1]. Values &lt;= 0 return the minimum; values &gt;= 1 return the maximum.</param>
    /// <returns>The percentile value.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="sorted"/> is <c>null</c>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double PercentileSorted(double[] sorted, int length, double q)
    {
        ArgumentNullException.ThrowIfNull(sorted);

        if (length <= 0)
        {
            return 0d;
        }

        if (q <= 0)
        {
            return sorted[0];
        }

        if (q >= 1)
        {
            return sorted[length - 1];
        }

        double pos = q * (length - 1);
        int i = (int)pos;
        double frac = pos - i;

        return (i + 1 < length)
            ? sorted[i] + frac * (sorted[i + 1] - sorted[i])
            : sorted[i];
    }

    /// <summary>
    /// Attempts to record a value; returns <c>false</c> for invalid inputs instead of throwing.
    /// </summary>
    /// <param name="value">The value to record.</param>
    /// <returns><c>true</c> if recorded; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Prefer <see cref="Record(double)"/> on validated inputs to avoid the extra branch.
    /// </remarks>
    public bool TryRecord(double value)
    {
        if (!double.IsFinite(value))
        {
            return false;
        }

        Record(value);
        return true;
    }
}
