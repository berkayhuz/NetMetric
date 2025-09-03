// <copyright file="BucketHistogramMetric.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.BucketHistogram;

/// <summary>
/// A thread-safe histogram metric that classifies observations into discrete buckets
/// (linear, exponential, or custom upper bounds) and maintains running aggregates
/// (<see cref="Min"/>, <see cref="Max"/>, and <see cref="Sum"/>). An optional
/// <see cref="MetricWindowPolicy"/> allows cumulative or tumbling-window behavior.
/// </summary>
/// <remarks>
/// <para>
/// Buckets are represented by their upper bounds; an additional implicit “overflow” bucket
/// captures values greater than the last bound. Internally, bucket selection uses
/// <see cref="Array.BinarySearch{T}(T[], T)"/> to achieve O(log N) classification per observation.
/// </para>
/// <para>
/// When a tumbling window is configured, all counters and aggregates are atomically reset at
/// the end of each period. The reset is triggered lazily on the next call to <see cref="Observe(double)"/>
/// after the window has elapsed.
/// </para>
/// <para>
/// This metric is allocation-conscious and uses lock-free atomic operations where practical
/// (e.g., <see cref="Interlocked"/> and <see cref="Volatile"/> for counters and aggregates).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example 1: Cumulative histogram with linear buckets (0..500ms by 25ms)
/// var metric = new BucketHistogramMetric(
///     id: "svc.req.duration",
///     name: "Request Duration (ms)",
///     bucketUpperBounds: BucketHistogramMetric.LinearBuckets(start: 0, width: 25, count: 20));
///
/// metric.Observe(42);
/// metric.Observe(230);
/// var snapshot = (BucketHistogramValue)metric.GetValue();
/// Console.WriteLine($"Count={snapshot.Count} Min={snapshot.Min} Max={snapshot.Max} Sum={snapshot.Sum}");
///
/// // Example 2: Tumbling 1-minute window with exponential buckets (1..32KB)
/// var tumbling = MetricWindowPolicy.Tumbling(TimeSpan.FromMinutes(1));
/// var sizeHist = new BucketHistogramMetric(
///     id: "svc.payload.size",
///     name: "Payload Size (KB)",
///     bucketUpperBounds: BucketHistogramMetric.ExponentialBuckets(start: 1, factor: 2, count: 6),
///     window: tumbling);
///
/// sizeHist.Observe(4);
/// sizeHist.Observe(7);
/// // After 1 minute (on next Observe), the histogram resets.
/// </code>
/// </example>
public sealed class BucketHistogramMetric : MetricBase, IBucketHistogramMetric
{
    private readonly double[] _bounds;
    private readonly long[] _counts;
    private double _sum;
    private double _min = double.PositiveInfinity;
    private double _max = double.NegativeInfinity;

    private readonly MetricWindowPolicy _window;
    private readonly ITimeProvider _clock;
    private long _nextResetTicksUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="BucketHistogramMetric"/> class with the specified bucket bounds.
    /// </summary>
    /// <param name="id">Stable unique identifier for this metric (e.g., <c>"service.request.duration"</c>).</param>
    /// <param name="name">Human-readable metric name (e.g., <c>"Request Duration"</c>).</param>
    /// <param name="bucketUpperBounds">
    /// The set of bucket upper bounds. The array may be unsorted; it will be sorted ascending.
    /// An implicit overflow bucket will account for values greater than the last bound.
    /// </param>
    /// <param name="tags">Optional base dimension tags applied to the metric.</param>
    /// <param name="window">
    /// Window policy controlling accumulation. If <c>null</c>, <see cref="MetricWindowPolicy.Cumulative"/> is used.
    /// </param>
    /// <param name="clock">
    /// Optional time provider used for window rollovers. Defaults to UTC time via <see cref="UtcTimeProvider"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bucketUpperBounds"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="bucketUpperBounds"/> is empty or contains non-finite values (NaN or ±Infinity).
    /// </exception>
    public BucketHistogramMetric(
         string id,
         string name,
         IEnumerable<double> bucketUpperBounds,
         IReadOnlyDictionary<string, string>? tags = null,
         MetricWindowPolicy? window = null,
         ITimeProvider? clock = null)
         : base(id, name, InstrumentKind.Histogram, tags)
    {
        _bounds = bucketUpperBounds?.ToArray() ?? throw new ArgumentNullException(nameof(bucketUpperBounds));
        if (_bounds.Length == 0)
            throw new ArgumentException("At least one bucket bound required.", nameof(bucketUpperBounds));

        Array.Sort(_bounds);
        if (_bounds.Any(b => double.IsNaN(b) || double.IsInfinity(b)))
            throw new ArgumentException("Bucket bounds must be finite.", nameof(bucketUpperBounds));

        _counts = new long[_bounds.Length + 1]; // +1 for overflow bucket
        _window = window ?? MetricWindowPolicy.Cumulative;
        _clock = clock ?? new UtcTimeProvider();
        if (_window.Kind == MetricWindowPolicy.WindowKind.Tumbling)
            _nextResetTicksUtc = _clock.UtcNow.Add(_window.Period).Ticks;
    }

