// <copyright file="CpuThermalAndFanCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

#if WINDOWS
using System.Management;
#endif

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Collects CPU temperature (°C) and fan speed (RPM) as multi-gauge metrics across supported platforms.
/// </summary>
/// <remarks>
/// <para>
/// <b>Goals</b><br/>
/// • Cross-platform, best-effort collection with zero external dependencies.<br/>
/// • Resilient to partial failures (per-file / per-sensor guarded as shown below):<br/>
/// <code>
/// try
/// {
///     // collect sensor data
/// }
/// catch (Exception ex)
/// {
///     // handle errors
/// }
/// </code><br/>
/// • Bounded tag cardinality on errors (fixed <c>status</c>, <c>error</c>, <c>reason</c> tags).
/// </para>
/// <para>
/// <b>Platform behavior</b><br/>
/// • <b>Linux</b>: Reads vendor-specific hwmon sensors under <c>/sys/class/hwmon</c> (preferred) and generic thermal zones under <c>/sys/class/thermal</c> (fallback/enrichment).<br/>
/// • <b>Windows</b>: Uses WMI queries <c>MSAcpi_ThermalZoneTemperature</c> (°C via deci-Kelvin conversion) and <c>Win32_Fan</c> (prefers <c>CurrentSpeed</c>, falls back to <c>DesiredSpeed</c>).<br/>
/// • <b>macOS</b>: Not supported (returns a metric with <c>status=unsupported</c>, <c>os=Apple</c>).
/// </para>
/// <para>
/// <b>Metric layout</b><br/>
/// The collector emits a primary multi-gauge metric for temperatures (<c>cpu.temp.celsius</c> / “CPU Temperature (°C)”), and adds a sibling
/// multi-gauge for fan speeds (<c>fan.rpm</c> / “Fan Speed (RPM)”) using <see cref="IMultiGauge.AddSibling(string, string, double, System.Collections.Generic.IReadOnlyDictionary{string, string}?)"/>.
/// </para>
/// <para>
/// <b>Tag schema</b><br/>
/// Success samples include a fixed set of tags (e.g., <c>hwmon</c>, <c>sensor</c>, <c>label</c> for temperatures; <c>device</c> or <c>fan</c> for RPM) and <c>status=ok</c>.
/// Error and edge cases are encoded as metric points with <c>status</c> in { <c>error</c>, <c>empty</c>, <c>unsupported</c>, <c>cancelled</c> }. For <c>status=error</c>,
/// additional tags include <c>error</c> (exception type) and a truncated <c>reason</c>.
/// </para>
/// <para>
/// <b>Thread-safety</b><br/>
/// The collector is stateless between invocations and thread-safe provided the supplied <see cref="IMetricFactory"/> implementation is thread-safe.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Registration (typical):
/// services.AddNetMetric(); // Your extension that wires IMetricFactory/IMetricCollector integrations
/// services.AddSingleton<IMetricCollector, CpuThermalAndFanCollector>();
///
/// // Manual usage:
/// var collector = new CpuThermalAndFanCollector(metricFactory);
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
/// var metric = await collector.CollectAsync(cts.Token);
/// // 'metric' is a multi-gauge with temperature samples and a sibling gauge with fan RPMs.
/// ]]></code>
/// </example>
public sealed class CpuThermalAndFanCollector : IMetricCollector
{
    private const string TempMetricId = "cpu.temp.celsius";
    private const string TempMetricName = "CPU Temperature (°C)";
    private const string FanMetricId = "fan.rpm";
    private const string FanMetricName = "Fan Speed (RPM)";

    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuThermalAndFanCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create multi-gauge and related metric instances.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public CpuThermalAndFanCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects CPU temperature and fan speed metrics asynchronously, choosing a platform-specific strategy.
    /// </summary>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>
    /// A task that resolves to the multi-gauge metric containing temperature samples and a sibling gauge with fan RPMs.<br/>
    /// If a platform is unsupported, the returned metric contains a single sample with <c>status=unsupported</c> and an <c>os</c> tag.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Cancellation</b>: If <paramref name="ct"/> is cancelled, a point with <c>status=cancelled</c> is recorded and the metric is returned.
    /// </para>
    /// <para>
    /// <b>Partial failures</b>: Per-sensor exceptions are captured as points tagged with <c>status=error</c> and bounded <c>reason</c>; collection then continues for remaining sensors.
    /// </para>
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        if (OperatingSystem.IsLinux())
        {
            return Task.FromResult<IMetric?>(CollectLinux(ct));
        }
        else if (OperatingSystem.IsMacOS())
        {
            return Task.FromResult<IMetric?>(Unsupported("Apple"));
        }
        else if (OperatingSystem.IsWindows())
        {
            return Task.FromResult<IMetric?>(CollectWindowsBestEffort(ct));
        }

