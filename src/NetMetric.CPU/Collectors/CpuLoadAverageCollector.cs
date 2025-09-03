// <copyright file="CpuLoadAverageCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Collects system load averages for 1, 5, and 15 minutes and normalizes each value by the number of logical CPU cores.
/// </summary>
/// <remarks>
/// <para>
/// Platform behavior:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><b>Linux</b>: Reads <c>/proc/loadavg</c> and parses the first three fields, which represent the 1-, 5-, and 15-minute load averages.
///     Each value is divided by <see cref="Environment.ProcessorCount"/> to produce a per-core (normalized) figure.</description>
///   </item>
///   <item>
///     <description><b>macOS</b>: Invokes <c>getloadavg</c> via source-generated interop and normalizes as above.</description>
///   </item>
///   <item>
///     <description><b>Other OSes</b>: Collection is reported as unsupported.</description>
///   </item>
/// </list>
///
/// <para>
/// Output metric:
/// </para>
/// <list type="table">
///   <listheader>
///     <term>Id</term>
///     <description>Description</description>
///   </listheader>
///   <item>
///     <term><c>cpu.load.average</c></term>
///     <description>Multi-gauge with up to three samples (1m, 5m, 15m) representing normalized load averages.</description>
///   </item>
/// </list>
///
/// <para>
/// Each sample is emitted with consistent tags:
/// </para>
/// <list type="table">
///   <listheader>
///     <term>Tag</term>
///     <description>Meaning</description>
///   </listheader>
///   <item>
///     <term><c>window</c></term>
///     <description>One of <c>1m</c>, <c>5m</c>, <c>15m</c>, or status markers such as <c>unsupported</c>, <c>error</c>, or <c>cancelled</c>.</description>
///   </item>
///   <item>
///     <term><c>status</c></term>
///     <description><c>ok</c>, <c>unsupported</c>, <c>error</c>, or <c>cancelled</c>.</description>
///   </item>
///   <item>
///     <term><c>normalized</c></term>
///     <description><c>true</c> if the value has been divided by logical core count; otherwise <c>false</c>.</description>
///   </item>
///   <item>
///     <term><c>os</c></term>
///     <description>Operating system description (e.g., <see cref="RuntimeInformation.OSDescription"/>).</description>
///   </item>
///   <item>
///     <term><c>message</c></term>
///     <description>Optional short error message (present only when <c>status=error</c>).</description>
///   </item>
/// </list>
///
/// <para>
/// Error handling:
/// </para>
/// <list type="bullet">
///   <item><description>On cancellation, emits a single <c>cancelled</c> sample and returns successfully.</description></item>
///   <item><description>On error, emits a single <c>error</c> sample containing a truncated message, then returns successfully.</description></item>
///   <item><description>On unsupported platforms, emits a single <c>unsupported</c> sample.</description></item>
/// </list>
///
/// <para>
/// Performance characteristics: the collector performs lightweight file I/O on Linux or a single native call on macOS,
/// and simple arithmetic normalization. It is suitable for frequent collection intervals.
/// </para>
/// </remarks>
/// <example>
/// The following example registers the collector and reads the most recent values:
/// <code language="csharp"><![CDATA[
/// Acquire the metric factory from your DI container.
/// IMetricFactory factory = services.GetRequiredService<IMetricFactory>();
/// 
/// var collector = new CpuLoadAverageCollector(factory);
/// IMetric? metric = await collector.CollectAsync();
/// 
/// if (metric is IMultiGaugeMetric mg)
/// {
///     foreach (var sample in mg.Get())
///     {
///         Console.WriteLine($"{sample.Value} window={sample.Tags["window"]} normalized={sample.Tags["normalized"]}");
///     }
/// }
/// ]]></code>
/// </example>
public sealed partial class CpuLoadAverageCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuLoadAverageCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metric instruments and builders.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public CpuLoadAverageCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }
#pragma warning disable CA1031
    /// <summary>
    /// Collects the current system load averages and returns a multi-gauge metric containing up to three samples (1m, 5m, 15m).
    /// </summary>
    /// <param name="ct">A token used to observe cancellation requests.</param>
    /// <returns>
    /// A task that completes with an <see cref="IMetric"/> instance containing the collected samples or a single
    /// status sample (<c>unsupported</c>, <c>error</c>, or <c>cancelled</c>) when applicable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method does not throw on typical platform or parsing failures. Instead, it emits a representative status sample
    /// and returns the multi-gauge, allowing pipelines to remain resilient in the presence of transient issues.
    /// </para>
    /// <para>
    /// The returned multi-gauge is configured with <c>ResetOnGet=true</c> so that consumers retrieving values will clear prior samples.
    /// </para>
    /// </remarks>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        var mg = _factory.MultiGauge("cpu.load.average", "System Load Average").WithResetOnGet(true).Build();

        try
        {
            ct.ThrowIfCancellationRequested();

            var (l1, l5, l15, supported, normalized) = await ReadAsync(ct).ConfigureAwait(false);
            var os = RuntimeInformation.OSDescription;

            if (!supported)
            {
                mg.SetValue(0, Tags("unsupported", "unsupported", false, os));
                return mg;
            }

            mg.SetValue(l1, Tags("1m", "ok", normalized, os));
            mg.SetValue(l5, Tags("5m", "ok", normalized, os));
            mg.SetValue(l15, Tags("15m", "ok", normalized, os));

            return mg;
        }
        catch (OperationCanceledException)
        {
            mg.SetValue(0, Tags("cancelled", "cancelled", false, RuntimeInformation.OSDescription));
            return mg;
        }
        catch (Exception ex)
        {
            mg.SetValue(0, Tags("error", "error", false, RuntimeInformation.OSDescription, Short(ex.Message)));
            return mg; // Intentionally returns a status sample to keep the pipeline resilient.
        }
    }
