// <copyright file="CpuFrequencyCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

#if WINDOWS
using System.Management; // WMI provider (Win32_Processor)
#endif

using System.Management;

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Collects CPU frequency metrics across supported platforms with normalized tags and stable metric IDs.
/// </summary>
/// <remarks>
/// <para>
/// Platform behavior:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><b>Linux</b>: Emits per-core <c>current</c> and best-effort <c>max</c> frequencies in MHz using <c>/sys/devices/system/cpu/cpuX/cpufreq</c> sources.  
///     Prefer <c>scaling_cur_freq</c>, falling back to <c>cpuinfo_cur_freq</c>; reads <c>cpuinfo_max_freq</c> for maximums when available.</description>
///   </item>
///   <item>
///     <description><b>macOS</b>: Emits a single <c>max</c> (nominal) frequency in MHz using <c>sysctl</c> key <c>hw.cpufrequency</c>.</description>
///   </item>
///   <item>
///     <description><b>Windows</b>: Emits per-socket <c>current</c> and <c>max</c> frequencies in MHz via WMI class <c>Win32_Processor</c> (<c>CurrentClockSpeed</c>, <c>MaxClockSpeed</c>).</description>
///   </item>
/// </list>
/// <para>
/// Metric IDs and tags:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><c>cpu.freq.current.mhz</c>: Gauge/MultiGauge. Tags include <c>os</c>=<c>linux|windows|unknown</c>, and either <c>core</c> (Linux) or <c>socket</c> (Windows); <c>type</c>=<c>current</c>; <c>status</c>=<c>ok|error|unsupported|empty</c>. On Windows, includes <c>provider</c>=<c>wmi</c>. On error, adds <c>message</c> with a truncated reason.</description>
///   </item>
///   <item>
///     <description><c>cpu.freq.max.mhz</c>: Gauge. Tags include <c>os</c>=<c>macos</c>, <c>type</c>=<c>max</c>, <c>status</c>=<c>ok|error|unsupported</c>. On error, adds <c>reason</c>.</description>
///   </item>
/// </list>
/// <para>
/// Error handling:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>Platform collectors attempt best-effort population. When a per-item read fails, a zero value with <c>status</c>=<c>error</c> is emitted alongside a truncated message; the exception is rethrown on Linux to aid diagnostics after emitting the error sample. macOS and Windows emit zero with error status and do not throw further.</description>
///   </item>
///   <item>
///     <description>Unsupported platforms emit a single zero sample with <c>status</c>=<c>unsupported</c>.</description>
///   </item>
/// </list>
/// <para><b>Thread safety</b>: Instances are thread-safe for <see cref="CollectAsync(System.Threading.CancellationToken)"/>; all state used during collection is local to the call or within thread-safe factory builders.</para>
/// </remarks>
/// <example>
/// The following example creates a collector and publishes CPU frequency metrics:
/// <code language="csharp"><![CDATA[
/// IMetricFactory factory = /* obtain from DI */;
/// var collector = new CpuFrequencyCollector(factory);
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
///
/// // Collect once (e.g., from a scheduler)
/// IMetric? metric = await collector.CollectAsync(cts.Token);
///
/// if (metric is not null)
/// {
///     // Export via your metrics pipeline
///     Export(metric);
/// }
/// ]]></code>
/// </example>
public sealed partial class CpuFrequencyCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuFrequencyCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to construct gauges and multi-gauges.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public CpuFrequencyCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects CPU frequency metrics for the current operating system.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// A task producing the constructed metric for the platform, or <see langword="null"/> if no samples are available.
    /// On Linux and Windows, this is a MultiGauge for <c>cpu.freq.current.mhz</c>; on macOS, a Gauge for <c>cpu.freq.max.mhz</c>.
    /// </returns>
    /// <remarks>
    /// The method emits zero-valued samples with descriptive tags in error or unsupported scenarios to keep series discoverable
    /// and simplify downstream alerting. On Linux, certain per-core read failures rethrow after emitting an error sample
    /// to help surface filesystem or permission issues.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is signaled before completion.</exception>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Task.FromResult<IMetric?>(CollectLinux());
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Task.FromResult<IMetric?>(CollectMac());
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult<IMetric?>(CollectWindows());
        }

        // Unknown platform → emit an "unsupported" gauge
        var g = _factory
            .Gauge("cpu.freq.current.mhz", "CPU Frequency (MHz)")
            .WithTag("os", "unknown")
            .WithTag("status", "unsupported")
            .Build();

        g.SetValue(0);
        return Task.FromResult<IMetric?>(g);
    }

    // =======================================================================
    // Linux
    // =======================================================================

    /// <summary>
    /// Builds per-core frequency metrics on Linux using <c>/sys/devices/system/cpu</c>.
    /// </summary>
    /// <returns>
    /// A MultiGauge with one or more samples for <c>cpu.freq.current.mhz</c> and optional <c>max</c> samples per core.
    /// Each sample includes tags: <c>os</c>=<c>linux</c>, <c>core</c>, <c>type</c>=<c>current|max</c>, and <c>status</c>.
    /// </returns>
    /// <remarks>
    /// Uses <c>scaling_cur_freq</c> if present (kHz), otherwise <c>cpuinfo_cur_freq</c>. Max frequency is taken from <c>cpuinfo_max_freq</c> when available.
    /// On per-core read exceptions, an error sample is emitted and the exception is rethrown to aid diagnostics.
    /// When no samples are discovered, emits a single <c>unsupported</c> sample to keep the series visible.
    /// </remarks>
    private IMetric CollectLinux()
    {
        var mg = _factory.MultiGauge("cpu.freq.current.mhz", "CPU Frequency (MHz)")
                         .WithResetOnGet(true)
                         .Build();

        int added = 0;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/sys/devices/system/cpu", "cpu*"))
            {
                var name = Path.GetFileName(dir);

                if (!name.StartsWith("cpu", StringComparison.Ordinal) ||
                    !int.TryParse(name.AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var core))
                {
                    continue;
                }

                // Prefer scaling_cur_freq; fallback to cpuinfo_cur_freq
                var curPath = Path.Combine(dir, "cpufreq", "scaling_cur_freq");
                if (!File.Exists(curPath))
                {
                    curPath = Path.Combine(dir, "cpufreq", "cpuinfo_cur_freq");
                }

                if (File.Exists(curPath))
                {
                    try
                    {
                        var txt = File.ReadAllText(curPath).Trim();

                        if (long.TryParse(txt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kHz))
                        {
                            mg.SetValue(kHz / 1000.0, new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["os"] = "linux",
                                ["core"] = core.ToString(CultureInfo.InvariantCulture),
                                ["type"] = "current",
                                ["status"] = "ok"
                            });

                            added++;
                        }
                    }
                    catch (Exception ex)
                    {
                        mg.SetValue(0, new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["os"] = "linux",
                            ["core"] = core.ToString(CultureInfo.InvariantCulture),
                            ["type"] = "current",
                            ["status"] = "error",
                            ["message"] = Short(ex.Message)
                        });

                        throw;
                    }
                }

                // Best-effort: publish per-core max if available
                var maxPath = Path.Combine(dir, "cpufreq", "cpuinfo_max_freq");

                if (File.Exists(maxPath))
                {
                    try
                    {
                        var txt = File.ReadAllText(maxPath).Trim();

                        if (long.TryParse(txt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kHzMax))
                        {
                            mg.SetValue(kHzMax / 1000.0, new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["os"] = "linux",
                                ["core"] = core.ToString(CultureInfo.InvariantCulture),
                                ["type"] = "max",
                                ["status"] = "ok"
                            });

                            added++;
                        }
                    }
                    catch (Exception ex)
                    {
                        mg.SetValue(0, new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["os"] = "linux",
                            ["core"] = core.ToString(CultureInfo.InvariantCulture),
                            ["type"] = "max",
                            ["status"] = "error",
                            ["message"] = Short(ex.Message)
                        });

                        throw;
                    }
                }
            }

            if (added == 0)
            {
                mg.SetValue(0, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["os"] = "linux",
                    ["status"] = "unsupported"
                });
            }
        }
        catch (Exception ex)
        {
            mg.SetValue(0, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["os"] = "linux",
                ["status"] = "error",
                ["message"] = Short(ex.Message)
            });

            throw;
        }

        return mg;
    }

    // =======================================================================
    // macOS
    // =======================================================================
