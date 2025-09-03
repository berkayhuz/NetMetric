// <copyright file="CpuAffinityCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Collects CPU affinity information for the current process and exposes it as a single gauge metric.
/// </summary>
/// <remarks>
/// <para>
/// This collector computes the count of CPUs the current process is allowed to run on (its processor
/// affinity set). When processor affinity is available (Windows and Linux are supported in the current
/// implementation), the metric reflects the exact number of enabled CPUs. The processor affinity
/// bitmask (when available) is emitted as a <c>mask</c> tag in hexadecimal (e.g., <c>0x0000000F</c>).
/// </para>
/// <para>
/// If processor affinity is not supported on the running platform, the collector falls back to
/// <see cref="Environment.ProcessorCount"/> and marks the metric with <c>status=best_effort</c>.
/// </para>
/// <para>
/// The exposed metric is:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><c>cpu.process.affinity.count</c> (Gauge): Number of CPUs allowed by the process's affinity.</description>
///   </item>
/// </list>
/// <para>
/// Tags:
/// </para>
/// <list type="bullet">
///   <item><description><c>status</c>: <c>ok</c> | <c>best_effort</c> | <c>cancelled</c> | <c>error</c></description></item>
///   <item><description><c>mask</c> (optional): Hex representation of the processor affinity bitmask when available.</description></item>
///   <item><description><c>reason</c> (on error): Short error message (truncated to 160 characters).</description></item>
/// </list>
/// <para><b>Thread safety:</b> This collector is stateless aside from metric factory usage and is safe to call concurrently.</para>
/// <para><b>Performance:</b> Uses <see cref="System.Diagnostics.Process.GetCurrentProcess"/> once per call and a simple bit count
/// (Kernighan popcount). Overhead is negligible for typical collection intervals.</para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Typical usage in a module or background job:
/// var collector = new CpuAffinityCollector(metricFactory);
/// var metric = await collector.CollectAsync(ct);
/// // metric is a gauge with value = number of allowed CPUs
/// ]]></code>
/// </example>
public sealed class CpuAffinityCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuAffinityCollector"/> class.
    /// </summary>
    /// <param name="factory">The factory used to create metric instances.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// IMetricFactory factory = /* resolved from DI */;
    /// var collector = new CpuAffinityCollector(factory);
    /// ]]></code>
    /// </example>
    public CpuAffinityCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects the CPU affinity count metric asynchronously.
    /// </summary>
    /// <param name="ct">The cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the collected metric.</returns>
    /// <exception cref="PlatformNotSupportedException">Thrown when the platform does not support processor affinity.</exception>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        const string id = "cpu.process.affinity.count";   // value = #allowed CPUs
        const string name = "Process Affinity (Allowed CPU Count)";

        try
        {
            ct.ThrowIfCancellationRequested();

            var tags = new Dictionary<string, string>(StringComparer.Ordinal) { ["status"] = "ok" };

            double value;

            string? maskHex = null;

            try
            {
                if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                {
                    using var p = Process.GetCurrentProcess();
                    nuint mask = (nuint)p.ProcessorAffinity;

                    maskHex = $"0x{mask:X}";

                    value = CountBits(mask);
                }
                else
                {
                    throw new PlatformNotSupportedException("ProcessorAffinity is not supported on this platform.");
                }
            }
            catch (PlatformNotSupportedException)
            {
                tags["status"] = "best_effort";

                value = Environment.ProcessorCount;
            }

            if (maskHex is not null)
            {
                tags["mask"] = maskHex;
            }

            var gb = _factory.Gauge(id, name);

            foreach (var kv in tags)
            {
                gb.WithTag(kv.Key, kv.Value);
            }

            var g = gb.Build();

            g.SetValue(value);

            return Task.FromResult<IMetric?>(g);
        }
        catch (OperationCanceledException)
        {
            var gb = _factory.Gauge(id, name).WithTag("status", "cancelled");
            var g = gb.Build();

            g.SetValue(0);

            return Task.FromResult<IMetric?>(g);
        }
        catch (Exception ex)
        {
            var gb = _factory.Gauge(id, name).WithTag("status", "error").WithTag("reason", Short(ex.Message));

            var g = gb.Build();

            g.SetValue(0);

            return Task.FromResult<IMetric?>(g);

            throw;
        }

        static int CountBits(nuint x)
        {
            int c = 0;

            while (x != 0)
            {
                x &= (x - 1);
                c++;
            }  // Kernighan popcount

            return c;
        }

        static string Short(string s)
        {
            return string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
        }
    }

    // ---- Explicit IMetricCollector helper methods ----

    /// <summary>
    /// Creates a summary metric.
    /// </summary>
    /// <param name="id">The ID of the summary metric.</param>
    /// <param name="name">The name of the summary metric.</param>
    /// <param name="quantiles">The quantiles for the summary metric.</param>
    /// <param name="tags">Optional tags to associate with the metric.</param>
    /// <param name="resetOnGet">A flag indicating if the summary metric should reset on retrieval. This is ignored in the current implementation.</param>
    /// <returns>A new <see cref="ISummaryMetric"/> instance.</returns>
    public ISummaryMetric CreateSummary(string id, string name, IEnumerable<double> quantiles, IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
    {
        var q = quantiles?.ToArray() ?? new[] { 0.5, 0.9, 0.99 };

        var sb = _factory.Summary(id, name).WithQuantiles(q);

        if (tags is not null)
        {
            foreach (var kv in tags)
            {
                sb.WithTag(kv.Key, kv.Value);
            }
        }

        return sb.Build();
    }

    /// <summary>
    /// Creates a bucket histogram metric.
    /// </summary>
    /// <param name="id">The ID of the histogram metric.</param>
    /// <param name="name">The name of the histogram metric.</param>
    /// <param name="bucketUpperBounds">The upper bounds of the histogram's buckets.</param>
    /// <param name="tags">Optional tags to associate with the metric.</param>
    /// <returns>A new <see cref="IBucketHistogramMetric"/> instance.</returns>
    public IBucketHistogramMetric CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
    {
        var bounds = bucketUpperBounds?.ToArray() ?? Array.Empty<double>();

        var hb = _factory.Histogram(id, name).WithBounds(bounds);

        if (tags is not null)
        {
            foreach (var kv in tags)
            {
                hb.WithTag(kv.Key, kv.Value);
            }
        }

        return hb.Build();
    }
}
