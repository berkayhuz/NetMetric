// <copyright file="SummaryMetric.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.Summary;

/// <summary>
/// Online/streaming quantile metric based on the P² algorithm.
/// </summary>
/// <remarks>
/// <para>
/// A summary metric computes approximate quantiles (e.g., median, p90, p99) for a stream of values,
/// without storing the full dataset.  
/// Internally, it delegates to <see cref="MultiP2Estimator"/> for per-quantile estimation.
/// </para>
/// <para>
/// Windowing behavior:
/// <list type="bullet">
///   <item><description><b>Cumulative</b> (default): aggregates all values since creation.</description></item>
///   <item><description><b>Tumbling</b>: automatically resets estimators after each configured period.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Thread-safety:</b> All operations are safe for concurrent readers/writers.  
/// Quantile estimation is approximate and converges with more samples.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Example: track request latency distribution
/// var summary = factory.Summary("http.latency", "HTTP Latency")
///     .WithUnit("ms")
///     .WithDescription("Latency distribution of incoming HTTP requests")
///     .WithQuantiles(0.5, 0.9, 0.99)
///     .WithWindow(MetricWindowPolicy.Tumbling(TimeSpan.FromMinutes(1)))
///     .Build();
///
/// // Record observations
/// summary.Record(123);
/// summary.Record(250);
///
/// // Retrieve snapshot
/// var snapshot = (SummaryValue)summary.GetValue();
/// Console.WriteLine($"count={snapshot.Count} p90={snapshot.Quantiles[0.9]} min={snapshot.Min} max={snapshot.Max}");
/// </code>
/// </example>
public sealed class SummaryMetric : MetricBase, ISummaryMetric
{
    private readonly MultiP2Estimator _estimator;
    private readonly MetricWindowPolicy _window;
    private readonly ITimeProvider _clock;
    private long _nextResetTicksUtc; // for tumbling window

    /// <summary>
    /// Gets the quantiles tracked by this summary (each strictly within (0,1)).
    /// </summary>
    /// <remarks>
    /// Quantiles are fixed at construction. Duplicate entries are allowed but redundant.
    /// </remarks>
    public IReadOnlyList<double> Quantiles { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SummaryMetric"/> class.
    /// </summary>
    /// <param name="id">Stable unique identifier (e.g., <c>"http.latency"</c>).</param>
    /// <param name="name">Human-readable metric name (e.g., <c>"HTTP Latency"</c>).</param>
    /// <param name="quantiles">Quantiles to track (e.g., <c>{0.5, 0.9, 0.99}</c>).</param>
    /// <param name="tags">Optional metric tags for dimensioning.</param>
    /// <param name="window">Optional windowing policy; defaults to cumulative.</param>
    /// <param name="clock">Optional time provider; defaults to <see cref="UtcTimeProvider"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="quantiles"/> is <c>null</c>.</exception>
    public SummaryMetric(
        string id,
        string name,
        IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags = null,
        MetricWindowPolicy? window = null,
        ITimeProvider? clock = null)
        : base(id, name, InstrumentKind.Summary, tags)
    {
        Quantiles = quantiles?.ToArray() ?? throw new ArgumentNullException(nameof(quantiles));
        _estimator = new MultiP2Estimator(Quantiles);
        _window = window ?? MetricWindowPolicy.Cumulative;
        _clock = clock ?? new UtcTimeProvider();

        if (_window.Kind == MetricWindowPolicy.WindowKind.Tumbling)
            _nextResetTicksUtc = _clock.UtcNow.Add(_window.Period).Ticks;
    }

    /// <summary>
    /// Records a new observation.
    /// </summary>
    /// <param name="value">Observed value (must be finite).</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is NaN or Infinity.</exception>
    /// <remarks>
    /// Each observation is broadcast to all underlying quantile estimators.  
    /// If a tumbling window has elapsed, the estimators are reset before recording.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("Value must be finite.", nameof(value));

        MaybeRollWindow();
        _estimator.Add(value);
    }

    /// <summary>
    /// Produces a snapshot of the current summary state.
    /// </summary>
    /// <returns>
    /// A <see cref="SummaryValue"/> containing:
    /// <list type="bullet">
    ///   <item><description>Total <c>Count</c> of samples.</description></item>
    ///   <item><description><c>Min</c> and <c>Max</c> observed values.</description></item>
    ///   <item><description>Estimated quantile values for each configured quantile.</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// Quantile estimates are approximate; for small samples they may be noisy but converge
    /// as more data arrives.
    /// </para>
    /// </remarks>
    public override object? GetValue()
    {
        var dict = new Dictionary<double, double>(Quantiles.Count);
        foreach (var q in Quantiles)
            dict[q] = _estimator.GetQuantile(q);

        var (min, max) = _estimator.GetMinMax();
        return new SummaryValue(_estimator.Count, min, max, dict);
    }

    /// <summary>
    /// Attempts to record a new observation without throwing.
    /// </summary>
    /// <param name="value">Observed value.</param>
    /// <returns><c>true</c> if recorded; <c>false</c> if invalid.</returns>
    /// <remarks>
    /// Useful in hot paths when input may occasionally be NaN/Infinity and you want to skip
    /// invalid samples silently.
    /// </remarks>
    public bool TryRecord(double value)
    {
        if (!double.IsFinite(value))
            return false;

        Record(value);
        return true;
    }

    /// <summary>
    /// Resets estimators if the tumbling window has elapsed.
    /// </summary>
    /// <remarks>
    /// Executed lazily on the next <see cref="Record"/> after the scheduled reset time.
    /// Safe under concurrency; rare double resets may occur but yield consistent results.
    /// </remarks>
    private void MaybeRollWindow()
    {
        if (_window.Kind != MetricWindowPolicy.WindowKind.Tumbling)
            return;

        var nowTicks = _clock.UtcNow.Ticks;
        if (nowTicks >= Interlocked.Read(ref _nextResetTicksUtc))
        {
            _estimator.Reset();
            Interlocked.Exchange(ref _nextResetTicksUtc, _clock.UtcNow.Add(_window.Period).Ticks);
        }
    }
}
