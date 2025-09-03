// <copyright file="TimerMetric.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.Timer;

/// <summary>
/// A high-precision timer metric that records elapsed durations (in milliseconds) into an
/// underlying <see cref="HistogramMetric"/> for percentile/statistical analysis (min, max, p50, p90, p99).
/// </summary>
/// <remarks>
/// <para>
/// <b>Key characteristics:</b>
/// <list type="bullet">
///   <item><description><b>Zero/low allocation:</b> uses a readonly struct (<see cref="TimerScope"/>) to avoid per-measurement heap allocations.</description></item>
///   <item><description><b>Thread-safe:</b> synchronization is delegated to the underlying histogram implementation.</description></item>
///   <item><description><b>Ergonomic API:</b> provides helpers like <see cref="Start"/>, <see cref="Measure(Action)"/>,
///   and <see cref="Measure{T}(Func{T})"/> in addition to direct <see cref="Record(TimeSpan)"/> and <see cref="RecordMilliseconds(double)"/> APIs.</description></item>
/// </list>
/// </para>
/// <para>
/// All durations are recorded in <b>milliseconds</b>, with nanosecond resolution from <see cref="Stopwatch"/> internally.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example: measure service operation time
/// var timer = factory.Timer("svc.operation.duration", "Operation Duration")
///     .WithUnit("ms")
///     .WithDescription("Latency distribution of service operations")
///     .WithHistogramCapacity(4096)
///     .Build();
///
/// // Option 1: Manual timing with Start()
/// using (timer.Start())
/// {
///     PerformWork();
/// }
///
/// // Option 2: Inline measurement helpers
/// timer.Measure(() => PerformWork());
/// var result = timer.Measure(() => ComputeValue());
///
/// // Option 3: Direct recording
/// var sw = Stopwatch.StartNew();
/// DoSomething();
/// sw.Stop();
/// timer.Record(sw.Elapsed);
///
/// // Snapshot
/// var dist = (DistributionValue)timer.GetValue();
/// Console.WriteLine($"count={dist.Count} p90={dist.P90}ms");
/// </code>
/// </example>
public sealed class TimerMetric : MetricBase, ITimerMetric
{
    private readonly HistogramMetric _hist;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimerMetric"/> class.
    /// </summary>
    /// <param name="id">Stable unique identifier (e.g., <c>"http.request.duration"</c>).</param>
    /// <param name="name">Human-readable name (e.g., <c>"HTTP Request Duration"</c>).</param>
    /// <param name="tags">Optional metric tags.</param>
    /// <param name="capacity">Capacity of the underlying histogram window (default = 2048).</param>
    /// <remarks>
    /// The histogram retains the last <paramref name="capacity"/> samples for percentile computation.
    /// </remarks>
    public TimerMetric(string id, string name, IReadOnlyDictionary<string, string>? tags = null, int capacity = 2048)
        : base(id, name, InstrumentKind.Histogram, tags, unit: "ms", description: "Elapsed time in milliseconds")
    {
        _hist = new HistogramMetric(id, name, tags, capacity);
    }

    /// <summary>
    /// Starts a new timer scope for measuring elapsed time.
    /// </summary>
    /// <returns>An <see cref="ITimerScope"/> that records duration on <c>Dispose</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ITimerScope Start() => StartScope();

    /// <summary>
    /// Alias for <see cref="Start"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ITimerScope StartMeasurement() => Start();

    /// <summary>
    /// Starts a new timer scope without boxing (internal use when a struct return is acceptable).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TimerScope StartScope() => new TimerScope(this);

    /// <summary>
    /// Measures the execution time of the given <see cref="Action"/> and records the duration.
    /// </summary>
    /// <param name="action">The action to execute and measure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is null.</exception>
    public void Measure(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var scope = StartScope(); // zero-alloc struct
        try { action(); }
        finally { scope.Dispose(); }
    }

    /// <summary>
    /// Measures the execution time of the given function and records the duration.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute and measure.</param>
    /// <returns>The return value of <paramref name="func"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is null.</exception>
    public T Measure<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var scope = StartScope(); // zero-alloc struct
        try { return func(); }
        finally { scope.Dispose(); }
    }

    /// <summary>
    /// Records a duration using a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="elapsed">The elapsed time to record.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(TimeSpan elapsed) => _hist.Record(elapsed.TotalMilliseconds);

    /// <summary>
    /// Records a duration in milliseconds.
    /// </summary>
    /// <param name="ms">Elapsed time in milliseconds.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="ms"/> is NaN or Infinity.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordMilliseconds(double ms)
    {
        if (double.IsNaN(ms) || double.IsInfinity(ms))
            throw new ArgumentException("Value must be a valid finite number.", nameof(ms));
        _hist.Record(ms);
    }

    /// <summary>
    /// Returns the current histogram snapshot containing distribution statistics of recorded durations.
    /// </summary>
    /// <returns>A <see cref="DistributionValue"/> snapshot.</returns>
    public override object? GetValue() => _hist.GetValue();

    /// <summary>
    /// Records a raw duration in stopwatch ticks. Internal use only.
    /// </summary>
    /// <param name="ticks">Elapsed ticks as returned by <see cref="Stopwatch.GetTimestamp"/>.</param>
    /// <remarks>
    /// Used by <see cref="TimerScope"/> when disposed. Converts ticks to milliseconds via <see cref="TimeUtil.TicksToMs"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void RecordTicks(long ticks) => _hist.Record(ticks * TimeUtil.TicksToMs);
}

/// <summary>
/// A scope struct that measures elapsed time and records it into a parent <see cref="TimerMetric"/> upon disposal.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TimerScope"/> is a readonly struct (zero allocation) intended to be used with <c>using</c> blocks.  
/// On <c>Dispose</c>, it computes elapsed ticks since creation and records the duration in milliseconds.
/// </para>
/// <para>
/// Equality is defined by owning <see cref="TimerMetric"/> reference and start timestamp.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using (timer.Start())   // returns ITimerScope implemented by TimerScope
/// {
///     DoWork();
/// }
/// // On Dispose(), duration is recorded
/// </code>
/// </example>
internal readonly struct TimerScope : ITimerScope, IDisposable, IEquatable<TimerScope>
{
    private readonly TimerMetric _owner;
    private readonly long _start;

    /// <summary>
    /// Initializes a new <see cref="TimerScope"/> for the given <paramref name="owner"/>.
    /// </summary>
    /// <param name="owner">The parent <see cref="TimerMetric"/> to record into.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="owner"/> is null.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TimerScope(TimerMetric owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _start = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Stops the timer and records the elapsed duration into the parent <see cref="TimerMetric"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        var elapsed = Stopwatch.GetTimestamp() - _start;
        _owner.RecordTicks(elapsed);
    }

    /// <summary>
    /// Two scopes are equal if they reference the same <see cref="TimerMetric"/> and were started at the same timestamp.
    /// </summary>
    public bool Equals(TimerScope other) =>
        ReferenceEquals(_owner, other._owner) && _start == other._start;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is TimerScope other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            int h = 17;
            h = (h * 31) + (_owner is null ? 0 : _owner.GetHashCode());
            h = (h * 31) + _start.GetHashCode();
            return h;
        }
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TimerScope left, TimerScope right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TimerScope left, TimerScope right) => !left.Equals(right);
}
