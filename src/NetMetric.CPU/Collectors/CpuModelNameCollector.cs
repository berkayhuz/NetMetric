// <copyright file="CpuModelNameCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Collects a single gauge metric that indicates the presence (1/0) and tags the CPU model information.
/// </summary>
/// <remarks>
/// <para>
/// The collector emits a gauge metric with the following contract:
/// </para>
/// <list type="bullet">
///   <item><description><c>id</c>: <c>"cpu.model.info"</c></description></item>
///   <item><description><c>name</c>: <c>"CPU Model Name"</c></description></item>
///   <item>
///     <description>Value:
///       <list type="bullet">
///         <item><description><c>1</c> on success (model identified)</description></item>
///         <item><description><c>0</c> on failure or cancellation</description></item>
///       </list>
///     </description>
///   </item>
///   <item>
///     <description>Tags:
///       <list type="bullet">
///         <item><description><c>model</c>: CPU model/brand string if available; otherwise a platform-specific "Unknown CPU (...)" placeholder</description></item>
///         <item><description><c>status</c>: <c>ok</c> | <c>error</c> | <c>cancelled</c></description></item>
///         <item><description><c>error</c>: optional, exception type name when <c>status = error</c></description></item>
///         <item><description><c>reason</c>: optional, truncated (≤160 chars) exception message when <c>status = error</c></description></item>
///       </list>
///     </description>
///   </item>
/// </list>
///
/// <para>Platform behavior:</para>
/// <list type="bullet">
///   <item><description><b>Windows</b>: Reads <c>HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0\ProcessorNameString</c>.</description></item>
///   <item><description><b>Linux</b>: Streams <c>/proc/cpuinfo</c>, preferring <c>model name</c>, falling back to <c>Hardware</c> (common on ARM).</description></item>
///   <item><description><b>macOS</b>: Uses <c>machdep.cpu.brand_string</c>, then <c>hw.model</c> via a <c>sysctl</c> helper.</description></item>
///   <item><description>Other OSes: Emits an <c>Unknown CPU</c> placeholder.</description></item>
/// </list>
///
/// <para>
/// Design goals:
/// </para>
/// <list type="bullet">
///   <item><description>Zero external runtime dependencies (only standard library and lightweight platform queries).</description></item>
///   <item><description>Low cardinality tags (no free-form stack traces; reason truncated to 160 chars).</description></item>
///   <item><description>Safe fallbacks and defensive coding per platform.</description></item>
/// </list>
///
/// <para><b>Thread safety:</b> This collector is stateless aside from the injected <see cref="IMetricFactory"/> and is safe to use concurrently as long as the provided factory is thread-safe.</para>
///
/// <example>
/// <code>
/// // Registration (example):
/// IMetricFactory factory = ...;
/// var collector = new CpuModelNameCollector(factory);
///
/// // Collection:
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
/// var metric = await collector.CollectAsync(cts.Token);
///
/// // The resulting metric has:
/// // - id: "cpu.model.info"
/// // - name: "CPU Model Name"
/// // - value: 1 or 0
/// // - tags: model, status, and optionally error/reason
/// </code>
/// </example>
/// </remarks>
public sealed class CpuModelNameCollector : IMetricCollector
{
    private const string Id = "cpu.model.info";
    private const string Name = "CPU Model Name";

    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuModelNameCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown when the <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The provided <see cref="IMetricFactory"/> is used to build the gauge metric instance and to apply consistent tagging.
    /// </remarks>
    /// <example>
    /// <code>
    /// IMetricFactory factory = ...;
    /// var collector = new CpuModelNameCollector(factory);
    /// </code>
    /// </example>
    public CpuModelNameCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects the CPU model information and emits a gauge metric.
    /// </summary>
    /// <param name="ct">The cancellation token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result is an <see cref="IMetric"/> instance with id <c>cpu.model.info</c> and the tags described in the class remarks.
    /// </returns>
    /// <exception cref="OperationCanceledException">Propagated if cancellation is requested before producing the metric; the method still returns a metric with <c>status="cancelled"</c> when handled internally.</exception>
    /// <remarks>
    /// <para>
    /// This method never throws for normal error paths; instead, it sets the gauge value to <c>0</c> and annotates the metric with
    /// <c>status="error"</c>, an <c>error</c> tag (exception type), and a truncated <c>reason</c>.
    /// </para>
    /// <para>
    /// On success, the gauge value is <c>1</c> and <c>status="ok"</c>. The <c>model</c> tag is always present with the resolved model string or a platform-specific <c>Unknown CPU (...)</c> placeholder.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var metric = await collector.CollectAsync();
    /// // Inspect tags:
    /// // metric.Tags["model"], metric.Tags["status"], optionally metric.Tags["error"], metric.Tags["reason"]
    /// </code>
    /// </example>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var model = ReadModelName();
            var tags = new Dictionary<string, string>
            {
                ["model"] = model,
                ["status"] = "ok"
            };

