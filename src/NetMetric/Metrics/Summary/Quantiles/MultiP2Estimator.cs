// <copyright file="MultiP2Estimator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.Summary.Quantiles;

/// <summary>
/// Maintains multiple <see cref="P2QuantileEstimator"/> instances to approximate
/// several quantiles concurrently using the P² (Piecewise-Parabolic) algorithm.
/// </summary>
/// <remarks>
/// <para>
/// The P² algorithm provides streaming, order-statistics approximations (e.g., p50, p90, p99)
/// without storing the entire sample set. Each configured quantile is tracked by an internal
/// <see cref="P2QuantileEstimator"/> instance that maintains five markers and updates them on
/// each observation.
/// </para>
/// <para>
/// <b>Thread safety:</b> All operations are synchronized on an internal lock, making the estimator
/// safe for concurrent writers and readers. The cost of each <see cref="Add(double)"/> call is
/// O(<c>k</c>) where <c>k</c> is the number of configured quantiles.
/// </para>
/// <para>
/// <b>Numerical behavior:</b> P² is an approximation; exact results may differ from batch
/// percentiles computed over a fully retained dataset, especially for small samples or highly
/// multi-modal distributions. However, it is very memory efficient (O(<c>k</c>)) and suitable for
/// high-throughput streams.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Track median, p90, p99 using P² estimators
/// var qs = new[] { 0.5, 0.9, 0.99 };
/// var est = new MultiP2Estimator(qs);
///
/// // Stream observations
/// foreach (var v in samples)
///     est.Add(v);
///
/// // Query quantiles
/// double p50 = est.GetQuantile(0.5);
/// double p90 = est.GetQuantile(0.9);
/// double p99 = est.GetQuantile(0.99);
///
/// // Count and min/max across the stream
/// long n = est.Count;
/// var (min, max) = est.GetMinMax();
///
/// // Reset if you need to start a fresh window
/// est.Reset();
/// </code>
/// </example>
internal sealed class MultiP2Estimator : IQuantileEstimator
{
    private readonly object _lock = new();
    private readonly double[] _qs;
    private readonly P2QuantileEstimator[] _estimators;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiP2Estimator"/> class.
    /// </summary>
    /// <param name="qs">
    /// Quantiles to track. Each value must lie strictly within the open interval <c>(0, 1)</c>.
    /// For example, <c>0.5</c> for the median, <c>0.9</c> for the 90th percentile.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="qs"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown if no quantiles are provided, or if any <c>q</c> is outside the range <c>(0, 1)</c>.
    /// </exception>
    /// <remarks>
    /// Duplicate quantiles are allowed but not recommended; retrieving a quantile requires an exact
    /// match to a configured <c>q</c>.
    /// </remarks>
    public MultiP2Estimator(IEnumerable<double> qs)
    {
        _qs = qs?.ToArray() ?? throw new ArgumentNullException(nameof(qs));
        if (_qs.Length == 0)
            throw new ArgumentException("At least one quantile required.", nameof(qs));
        if (_qs.Any(q => q <= 0 || q >= 1))
            throw new ArgumentException("q must be in (0,1).", nameof(qs));

        _estimators = _qs.Select(q => new P2QuantileEstimator(q)).ToArray();
    }

    /// <summary>
    /// Gets the number of observations processed so far.
    /// </summary>
    /// <remarks>
    /// The count is reported from the first configured estimator, which is consistent across all
    /// estimators since every observation is broadcast to each one.
    /// </remarks>
    public long Count
    {
        get
        {
            lock (_lock)
                return _estimators[0].Count;
        }
    }

    /// <summary>
    /// Adds a new observation to all underlying P² quantile estimators.
    /// </summary>
    /// <param name="value">The observed value. Must be a finite number.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is NaN or Infinity.</exception>
    /// <remarks>
    /// Complexity is O(<c>k</c>) per call where <c>k</c> is the number of tracked quantiles.
    /// </remarks>
    public void Add(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("Value must be finite.", nameof(value));

        lock (_lock)
        {
            foreach (var e in _estimators)
            {
                e.Add(value);
            }
        }
    }

    /// <summary>
    /// Retrieves the current estimated value of the requested quantile.
    /// </summary>
    /// <param name="q">
    /// The quantile to retrieve. Must exactly match one of the configured values passed to the constructor.
    /// </param>
    /// <returns>The current P² estimate of the requested quantile.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="q"/> was not configured.</exception>
    public double GetQuantile(double q)
    {
        int i = Array.IndexOf(_qs, q);
        if (i < 0)
            throw new ArgumentException("Unsupported q.", nameof(q));

        lock (_lock)
            return _estimators[i].GetQuantile();
    }

    /// <summary>
    /// Returns the minimum and maximum observed values across all estimators.
    /// </summary>
    /// <returns>
    /// A tuple <c>(min, max)</c>. If no values have been added yet, both are <c>0</c>.
    /// </returns>
    /// <remarks>
    /// The min/max are tracked by each P² estimator; this method aggregates across them and
    /// normalizes infinities to <c>0</c> for the empty-stream case.
    /// </remarks>
    public (double min, double max) GetMinMax()
    {
        lock (_lock)
        {
            double min = _estimators.Min(e => e.Min);
            double max = _estimators.Max(e => e.Max);

            var minOut = double.IsPositiveInfinity(min) ? 0d : min;
            var maxOut = double.IsNegativeInfinity(max) ? 0d : max;
            return (minOut, maxOut);
        }
    }

    /// <summary>
    /// Resets all estimators to their initial state, discarding previously observed values.
    /// </summary>
    /// <remarks>
    /// After <see cref="Reset"/>, <see cref="Count"/> returns <c>0</c> and subsequent quantile
    /// estimates depend only on new observations.
    /// </remarks>
    public void Reset()
    {
        lock (_lock)
        {
            foreach (var e in _estimators)
            {
                e.Reset();
            }
        }
    }
}
