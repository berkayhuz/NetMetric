// <copyright file="P2QuantileEstimator.cs" company="NetMetric"
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.Summary.Quantiles;

/// <summary>
/// Implements the P² (Jain–Chlamtac) algorithm for single-quantile estimation.
/// </summary>
/// <remarks>
/// <para>
/// The P² algorithm approximates an order statistic (e.g., median, p95, p99) from a data stream
/// without storing all samples. It maintains five markers whose heights and positions are adjusted
/// online as new observations arrive. The memory footprint is O(1) and per-sample update cost is O(1).
/// </para>
/// <para>
/// <b>Bootstrap phase.</b> Until the first five samples have been observed, the estimator
/// accumulates values and returns a simple quantile of the bootstrapped set. After five samples,
/// markers are initialized and the parabolic/linear correction rules take over.
/// </para>
/// <para>
/// <b>Thread-safety.</b> This type is not intrinsically thread-safe; coordinate external access
/// (e.g., via a lock) if used from multiple threads. For a thread-safe multi-quantile façade,
/// see <see cref="MultiP2Estimator"/>.
/// </para>
/// <para>
/// <b>Accuracy &amp; caveats.</b> P² returns an approximation that converges as sample size grows.
/// For very small streams or highly multi-modal distributions, the estimate can deviate from the
/// exact batch quantile. P² preserves running <see cref="Min"/> and <see cref="Max"/> as well.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Track p95 for request latency (ms)
/// var p95 = new P2QuantileEstimator(0.95);
/// foreach (var ms in latencies)
///     p95.Add(ms);
///
/// Console.WriteLine($"count={p95.Count} p95≈{p95.GetQuantile():F2} min={p95.Min:F2} max={p95.Max:F2}");
/// </code>
/// </example>
/// <example>
/// <code>
/// // Coordinating thread safety with an external lock
/// var est = new P2QuantileEstimator(0.5); // median
/// var gate = new object();
///
/// void Observe(double x)
/// {
///     lock (gate)
///     {
///         est.Add(x);
///     }
/// }
/// </code>
/// </example>
internal sealed class P2QuantileEstimator
{
    private readonly double _q;
    private bool _initialized;

    // Marker heights (q0..q4), positions (n), desired positions (np), and position deltas (dn)
    private readonly double[] _qHeights = new double[5];
    private readonly double[] _n = new double[5];
    private readonly double[] _np = new double[5];
    private readonly double[] _dn = new double[5];

    private long _count;
    private double _min = double.PositiveInfinity;
    private double _max = double.NegativeInfinity;

    // Temporary storage for the first few samples
    private double[]? _bootstrap;
    private int _bootstrapCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="P2QuantileEstimator"/> class.
    /// </summary>
    /// <param name="q">The target quantile to estimate; must be strictly within (0,1).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="q"/> is not in (0,1).</exception>
    public P2QuantileEstimator(double q)
    {
        if (q <= 0 || q >= 1)
            throw new ArgumentOutOfRangeException(nameof(q), "q must be in (0,1).");
        _q = q;
    }

    /// <summary>
    /// Gets the total number of samples processed.
    /// </summary>
    public long Count => _count;

    /// <summary>
    /// Gets the minimum value observed so far (∞ until the first sample).
    /// </summary>
    public double Min => _min;

    /// <summary>
    /// Gets the maximum value observed so far (-∞ until the first sample).
    /// </summary>
    public double Max => _max;

