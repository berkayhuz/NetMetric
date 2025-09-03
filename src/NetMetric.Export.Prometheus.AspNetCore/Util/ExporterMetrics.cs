// <copyright file="ExporterMetrics.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Globalization;
using System.Runtime.CompilerServices;

namespace NetMetric.Export.Prometheus.AspNetCore.Util;

/// <summary>
/// Provides internal counters, gauges, and a histogram to publish
/// self-metrics of the Prometheus exporter (e.g., request counts,
/// in-flight scrapes, last payload size, scrape durations).
/// </summary>
/// <remarks>
/// <para>
/// All members are thread-safe. State updates use <see cref="System.Threading.Interlocked"/> operations
/// and a small critical section protected by <see langword="lock"/> only when maintaining
/// the per-reason error dictionary. Reads in <see cref="WritePrometheus(System.IO.TextWriter, string)"/>
/// use <see cref="System.Threading.Volatile.Read{T}(ref readonly T)"/> where appropriate to avoid torn reads.
/// </para>
/// <para>
/// The metrics rendered by <see cref="WritePrometheus(System.IO.TextWriter, string)"/>
/// are formatted according to Prometheus' text exposition format. Metric names are
/// emitted under the caller-provided <c>prefix</c> allowing the exporter to integrate
/// cleanly into applications that already expose other metrics.
/// </para>
/// <para>
/// <b>Exposed metrics (all prefixed):</b>
/// </para>
/// <list type="bullet">
///   <item><description><c>{prefix}_requests_in_flight</c> (gauge)</description></item>
///   <item><description><c>{prefix}_scrapes_total</c> (counter)</description></item>
///   <item><description><c>{prefix}_rate_limited_total</c> (counter)</description></item>
///   <item><description><c>{prefix}_errors_total</c> (counter, labeled by <c>reason</c>)</description></item>
///   <item><description><c>{prefix}_last_scrape_size_bytes</c> (gauge)</description></item>
///   <item><description>
///     <c>{prefix}_scrape_duration_seconds</c> (histogram) with
///     <c>_bucket</c>, <c>_sum</c>, and <c>_count</c>.
///   </description></item>
/// </list>
/// <para>
/// <b>Histogram buckets (seconds):</b>
/// <c>[0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, +Inf]</c>.
/// </para>
/// </remarks>
/// <example>
/// <para><b>Recording scrape outcomes</b></para>
/// <code language="csharp"><![CDATA[
/// using NetMetric.Export.Prometheus.AspNetCore.Util;
///
/// // When a scrape begins:
/// ExporterMetrics.InflightIncrement();
///
/// try
/// {
///     // ... perform work, build payload ...
///
///     // Record a successful scrape (duration in seconds, payload size in bytes)
///     ExporterMetrics.OnSuccess(durationSeconds: 0.0123, sizeBytes: payloadBytes);
/// }
/// catch (OperationCanceledException)
/// {
///     ExporterMetrics.CountError(ExporterErrorReason.Timeout);
///     throw;
/// }
/// catch (Exception)
/// {
///     ExporterMetrics.CountError(ExporterErrorReason.Exception);
///     throw;
/// }
/// finally
/// {
///     ExporterMetrics.InflightDecrement();
/// }
/// ]]></code>
/// <para><b>Writing metrics in a HTTP handler</b></para>
/// <code language="csharp"><![CDATA[
/// app.MapGet("/metrics", async context =>
/// {
///     context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
///     using var writer = new StreamWriter(context.Response.Body);
///     ExporterMetrics.WritePrometheus(writer, prefix: "netmetric_exporter");
///     await writer.FlushAsync();
/// });
/// ]]></code>
/// </example>
internal static class ExporterMetrics
{
    // Histogram bucket boundaries (seconds)
    private static readonly double[] Buckets = new[] { 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10 };

    private static long _scrapeTotal;
    private static long _errorTotal;
    private static long _rateLimitedTotal;
    private static double _durationSum;
    private static long _durationCount;
    private static readonly long[] _bucketCounts = new long[Buckets.Length + 1]; // includes +Inf bucket

    private static long _inflight;
    private static long _lastSizeBytes;

    // Errors by reason
    private static readonly Dictionary<ExporterErrorReason, long> _errorByReason = new();