#pragma warning disable CA1031
    /// <summary>
    /// Builds a gauge for the nominal (max) CPU frequency on macOS via <c>sysctl</c>.
    /// </summary>
    /// <returns>
    /// A Gauge with metric ID <c>cpu.freq.max.mhz</c> tagged with <c>os</c>=<c>macos</c>, <c>type</c>=<c>max</c>, and <c>status</c>.
    /// Returns <c>status</c>=<c>unsupported</c> when <c>hw.cpufrequency</c> is not available.
    /// </returns>
    /// <remarks>
    /// Converts <c>hw.cpufrequency</c> (Hz) to MHz. On exception, emits a zero sample with <c>status</c>=<c>error</c> and a short reason.
    /// </remarks>
    private IMetric CollectMac()
    {
        const string id = "cpu.freq.max.mhz";
        const string name = "CPU Nominal Frequency (MHz)";

        try
        {
            if (TrySysctlUInt64("hw.cpufrequency", out var hz))
            {
                var g = _factory.Gauge(id, name)
                                .WithTag("os", "macos")
                                .WithTag("type", "max")
                                .WithTag("status", "ok")
                                .Build();
                g.SetValue(hz / 1_000_000.0); // Hz → MHz

                return g;
            }

            var g0 = _factory.Gauge(id, name)
                             .WithTag("os", "macos")
                             .WithTag("type", "max")
                             .WithTag("status", "unsupported")
                             .Build();
            g0.SetValue(0);

            return g0;
        }
        catch (Exception ex)
        {
            var g = _factory.Gauge(id, name)
                            .WithTag("os", "macos")
                            .WithTag("type", "max")
                            .WithTag("status", "error")
                            .WithTag("reason", Short(ex.Message))
                            .Build();

            g.SetValue(0);
            return g;
        }
    }