        return Task.FromResult<IMetric?>(Unsupported("Unknown"));
    }

    /// <summary>
    /// Creates and returns a multi-gauge metric with a single point indicating that the current OS is unsupported.
    /// </summary>
    /// <param name="osTag">A short OS identifier placed into the <c>os</c> tag (e.g., <c>Apple</c>, <c>Unknown</c>).</param>
    /// <returns>A multi-gauge with <c>status=unsupported</c> and <c>os</c> tags.</returns>
    private IMetric Unsupported(string osTag)
    {
        var mg = _factory.MultiGauge(TempMetricId, TempMetricName).WithResetOnGet(true).Build();

        AddUnsupported(mg, osTag);

        return mg;
    }

    /// <summary>
    /// Collects CPU thermal (°C) and fan RPM data on Linux systems.
    /// </summary>
    /// <param name="ct">A token used to observe cancellation.</param>
    /// <returns>
    /// A multi-gauge containing:
    /// <list type="bullet">
    /// <item>
    /// <description>Temperature samples from <c>/sys/class/hwmon</c> (preferred) and <c>/sys/class/thermal</c> (fallback).</description>
    /// </item>
    /// <item>
    /// <description>A sibling gauge for fan RPMs from <c>/sys/class/hwmon</c> fan inputs.</description>
    /// </item>
    /// </list>
    /// If no usable data is found, an <c>empty</c> point (<c>status=empty</c>, <c>os=Linux</c>) is emitted.
    /// </returns>
    /// <exception cref="OperationCanceledException">Propagated when <paramref name="ct"/> is cancelled during enumeration.</exception>
    private IMetric CollectLinux(CancellationToken ct)
    {
        var mg = _factory.MultiGauge(TempMetricId, TempMetricName).WithResetOnGet(true).Build();

        int added = 0;

        try
        {
            // /sys/class/hwmon: vendor-specific sensors (preferred)
            const string hwmonRoot = "/sys/class/hwmon";

            if (Directory.Exists(hwmonRoot))
            {
                foreach (var hw in Directory.EnumerateDirectories(hwmonRoot))
                {
                    ct.ThrowIfCancellationRequested();

                    string hwName = ReadTrimOrDefault(Path.Combine(hw, "name"), fallback: Path.GetFileName(hw));

                    // temps
                    foreach (var tempPath in SafeEnumerate(hw, "temp*_input"))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var baseName = Path.GetFileNameWithoutExtension(tempPath); // e.g., temp1_input
                            var labelPath = Path.Combine(hw, baseName.Replace("_input", "_label", StringComparison.Ordinal));
                            var label = ReadTrimOrDefault(labelPath, fallback: baseName);

                            if (long.TryParse(ReadTrimOrDefault(tempPath), out var milli))
                            {
                                mg.SetValue(milli / 1000.0, new Dictionary<string, string>
                                {
                                    ["hwmon"] = hwName,
                                    ["sensor"] = "temp",
                                    ["label"] = label,
                                    ["status"] = "ok"
                                });

                                added++;
                            }
                        }
                        catch (Exception ex)
                        {
                            AddError(mg, new Dictionary<string, string> { ["hwmon"] = hwName, ["sensor"] = "temp" }, ex);

                            throw;
                        }
                    }

                    // fans
                    foreach (var fanPath in SafeEnumerate(hw, "fan*_input"))
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            var baseName = Path.GetFileNameWithoutExtension(fanPath); // e.g., fan1_input
                            var labelPath = Path.Combine(hw, baseName.Replace("_input", "_label", StringComparison.Ordinal));
                            var label = ReadTrimOrDefault(labelPath, fallback: baseName);

                            if (long.TryParse(ReadTrimOrDefault(fanPath), out var rpm))
                            {
                                mg.AddSibling(FanMetricId, FanMetricName, rpm,
                                    new Dictionary<string, string>
                                    {
                                        ["hwmon"] = hwName,
                                        ["fan"] = label,
                                        ["status"] = "ok"
                                    });

                                added++;
                            }
                        }
                        catch (Exception ex)
                        {
                            AddErrorSibling(mg, new Dictionary<string, string> { ["hwmon"] = hwName, ["fan"] = "unknown" }, ex);

                            throw;
                        }
                    }
                }
            }

            // /sys/class/thermal: generic thermal zones (fallback/enrichment)
            const string tzRoot = "/sys/class/thermal";

            if (Directory.Exists(tzRoot))
            {
                foreach (var tz in Directory.EnumerateDirectories(tzRoot, "thermal_zone*"))
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        string type = ReadTrimOrDefault(Path.Combine(tz, "type"), "unknown");
                        string tpath = Path.Combine(tz, "temp");

                        if (File.Exists(tpath) && long.TryParse(ReadTrimOrDefault(tpath), out var milli))
                        {
                            mg.SetValue(milli / 1000.0, new Dictionary<string, string>
                            {
                                ["sensor"] = "thermal_zone",
                                ["label"] = type,
                                ["status"] = "ok"
                            });

                            added++;
                        }
                    }
                    catch (Exception ex)
                    {
                        AddError(mg, new Dictionary<string, string> { ["sensor"] = "thermal_zone" }, ex);

                        throw;
                    }
                }
            }

            if (added == 0)
            {
                AddEmpty(mg, "Linux");
            }
        }
        catch (OperationCanceledException)
        {
            AddCancelled(mg);
        }
        catch (Exception ex)
        {
            AddError(mg, tags: null, ex);

            throw;
        }

        return mg;
    }

    /// <summary>
    /// Collects CPU thermal and fan data on Windows using WMI, on a best-effort basis.
    /// </summary>
    /// <param name="ct">A token used to observe cancellation.</param>
    /// <returns>
    /// A multi-gauge whose primary series contains CPU temperatures (converted from deci-Kelvin), and a sibling series with fan RPMs.<br/>
    /// If no data can be obtained, an <c>empty</c> point (<c>status=empty</c>, <c>os=Windows</c>) is emitted.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Queries <c>MSAcpi_ThermalZoneTemperature</c> in <c>root\WMI</c> for temperatures and <c>Win32_Fan</c> in <c>root\CIMV2</c> for RPM. Not all hardware/firmware exposes these classes.
    /// </para>
    /// </remarks>
    private IMetric CollectWindowsBestEffort(CancellationToken ct)
    {
        var mg = _factory.MultiGauge(TempMetricId, TempMetricName).WithResetOnGet(true).Build();

#if WINDOWS
        try
        {
            int added = 0;

            // Temperature via ACPI (may not be present on many systems)
            try
            {
                ct.ThrowIfCancellationRequested();
                using var s = new ManagementObjectSearcher(
                    @"root\\WMI",
                    "SELECT CurrentTemperature, InstanceName FROM MSAcpi_ThermalZoneTemperature");

                foreach (ManagementObject mo in s.Get())
                {
                    ct.ThrowIfCancellationRequested();
                    var kelvin10 = (uint?)(mo["CurrentTemperature"]) ?? 0u;
                    double c = kelvin10 > 0 ? (kelvin10 / 10.0) - 273.15 : 0.0;

                    mg.SetValue(c, new Dictionary<string, string>
                    {
                        ["sensor"] = mo["InstanceName"]?.ToString() ?? "acpi",
                        ["status"] = "ok"
                    });
                    added++;
                }
            }
            catch (Exception ex)
            {
                AddError(mg, new Dictionary<string, string> { ["source"] = "MSAcpi_ThermalZoneTemperature" }, ex);
            }

            // Fan (best-effort). DesiredSpeed isn't always actual RPM; prefer CurrentSpeed when present.
            try
            {
                ct.ThrowIfCancellationRequested();
                using var fan = new ManagementObjectSearcher(
                    @"root\\CIMV2",
                    "SELECT Name, DesiredSpeed, CurrentSpeed FROM Win32_Fan");

                foreach (ManagementObject mo in fan.Get())
                {
                    ct.ThrowIfCancellationRequested();

                    double rpm = Convert.ToDouble((mo["CurrentSpeed"] ?? mo["DesiredSpeed"] ?? 0));
                    mg.AddSibling(FanMetricId, FanMetricName, rpm, new Dictionary<string, string>
                    {
                        ["device"] = mo["Name"]?.ToString() ?? "fan",
                        ["status"] = "ok"
                    });
                    added++;
                }
            }
            catch (Exception ex)
            {
                AddErrorSibling(mg, new Dictionary<string, string> { ["source"] = "Win32_Fan" }, ex);
            }

            if (added == 0)
                AddEmpty(mg, "Windows");
        }
        catch (OperationCanceledException)
        {
            AddCancelled(mg);
        }
        catch (Exception ex)
        {
            AddError(mg, tags: null, ex);
        }
#else
        AddUnsupported(mg, "not_built_with_WINDOWS_symbol");
#endif

        return mg;
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Reads and trims file contents, returning a fallback value if the file is missing, unreadable, or empty.
    /// </summary>
    /// <param name="path">The file path to read.</param>
    /// <param name="fallback">A value to return if the file cannot be read or yields no content.</param>
    /// <returns>The trimmed file content, or <paramref name="fallback"/> when unavailable.</returns>
    private static string ReadTrimOrDefault(string path, string? fallback = null)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback ?? string.Empty;
            }
            var s = File.ReadAllText(path).Trim();

            return string.IsNullOrEmpty(s) ? (fallback ?? string.Empty) : s;
        }
        catch
        {
            return fallback ?? string.Empty;

            throw;
        }
    }

    /// <summary>
    /// Enumerates files in a directory using a search pattern, suppressing IO errors.
    /// </summary>
    /// <param name="dir">The directory to search.</param>
    /// <param name="pattern">A file name pattern (e.g., <c>temp*_input</c>).</param>
    /// <returns>An enumerable of matching file paths, or an empty sequence if the directory is inaccessible.</returns>
    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern) : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();

            throw;
        }
    }

    // ---------- Error Handling Methods ----------

    /// <summary>
    /// Records an error sample on the primary multi-gauge with bounded error metadata.
    /// </summary>
    /// <param name="mg">The multi-gauge to write to.</param>
    /// <param name="tags">Optional tags to include (will be augmented with <c>status</c>, <c>error</c>, <c>reason</c>).</param>
    /// <param name="ex">The exception that occurred.</param>
    private static void AddError(IMultiGauge mg, Dictionary<string, string>? tags, Exception ex)
    {
        if (mg == null)
        {
            ArgumentNullException.ThrowIfNull(mg);
        }

        if (ex == null)
        {
            ArgumentNullException.ThrowIfNull(ex);
        }

        var t = tags ?? new Dictionary<string, string>();

        t["status"] = "error";
        t["error"] = ex.GetType().Name;

        var msg = ex.Message ?? string.Empty;

        t["reason"] = msg.Length <= 160 ? msg : msg[..160];

        mg.SetValue(0, t);
    }

    /// <summary>
    /// Records an error sample on the fan RPM sibling series with bounded error metadata.
    /// </summary>
    /// <param name="mg">The multi-gauge to write to.</param>
    /// <param name="tags">Optional tags to include (will be augmented with <c>status</c>, <c>error</c>, <c>reason</c>).</param>
    /// <param name="ex">The exception that occurred.</param>
    private static void AddErrorSibling(IMultiGauge mg, Dictionary<string, string>? tags, Exception ex)
    {
        if (mg == null)
        {
            ArgumentNullException.ThrowIfNull(mg);
        }

        if (ex == null)
        {
            ArgumentNullException.ThrowIfNull(ex);
        }

        var t = tags ?? new Dictionary<string, string>();

        t["status"] = "error";
        t["error"] = ex.GetType().Name;

        var msg = (ex.Message ?? string.Empty);

        t["reason"] = msg.Length <= 160 ? msg : msg[..160];

        mg.AddSibling(FanMetricId, FanMetricName, 0, t);
    }

    /// <summary>
    /// Records an <c>empty</c> sample when no readings are available for the OS.
    /// </summary>
    /// <param name="mg">The multi-gauge to write to.</param>
    /// <param name="os">OS identifier placed into the <c>os</c> tag.</param>
    private static void AddEmpty(IMultiGauge mg, string os)
    {
        if (mg == null)
        {
            ArgumentNullException.ThrowIfNull(mg);
        }

        mg.SetValue(0, new Dictionary<string, string> { ["status"] = "empty", ["os"] = os });
    }

    /// <summary>
    /// Records an <c>unsupported</c> sample for the current build/runtime combination.
    /// </summary>
    /// <param name="mg">The multi-gauge to write to.</param>
    /// <param name="os">OS identifier placed into the <c>os</c> tag.</param>
    private static void AddUnsupported(IMultiGauge mg, string os)
    {
        if (mg == null)
        {
            ArgumentNullException.ThrowIfNull(mg);
        }

        mg.SetValue(0, new Dictionary<string, string> { ["status"] = "unsupported", ["os"] = os });
    }

    /// <summary>
    /// Records a <c>cancelled</c> sample when collection is aborted via cancellation.
    /// </summary>
    /// <param name="mg">The multi-gauge to write to.</param>
    private static void AddCancelled(IMultiGauge mg)
    {
        if (mg == null)
        {
            ArgumentNullException.ThrowIfNull(mg);
        }

        mg.SetValue(0, new Dictionary<string, string> { ["status"] = "cancelled" });
    }

    // ---------- Explicit IMetricCollector helper methods ----------

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