    /// <summary>
    /// Increments the in-flight scrape gauge (<c>{prefix}_requests_in_flight</c>).
    /// </summary>
    /// <remarks>
    /// Call this at the start of a scrape. Always pair with <see cref="InflightDecrement"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InflightIncrement() => Interlocked.Increment(ref _inflight);

    /// <summary>
    /// Decrements the in-flight scrape gauge (<c>{prefix}_requests_in_flight</c>).
    /// </summary>
    /// <remarks>
    /// Call this in a <c>finally</c> block after <see cref="InflightIncrement"/> to ensure correctness on exceptions.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InflightDecrement() => Interlocked.Decrement(ref _inflight);

    /// <summary>
    /// Increments the rate-limited scrape counter (<c>{prefix}_rate_limited_total</c>).
    /// </summary>
    /// <remarks>
    /// Invoke when a scrape is rejected due to rate limiting or concurrency caps.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CountRateLimited() => Interlocked.Increment(ref _rateLimitedTotal);

    /// <summary>
    /// Increments the error counter and attributes the error to the specified <paramref name="reason"/>.
    /// </summary>
    /// <param name="reason">The reason why the error occurred.</param>
    /// <remarks>
    /// <para>
    /// Updates two series:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>{prefix}_errors_total</c> with <c>reason</c> label</description></item>
    ///   <item><description>The aggregate <c>_errorTotal</c> (used internally)</description></item>
    /// </list>
    /// </remarks>
    public static void CountError(ExporterErrorReason reason)
    {
        Interlocked.Increment(ref _errorTotal);
        lock (_errorByReason)
        {
            _errorByReason.TryGetValue(reason, out var v);
            _errorByReason[reason] = v + 1;
        }
    }

    /// <summary>
    /// Records a successful scrape, updating the total scrape counter, the
    /// scrape duration histogram, the histogram sum and count, and the
    /// last emitted payload size gauge.
    /// </summary>
    /// <param name="durationSeconds">The scrape duration in seconds.</param>
    /// <param name="sizeBytes">The size of the scrape response in bytes.</param>
    /// <remarks>
    /// Adds <paramref name="durationSeconds"/> to the histogram buckets
    /// and to <c>{prefix}_scrape_duration_seconds_sum</c>, and increments
    /// <c>{prefix}_scrape_duration_seconds_count</c>. Also updates
    /// <c>{prefix}_scrapes_total</c> and <c>{prefix}_last_scrape_size_bytes</c>.
    /// </remarks>
    public static void OnSuccess(double durationSeconds, long sizeBytes)
    {
        Interlocked.Increment(ref _scrapeTotal);
        Interlocked.Exchange(ref _lastSizeBytes, sizeBytes);

        // Histogram
        Interlocked.Add(ref _durationCount, 1);
        AddToHistogram(durationSeconds);

        // Update sum atomically (CAS loop to avoid lost updates on double)
        double oldSum, newSum;
        do
        {
            oldSum = _durationSum;
            newSum = oldSum + durationSeconds;
        } while (Interlocked.CompareExchange(ref _durationSum, newSum, oldSum) != oldSum);
    }

    /// <summary>
    /// Adds a duration value (in seconds) to the appropriate histogram bucket.
    /// </summary>
    /// <param name="value">The duration in seconds to record.</param>
    /// <remarks>
    /// This method supports <see cref="OnSuccess(double, long)"/> and is not intended
    /// to be called directly by consumers.
    /// </remarks>
    private static void AddToHistogram(double value)
    {
        int i = 0;
        for (; i < Buckets.Length; i++)
        {
            if (value <= Buckets[i])
            {
                Interlocked.Increment(ref _bucketCounts[i]);
                return;
            }
        }
        // +Inf bucket
        Interlocked.Increment(ref _bucketCounts[^1]);
    }