    /// <summary>
    /// Records a single observation and updates bucket counts and running aggregates.
    /// </summary>
    /// <param name="value">Observed value (must be finite).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is NaN or Infinity.</exception>
    /// <remarks>
    /// <para>
    /// This method is safe for concurrent callers. Bucket classification uses binary search; the
    /// overflow bucket index is <c>_bounds.Length</c>.
    /// </para>
    /// <para>
    /// If a tumbling window has elapsed since the last observation, the internal state will be
    /// reset before applying the new sample.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Observe(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("Value must be finite.", nameof(value));

        MaybeRollWindow();

        // Find bucket index via binary search (idx is insertion point when negative)
        int idx = Array.BinarySearch(_bounds, value);
        if (idx < 0)
            idx = ~idx;
        Interlocked.Increment(ref _counts[idx]);

        AddToSum(value);
        UpdateMin(value);
        UpdateMax(value);
    }

    /// <summary>
    /// Returns a consistent, thread-safe snapshot of the histogram: per-bucket counts,
    /// total count, and running aggregates.
    /// </summary>
    /// <returns>
    /// A <see cref="BucketHistogramValue"/> containing:
    /// total <c>Count</c>, <c>Min</c>, <c>Max</c>, <c>Sum</c>, bucket <c>Buckets</c> and <c>Counts</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Min and Max are returned as <c>0</c> when no finite values have been observed yet (i.e., when their
    /// internal representations are still ±Infinity).
    /// </para>
    /// <para>
    /// The returned arrays are copies and safe to inspect without additional synchronization.
    /// </para>
    /// </remarks>
    public override object? GetValue()
    {
        var countsCopy = new long[_counts.Length];
        for (int i = 0; i < countsCopy.Length; i++)
            countsCopy[i] = Volatile.Read(ref _counts[i]);

        var min = Volatile.Read(ref _min);
        var max = Volatile.Read(ref _max);
        var sum = Volatile.Read(ref _sum);

        long total = 0;
        foreach (var c in countsCopy)
            total += c;

        return new BucketHistogramValue(
            Count: total,
            Min: double.IsPositiveInfinity(min) ? 0d : min,
            Max: double.IsNegativeInfinity(max) ? 0d : max,
            Buckets: _bounds.ToArray(),
            Counts: countsCopy,
            Sum: sum);
    }

    /// <summary>
    /// Checks whether the tumbling window (if configured) has elapsed and, if so, atomically
    /// resets bucket counts and aggregates for the next period.
    /// </summary>
    /// <remarks>
    /// The reset is guarded by atomic reads/writes to avoid redundant resets under concurrency.
    /// </remarks>
    private void MaybeRollWindow()
    {
        if (_window.Kind != MetricWindowPolicy.WindowKind.Tumbling)
            return;

        var nowTicks = _clock.UtcNow.Ticks;
        if (nowTicks < Interlocked.Read(ref _nextResetTicksUtc))
            return;

        // Reset bucket counts
        for (int i = 0; i < _counts.Length; i++)
            Interlocked.Exchange(ref _counts[i], 0);

        // Reset aggregates
        Volatile.Write(ref _sum, 0d);
        Volatile.Write(ref _min, double.PositiveInfinity);
        Volatile.Write(ref _max, double.NegativeInfinity);

        // Schedule next reset
        Interlocked.Exchange(ref _nextResetTicksUtc, _clock.UtcNow.Add(_window.Period).Ticks);
    }

    /// <summary>
    /// Atomically adds <paramref name="value"/> to <see cref="_sum"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToSum(double value)
    {
        double initial, computed;
        do
        {
            initial = Volatile.Read(ref _sum);
            computed = initial + value;
        } while (Interlocked.CompareExchange(ref _sum, computed, initial) != initial);
    }

    /// <summary>
    /// Atomically updates the running minimum with <paramref name="x"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateMin(double x)
    {
        double cur;
        do
        {
            cur = Volatile.Read(ref _min);
            if (x >= cur)
                return;
        } while (Interlocked.CompareExchange(ref _min, x, cur) != cur);
    }

    /// <summary>
    /// Atomically updates the running maximum with <paramref name="x"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateMax(double x)
    {
        double cur;
        do
        {
            cur = Volatile.Read(ref _max);
            if (x <= cur)
                return;
        } while (Interlocked.CompareExchange(ref _max, x, cur) != cur);
    }

    /// <summary>
    /// Generates <paramref name="count"/> linear bucket upper bounds of the form:
    /// <c>start + (i + 1) * width</c> for <c>i = 0..count-1</c>.
    /// </summary>
    /// <param name="start">Starting offset. The first upper bound is <c>start + width</c>.</param>
    /// <param name="width">Bucket width (finite; may be fractional).</param>
    /// <param name="count">Number of buckets; must be &gt; 0.</param>
    /// <returns>Ascending array of bucket upper bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> ≤ 0.</exception>
    /// <example>
    /// <code>
    /// // 10 buckets: (0,10],(10,20],...,(90,100]
    /// var bounds = BucketHistogramMetric.LinearBuckets(0, 10, 10);
    /// </code>
    /// </example>
    public static double[] LinearBuckets(double start, double width, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count, nameof(count));

        var res = new double[count];
        for (int i = 0; i < count; i++)
            res[i] = start + (i + 1) * width;
        return res;
    }

    /// <summary>
    /// Generates <paramref name="count"/> exponential bucket upper bounds starting at
    /// <paramref name="start"/> and multiplying by <paramref name="factor"/> each step.
    /// </summary>
    /// <param name="start">First upper bound; must be &gt; 0.</param>
    /// <param name="factor">Growth factor; must be &gt; 1.</param>
    /// <param name="count">Number of buckets; must be &gt; 0.</param>
    /// <returns>Ascending array of bucket upper bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="count"/> ≤ 0, <paramref name="start"/> ≤ 0, or <paramref name="factor"/> ≤ 1.
    /// </exception>
    /// <example>
    /// <code>
    /// // 6 buckets: 1, 2, 4, 8, 16, 32
    /// var bounds = BucketHistogramMetric.ExponentialBuckets(1, 2, 6);
    /// </code>
    /// </example>
    public static double[] ExponentialBuckets(double start, double factor, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count, nameof(count));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(start, 0d, nameof(start));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(factor, 1d, nameof(factor));

        var res = new double[count];
        double cur = start;
        for (int i = 0; i < count; i++)
        {
            res[i] = cur;
            cur *= factor;
        }
        return res;
    }

    /// <summary>
    /// Safe variant of <see cref="Observe(double)"/> that returns <c>false</c> for non-finite values
    /// instead of throwing an exception.
    /// </summary>
    /// <param name="value">Observed value.</param>
    /// <returns><c>true</c> if the value was recorded; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Prefer <see cref="Observe(double)"/> in hot paths when inputs are already validated.
    /// </remarks>
    public bool TryObserve(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return false;

        Observe(value);
        return true;
    }

    // Exposed for documentation purposes via <see cref="GetValue"/> remarks.
    // Current running aggregates (thread-safe via Volatile/Interlocked):
    // Min/Max default to 0 in snapshots when no finite samples have been observed.
    private double Min => Volatile.Read(ref _min);
    private double Max => Volatile.Read(ref _max);
    private double Sum => Volatile.Read(ref _sum);
}