    /// <summary>
    /// Resets the estimator to its initial state, discarding all accumulated information.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        _initialized = false;
        Array.Clear(_qHeights);
        Array.Clear(_n);
        Array.Clear(_np);
        Array.Clear(_dn);
        _count = 0;
        _min = double.PositiveInfinity;
        _max = double.NegativeInfinity;
        _bootstrap = null;
        _bootstrapCount = 0;
    }

    /// <summary>
    /// Adds a new sample to the estimator.
    /// </summary>
    /// <param name="x">The sample value; must be finite.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="x"/> is NaN or Infinity.</exception>
    public void Add(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x))
            throw new ArgumentException("Value must be finite.", nameof(x));

        _count++;
        if (x < _min) _min = x;
        if (x > _max) _max = x;

        if (!_initialized)
        {
            Bootstrap(x);
            return;
        }

        // Select cell k in which x belongs, then update marker positions.
        // k in {0..3}, markers are indices 0..4, with q[2] targeting the requested quantile.
        int k = x < _qHeights[0] ? 0 :
                x >= _qHeights[4] ? 3 :
                x < _qHeights[1] ? 0 :
                x < _qHeights[2] ? 1 :
                x < _qHeights[3] ? 2 : 3;

        if (x < _qHeights[0]) _qHeights[0] = x;
        else if (x > _qHeights[4]) _qHeights[4] = x;

        for (int i = k + 1; i < 5; i++)
            _n[i] += 1;

        // Desired positions after the new observation
        _np[0] = 1;
        _np[1] += _q / 2.0;
        _np[2] += _q;
        _np[3] += (1 + _q) / 2.0;
        _np[4] = _count;

        // Adjust the interior markers by at most 1 position using parabolic prediction,
        // falling back to linear interpolation if the prediction would violate monotonicity.
        for (int i = 1; i <= 3; i++)
        {
            _dn[i] = _np[i] - _n[i];
            bool move = (_dn[i] >= 1 && _n[i + 1] - _n[i] > 1) || (_dn[i] <= -1 && _n[i - 1] - _n[i] < -1);
            if (move)
            {
                int d = Math.Sign(_dn[i]);
                double qip = Parabolic(i, d);
                _qHeights[i] = (_qHeights[i - 1] < qip && qip < _qHeights[i + 1]) ? qip : Linear(i, d);
                _n[i] += d;
            }
        }
    }

    /// <summary>
    /// Gets the current quantile estimate.
    /// </summary>
    /// <returns>
    /// The estimated quantile value. During the bootstrap phase (fewer than five samples),
    /// returns the quantile of the bootstrapped subset.
    /// </returns>
    public double GetQuantile() => _initialized ? _qHeights[2] : BootstrapQuantile();

    /// <summary>
    /// Accumulates initial samples until five are available and initializes marker heights/positions.
    /// </summary>
    /// <param name="x">The current sample.</param>
    private void Bootstrap(double x)
    {
        _bootstrap ??= new double[5];
        _bootstrap[_bootstrapCount++] = x;
        if (_bootstrapCount < 5)
            return;

        Array.Sort(_bootstrap, 0, 5);
        for (int i = 0; i < 5; i++)
        {
            _qHeights[i] = _bootstrap[i];
            _n[i] = i + 1;
        }

        // Desired positions according to the original P² initialization
        _np[0] = 1;
        _np[1] = 1 + 2 * _q;
        _np[2] = 1 + 4 * _q;
        _np[3] = 3 + 2 * _q;
        _np[4] = 5;

        _initialized = true;
        _bootstrap = null;
    }

    /// <summary>
    /// Computes the quantile from the bootstrapped subset (prior to full initialization).
    /// </summary>
    /// <returns>The bootstrapped quantile or NaN if no samples have been observed yet.</returns>
    private double BootstrapQuantile()
    {
        if (_bootstrapCount == 0)
            return double.NaN;

        var copy = new double[_bootstrapCount];
        Array.Copy(_bootstrap!, copy, _bootstrapCount);
        Array.Sort(copy);

        int idx = (int)Math.Round((_bootstrapCount - 1) * _q, MidpointRounding.ToZero);
        return copy[idx];
    }

    /// <summary>
    /// Parabolic prediction for the next marker height at interior index <paramref name="i"/>.
    /// </summary>
    /// <param name="i">Interior marker index in {1,2,3}.</param>
    /// <param name="d">Adjustment direction (+1 or -1).</param>
    /// <returns>The predicted height.</returns>
    private double Parabolic(int i, int d)
    {
        double n0 = _n[i - 1], n1 = _n[i], n2 = _n[i + 1];
        double q0 = _qHeights[i - 1], q1 = _qHeights[i], q2 = _qHeights[i + 1];

        double a = d / (n2 - n0);
        double b = (n1 - n0 + d) * (q2 - q1) / (n2 - n1);
        double c = (n2 - n1 - d) * (q1 - q0) / (n1 - n0);
        return q1 + a * (b + c);
    }

    /// <summary>
    /// Linear fallback interpolation for the next marker height at interior index <paramref name="i"/>.
    /// </summary>
    /// <param name="i">Interior marker index in {1,2,3}.</param>
    /// <param name="d">Adjustment direction (+1 or -1).</param>
    /// <returns>The interpolated height.</returns>
    private double Linear(int i, int d)
    {
        return _qHeights[i] + d * (_qHeights[i + d] - _qHeights[i]) / (_n[i + d] - _n[i]);
    }
}