    /// <summary>
    /// Writes the exporter self-metrics in Prometheus text exposition format
    /// using the provided <paramref name="prefix"/> for metric names.
    /// </summary>
    /// <param name="w">The <see cref="System.IO.TextWriter"/> to write to.</param>
    /// <param name="prefix">The metric name prefix to apply (for example, <c>netmetric_exporter</c>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="w"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The output includes <c># TYPE</c> metadata lines and all series for in-flight,
    /// scrape totals, rate-limits, labeled errors by <see cref="ExporterErrorReason"/>,
    /// last payload size, and histogram series for scrape durations.
    /// </para>
    /// <para>
    /// <b>Performance:</b> this method emits a consistent snapshot with minimal allocation.
    /// It copies the error dictionary under a lock to avoid holding the lock while writing.
    /// </para>
    /// </remarks>
    public static void WritePrometheus(System.IO.TextWriter w, string prefix)
    {
        ArgumentNullException.ThrowIfNull(w);

        var ci = CultureInfo.InvariantCulture;

        // In-flight gauge
        w.WriteLine($"# TYPE {prefix}_requests_in_flight gauge");
        w.Write(prefix);
        w.Write("_requests_in_flight ");
        w.WriteLine(Volatile.Read(ref _inflight).ToString(ci));

        // Scrape total counter
        w.WriteLine($"# TYPE {prefix}_scrapes_total counter");
        w.Write(prefix);
        w.Write("_scrapes_total ");
        w.WriteLine(Volatile.Read(ref _scrapeTotal).ToString(ci));

        // Rate-limited counter
        w.WriteLine($"# TYPE {prefix}_rate_limited_total counter");
        w.Write(prefix);
        w.Write("_rate_limited_total ");
        w.WriteLine(Volatile.Read(ref _rateLimitedTotal).ToString(ci));

        // Errors by reason
        w.WriteLine($"# TYPE {prefix}_errors_total counter");
        Dictionary<ExporterErrorReason, long> snap;
        lock (_errorByReason)
            snap = new Dictionary<ExporterErrorReason, long>(_errorByReason);
        foreach (var kv in snap.OrderBy(k => k.Key.ToString(), StringComparer.Ordinal))
        {
            w.Write(prefix);
            w.Write("_errors_total{reason=\"");
            w.Write(kv.Key.ToString());
            w.Write("\"} ");
            w.WriteLine(kv.Value.ToString(ci));
        }

        // Last size gauge
        w.WriteLine($"# TYPE {prefix}_last_scrape_size_bytes gauge");
        w.Write(prefix);
        w.Write("_last_scrape_size_bytes ");
        w.WriteLine(Volatile.Read(ref _lastSizeBytes).ToString(ci));

        // Duration histogram
        w.WriteLine($"# TYPE {prefix}_scrape_duration_seconds histogram");
        long cum = 0;
        for (int i = 0; i < Buckets.Length; i++)
        {
            cum += Volatile.Read(ref _bucketCounts[i]);
            w.Write(prefix);
            w.Write("_scrape_duration_seconds_bucket{le=\"");
            w.Write(Buckets[i].ToString("0.############", ci));
            w.Write("\"} ");
            w.WriteLine(cum.ToString(ci));
        }
        cum += Volatile.Read(ref _bucketCounts[^1]);
        w.Write(prefix);
        w.Write("_scrape_duration_seconds_bucket{le=\"+Inf\"} ");
        w.WriteLine(cum.ToString(ci));

        var sum = Volatile.Read(ref _durationSum);
        var cnt = Volatile.Read(ref _durationCount);

        w.Write(prefix);
        w.Write("_scrape_duration_seconds_sum ");
        w.WriteLine(sum.ToString("0.############", ci));
        w.Write(prefix);
        w.Write("_scrape_duration_seconds_count ");
        w.WriteLine(cnt.ToString(ci));
    }
}

/// <summary>
/// Enumerates canonical reasons for scrape errors encountered by the exporter.
/// </summary>
/// <remarks>
/// Used as the value for the <c>reason</c> label on <c>{prefix}_errors_total</c>.
/// </remarks>
internal enum ExporterErrorReason
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>The scrape was canceled due to timeout.</summary>
    Timeout,

    /// <summary>An unhandled exception occurred during the scrape.</summary>
    Exception,

    /// <summary>The request host was not in the configured allowlist.</summary>
    HostDenied,

    /// <summary>The client IP was not in the configured allowed networks.</summary>
    IpDenied,

    /// <summary>A required or validated proxy header failed validation.</summary>
    ProxyViolation,

    /// <summary>Basic authentication failed.</summary>
    BasicAuthFailed,

    /// <summary>Mutual TLS (mTLS) client certificate validation failed.</summary>
    MtlsFailed,

    /// <summary>The client IP address could not be determined.</summary>
    ClientIpUnknown
}