#pragma warning restore CA1031

    // =======================================================================
    // Windows
    // =======================================================================

    /// <summary>
    /// Builds per-socket frequency metrics on Windows using WMI (<c>Win32_Processor</c>).
    /// </summary>
    /// <returns>
    /// A MultiGauge for <c>cpu.freq.current.mhz</c> with two samples per socket (<c>current</c> and <c>max</c>).  
    /// Includes tags: <c>os</c>=<c>windows</c>, <c>socket</c> from <c>DeviceID</c>, <c>type</c>, <c>provider</c>=<c>wmi</c>, and <c>status</c>.
    /// Emits a single <c>empty</c> sample if no rows are returned.
    /// </returns>
    /// <remarks>
    /// WMI values are already in MHz. On <see cref="ManagementException"/> or general exceptions, emits a zero sample with <c>status</c>=<c>error</c> and a short message.
    /// </remarks>
    private IMetric CollectWindows()
    {
        var mg = _factory.MultiGauge("cpu.freq.current.mhz", "CPU Frequency (MHz)")
                         .WithResetOnGet(true)
                         .Build();

#if WINDOWS
        try
        {
            int added = 0;

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor");

            foreach (var mo in searcher.Get().Cast<ManagementObject>())
            {
                var socket = (mo["DeviceID"] as string) ?? "CPU0";
                var cur    = (uint?)mo["CurrentClockSpeed"] ?? 0;
                var max    = (uint?)mo["MaxClockSpeed"] ?? 0;

                mg.SetValue(cur, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["os"]      = "windows",
                    ["socket"]  = socket,
                    ["type"]    = "current",
                    ["provider"]= "wmi",
                    ["status"]  = "ok"
                });

                mg.SetValue(max, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["os"]      = "windows",
                    ["socket"]  = socket,
                    ["type"]    = "max",
                    ["provider"]= "wmi",
                    ["status"]  = "ok"
                });

                added += 2;
            }

            if (added == 0)
            {
                mg.SetValue(0, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["os"]     = "windows",
                    ["status"] = "empty"
                });
            }
        }
        catch (ManagementException mex)
        {
            mg.SetValue(0, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["os"]      = "windows",
                ["provider"]= "wmi",
                ["status"]  = "error",
                ["message"] = Short(mex.Message)
            });
        }
        catch (Exception ex)
        {
            mg.SetValue(0, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["os"]      = "windows",
                ["provider"]= "wmi",
                ["status"]  = "error",
                ["message"] = Short(ex.Message)
            });
        }