#pragma warning restore CA1031
    // --------------------------
    // Implementation details
    // --------------------------

    /// <summary>
    /// Reads and normalizes the platform load average values.
    /// </summary>
    /// <param name="ct">A token used to observe cancellation requests.</param>
    /// <returns>
    /// A tuple <c>(l1, l5, l15, supported, normalized)</c> where <c>l1</c>, <c>l5</c>, and <c>l15</c> are the per-core
    /// load averages for 1, 5, and 15 minutes respectively; <c>supported</c> indicates whether the platform is supported;
    /// and <c>normalized</c> indicates whether values were divided by logical core count.
    /// </returns>
    private static async Task<(double l1, double l5, double l15, bool supported, bool normalized)> ReadAsync(CancellationToken ct)
    {
        int cores = Math.Max(1, Environment.ProcessorCount); // defensive

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux: "/proc/loadavg" format: "0.42 0.36 0.30 1/123 4567"
            try
            {
                ct.ThrowIfCancellationRequested();

                string s = await File.ReadAllTextAsync("/proc/loadavg", ct).ConfigureAwait(false);

                var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 3
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var l1)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var l5)
                    && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var l15))
                {
                    return (l1 / cores, l5 / cores, l15 / cores, true, true);
                }

                return (0, 0, 0, false, false);
            }
            catch (IOException)
            {
                return (0, 0, 0, false, false);
            }
            catch (UnauthorizedAccessException)
            {
                return (0, 0, 0, false, false);
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: getloadavg(double* samples, int count)
            try
            {
                var loads = new double[3];
                int n = getloadavg(loads, 3);
                if (n >= 3)
                {
                    return (loads[0] / cores, loads[1] / cores, loads[2] / cores, true, true);
                }

                return (0, 0, 0, false, false);
            }
            catch (DllNotFoundException)
            {
                return (0, 0, 0, false, false);
            }
            catch (EntryPointNotFoundException)
            {
                return (0, 0, 0, false, false);
            }
        }

        // Other OSes: not supported.
        return (0, 0, 0, false, false);
    }

    /// <summary>
    /// Creates a uniform tag set for emitted samples.
    /// </summary>
    /// <param name="window">Measurement window identifier (e.g., <c>1m</c>, <c>5m</c>, <c>15m</c>) or a status marker.</param>
    /// <param name="status">Sample status: <c>ok</c>, <c>unsupported</c>, <c>error</c>, or <c>cancelled</c>.</param>
    /// <param name="normalized"><see langword="true"/> if the value was normalized by core count; otherwise <see langword="false"/>.</param>
    /// <param name="os">The operating system description.</param>
    /// <param name="message">Optional short error message for <c>status=error</c>.</param>
    /// <returns>A tag dictionary suitable for use with the multi-gauge samples.</returns>
    private static Dictionary<string, string> Tags(string window, string status, bool normalized, string os, string? message = null)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["window"] = window,
            ["status"] = status,
            ["normalized"] = normalized ? "true" : "false",
            ["os"] = os
        };

        if (!string.IsNullOrEmpty(message))
        {
            tags["message"] = message;
        }

        return tags;
    }

    /// <summary>
    /// Truncates error messages to a safe maximum length suitable for tag values.
    /// </summary>
    /// <param name="s">The input string.</param>
    /// <returns>The original string if short enough; otherwise a truncated prefix.</returns>
    private static string Short(string s)
    {
        return string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
    }

    /// <summary>
    /// Native interop to obtain the system load averages on macOS.
    /// </summary>
    /// <param name="loadavg">The array to receive load averages.</param>
    /// <param name="nelem">The number of elements to write (up to the length of <paramref name="loadavg"/>).</param>
    /// <returns>The number of elements written on success; otherwise a negative value.</returns>
    [LibraryImport("/usr/lib/libSystem.dylib", EntryPoint = "getloadavg")]
    private static partial int getloadavg([Out] double[] loadavg, int nelem);

    // ---- Explicit IMetricCollector helper methods (builder shortcuts) ----

    /// <summary>
    /// Creates a summary metric builder with the specified quantiles and optional tags, then builds the metric.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="quantiles">The set of quantiles to compute. Defaults to <c>{ 0.5, 0.9, 0.99 }</c> when <see langword="null"/>.</param>
    /// <param name="tags">Optional key/value tag pairs to attach to the metric.</param>
    /// <param name="resetOnGet">Ignored by this implementation; provided for interface completeness.</param>
    /// <returns>An initialized <see cref="ISummaryMetric"/>.</returns>
    ISummaryMetric IMetricCollector.CreateSummary(string id, string name, IEnumerable<double> quantiles, IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
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
    /// Creates a histogram metric builder with the specified upper bounds and optional tags, then builds the metric.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="bucketUpperBounds">The ascending list of inclusive bucket upper bounds.</param>
    /// <param name="tags">Optional key/value tag pairs to attach to the metric.</param>
    /// <returns>An initialized <see cref="IBucketHistogramMetric"/>.</returns>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
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