            return Task.FromResult<IMetric?>(BuildGauge(1, tags));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<IMetric?>(BuildGauge(0, new Dictionary<string, string>
            {
                ["status"] = "cancelled"
            }));
        }
        catch (Exception ex)
        {
            // Keep tag cardinality bounded: type + short reason (optional)
            return Task.FromResult<IMetric?>(BuildGauge(0, new Dictionary<string, string>
            {
                ["status"] = "error",
                ["error"] = ex.GetType().Name,
                ["reason"] = Short(ex.Message)
            }));

            throw;
        }
    }

    /// <summary>
    /// Builds a gauge metric instance with the provided value and tags.
    /// </summary>
    /// <param name="value">The gauge value to assign (e.g., <c>1</c> for success, <c>0</c> for failure).</param>
    /// <param name="tags">A non-null collection of key/value tags to annotate the metric.</param>
    /// <returns>The constructed <see cref="IMetric"/> gauge instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="tags"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Uses <see cref="IMetricFactory.Gauge(string, string)"/> to build a gauge with consistent identification and applies the supplied tags.
    /// </remarks>
    private IMetric BuildGauge(double value, IReadOnlyDictionary<string, string> tags)
    {
        if (tags is null)
        {
            throw new ArgumentNullException(nameof(tags), "Tags cannot be null.");
        }

        var gb = _factory.Gauge(Id, Name);

        foreach (var kv in tags)
        {
            gb.WithTag(kv.Key, kv.Value);
        }

        var g = gb.Build();

        g.SetValue(value);

        return g;
    }

    /// <summary>
    /// Resolves the CPU model name using platform-specific mechanisms with safe fallbacks.
    /// </summary>
    /// <returns>
    /// A model/brand string when available; otherwise a platform-specific <c>Unknown CPU (...)</c> placeholder or <c>Unknown CPU</c>.
    /// </returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description>Windows: reads <c>ProcessorNameString</c> from the registry.</description></item>
    ///   <item><description>Linux: scans <c>/proc/cpuinfo</c> for <c>model name</c> then <c>Hardware</c>.</description></item>
    ///   <item><description>macOS: tries <c>machdep.cpu.brand_string</c>, then <c>hw.model</c>.</description></item>
    /// </list>
    /// </remarks>
    private static string ReadModelName()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var rk = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");

                var name = rk?.GetValue("ProcessorNameString")?.ToString();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name!;
                }
            }
            catch
            {
                throw;
            }

            return "Unknown CPU (Windows)";
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                // Stream lines to avoid loading the whole file
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                {
                    if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = line.Split(':', 2)[^1].Trim();

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name!;
                        }
                    }

                    if (line.StartsWith("Hardware", StringComparison.OrdinalIgnoreCase))
                    {
                        var hw = line.Split(':', 2)[^1].Trim();

                        if (!string.IsNullOrWhiteSpace(hw))
                        {
                            return hw!;
                        }
                    }
                }
            }
            catch
            {
                throw;
            }

            return "Unknown CPU (Linux)";
        }

        if (OperatingSystem.IsMacOS())
        {
            if (MacSysctl.TryReadString("machdep.cpu.brand_string", out var brand) && !string.IsNullOrWhiteSpace(brand))
            {
                return brand!;
            }
            else if (MacSysctl.TryReadString("hw.model", out var model) && !string.IsNullOrWhiteSpace(model))
            {
                return model!;
            }

            return "Unknown CPU (Apple)";
        }

        return "Unknown CPU";
    }

    /// <summary>
    /// Returns a shortened version of the provided string, ensuring the length does not exceed 160 characters.
    /// </summary>
    /// <param name="s">The input string to truncate.</param>
    /// <returns>
    /// <para>
    /// <see cref="string.Empty"/> if the input is <see langword="null"/> or empty; otherwise the original string if ≤160 characters,
    /// or a substring of length 160.
    /// </para>
    /// </returns>
    private static string Short(string s) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);

    // ---- Explicit IMetricCollector helper methods ----

    /// <summary>
    /// Creates a summary metric with the specified quantiles.
    /// </summary>
    /// <param name="id">The unique identifier of the summary metric.</param>
    /// <param name="name">The human-readable name of the summary metric.</param>
    /// <param name="quantiles">The quantiles to compute (defaults to 0.5, 0.9, 0.99 when <see langword="null"/>).</param>
    /// <param name="tags">Optional key/value tags to annotate the metric.</param>
    /// <param name="resetOnGet">Indicates whether the summary is reset on retrieval (honored by the underlying factory if supported).</param>
    /// <returns>A built <see cref="ISummaryMetric"/> instance.</returns>
    /// <remarks>
    /// This is a convenience method required by <see cref="IMetricCollector"/> that delegates to <see cref="IMetricFactory"/>.
    /// </remarks>
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
    /// Creates a bucketed histogram metric with the specified upper bounds.
    /// </summary>
    /// <param name="id">The unique identifier of the histogram metric.</param>
    /// <param name="name">The human-readable name of the histogram metric.</param>
    /// <param name="bucketUpperBounds">The inclusive upper bounds for each bucket (in ascending order).</param>
    /// <param name="tags">Optional key/value tags to annotate the metric.</param>
    /// <returns>A built <see cref="IBucketHistogramMetric"/> instance.</returns>
    /// <remarks>
    /// This is a convenience method required by <see cref="IMetricCollector"/> that delegates to <see cref="IMetricFactory"/>.
    /// </remarks>
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