#else
        mg.SetValue(0, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["os"] = "windows",
            ["status"] = "unsupported",
            ["message"] = "not_built_with_WINDOWS_symbol"
        });
#endif

        return mg;
    }

    // =======================================================================
    // Utilities
    // =======================================================================

    /// <summary>
    /// Attempts to read a 64-bit unsigned integer from <c>sysctl</c> by key, with probe and retry for <c>EINTR</c> and <c>ENOMEM</c>.
    /// </summary>
    /// <param name="key">The <c>sysctl</c> key to read.</param>
    /// <param name="value">When this method returns, contains the parsed value if successful; otherwise, zero.</param>
    /// <returns><see langword="true"/> if the value was read successfully; otherwise, <see langword="false"/>.</returns>
    private static bool TrySysctlUInt64(string key, out ulong value)
    {
        value = 0;

        nuint size = 0;

        const int EINTR = 4;
        const int ENOMEM = 12;

        // Probe
        if (sysctlbyname(key, 0, ref size, 0, 0) != 0 || size != (nuint)sizeof(ulong))
        {
            return false;
        }

        var buf = Marshal.AllocHGlobal((nint)size);

        try
        {
            const int maxAttempts = 3;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var rc = sysctlbyname(key, buf, ref size, 0, 0);

                if (rc == 0 && size == (nuint)sizeof(ulong))
                {
                    long signed = Marshal.ReadInt64(buf);
                    value = unchecked((ulong)signed);
                    return true;
                }

                var errno = Marshal.GetLastPInvokeError();
                if (errno == EINTR)
                {
                    continue;
                }

                if (errno == ENOMEM)
                {
                    if (size == 0)
                    {
                        return false;
                    }

                    Marshal.FreeHGlobal(buf);
                    buf = Marshal.AllocHGlobal((nint)size);
                    continue;
                }

                return false;
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>
    /// P/Invoke to <c>sysctlbyname</c> from <c>libSystem.dylib</c>. Exposed as a private helper for <see cref="CollectMac"/>.
    /// </summary>
    /// <param name="name">The <c>sysctl</c> key.</param>
    /// <param name="oldp">Pointer to output buffer, or <c>NULL</c> to query required size.</param>
    /// <param name="oldlenp">In/out length of <paramref name="oldp"/>.</param>
    /// <param name="newp">Optional input buffer (unused here).</param>
    /// <param name="newlen">Length of <paramref name="newp"/>.</param>
    /// <returns><c>0</c> on success; non-zero on failure with <see cref="Marshal.GetLastPInvokeError"/> providing the errno.</returns>
    [LibraryImport("/usr/lib/libSystem.dylib", EntryPoint = "sysctlbyname", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int sysctlbyname(string name, nint oldp, ref nuint oldlenp, nint newp, nuint newlen);

    /// <summary>
    /// Produces a short, safe diagnostic message for metric tags.
    /// </summary>
    /// <param name="s">The original message.</param>
    /// <returns>The original message truncated to 160 characters, or empty if <paramref name="s"/> is null or empty.</returns>
    private static string Short(string s)
    {
        return string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
    }

    // ---- Explicit IMetricCollector helper methods ----

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
