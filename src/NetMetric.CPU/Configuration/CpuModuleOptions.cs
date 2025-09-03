// <copyright file="CpuModuleOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Configuration;

/// <summary>
/// Options controlling which CPU collectors are enabled for the NetMetric CPU module.
/// Defaults favor lightweight, high‑value metrics; heavier collectors are opt‑in.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class CpuModuleOptions
{
    // =============================
    // Presets for quick configuration
    // =============================

    /// <summary>
    /// Creates a <see cref="CpuModuleOptions"/> instance based on a preset configuration.
    /// </summary>
    /// <param name="preset">The preset to use for configuring the options.</param>
    /// <returns>A new instance of <see cref="CpuModuleOptions"/> configured based on the preset.</returns>
    public static CpuModuleOptions FromPreset(CpuModulePreset preset) => preset switch
    {
        CpuModulePreset.Light => new(
            enableCore: true,
            enablePerCore: false,
            enableLoadAverage: true,
            enableFrequency: false,
            enableThermalAndFan: false,
            enableAllProcesses: false,
            enableThreads: false),

        CpuModulePreset.Default => new CpuModuleOptions(),

        CpuModulePreset.Verbose => new(
            enableCore: true,
            enablePerCore: true,
            enableLoadAverage: true,
            enableFrequency: true,
            enableThermalAndFan: true,
            enableAllProcesses: true,
            enableThreads: true),

        _ => new CpuModuleOptions()
    };

    // =============================
    // Public options (init-only)
    // =============================

    /// <summary>
    /// Enables core metrics (always recommended): process/system CPU, core count, model, features.
    /// </summary>
    public bool EnableCore { get; init; }

    /// <summary>
    /// Enables per-core CPU usage deltas (lightweight).
    /// </summary>
    public bool EnablePerCore { get; init; }

    /// <summary>
    /// Enables normalized system load average (Linux/macOS).
    /// </summary>
    public bool EnableLoadAverage { get; init; }

    /// <summary>
    /// Enables CPU frequency read (Linux sysfs / macOS nominal / Windows WMI — relatively heavy).
    /// </summary>
    public bool EnableFrequency { get; init; }

    /// <summary>
    /// Enables thermal sensors and fan speeds (Linux good, Windows WMI best-effort, macOS N/A) — can be heavy.
    /// </summary>
    public bool EnableThermalAndFan { get; init; }

    /// <summary>
    /// Enables CPU% for all processes (very heavy; off by default).
    /// </summary>
    public bool EnableAllProcesses { get; init; }

    /// <summary>
    /// Enables CPU% for current process threads (heavy/noisy; off by default).
    /// </summary>
    public bool EnableThreads { get; init; }

    // =============================
    // Derived convenience flags
    // =============================

    /// <summary>
    /// Indicates if any heavy collectors are enabled (useful for scheduling/interval decisions).
    /// </summary>
    public bool AnyHeavy =>
        EnableFrequency || EnableThermalAndFan || EnableAllProcesses || EnableThreads;

    /// <summary>
    /// Indicates if at least one collector of any kind is enabled.
    /// </summary>
    public bool AnyEnabled =>
        EnableCore || EnablePerCore || EnableLoadAverage || AnyHeavy;

    // =============================
    // Fluent helpers (non-mutating)
    // These return a new instance with the requested change.
    // =============================

    /// <summary>
    /// Returns a new instance of <see cref="CpuModuleOptions"/> with the specified Core setting.
    /// </summary>
    public CpuModuleOptions WithCore(bool on = true) => Clone(enableCore: on);

    /// <summary>
    /// Returns a new instance of <see cref="CpuModuleOptions"/> with the specified PerCore setting.
    /// </summary>
    public CpuModuleOptions WithPerCore(bool on = true) => Clone(enablePerCore: on);

    /// <summary>
    /// Returns a new instance of <see cref="CpuModuleOptions"/> with the specified LoadAverage setting.
    /// </summary>
    public CpuModuleOptions WithLoadAverage(bool on = true) => Clone(enableLoadAverage: on);

    /// <summary>
    /// Returns a new instance of <see cref="CpuModuleOptions"/> with the specified Frequency setting.
    /// </summary>
    public CpuModuleOptions WithFrequency(bool on = true) => Clone(enableFrequency: on);

    /// <summary>
    /// Returns a new instance of <see cref="CpuModuleOptions"/> with the specified ThermalAndFan setting.
    /// </summary>
    public CpuModuleOptions WithThermalAndFan(bool on = true) => Clone(enableThermalAndFan: on);

    /// <summary>
    /// Returns a new instance of <see cref="CpuModuleOptions"/> with the specified AllProcesses setting.
    /// </summary>
    public CpuModuleOptions WithAllProcesses(bool on = true) => Clone(enableAllProcesses: on);

    /// <summary>
    /// Returns a new instance of <see cref="CpuModuleOptions"/> with the specified Threads setting.
    /// </summary>
    public CpuModuleOptions WithThreads(bool on = true) => Clone(enableThreads: on);

    // =============================
    // Diagnostics
    // =============================

    /// <summary>
    /// Returns a string representation of the current options.
    /// </summary>
    public override string ToString() =>
        $"Core={EnableCore}, PerCore={EnablePerCore}, LoadAvg={EnableLoadAverage}, " +
        $"Freq={EnableFrequency}, ThermalFan={EnableThermalAndFan}, " +
        $"AllProc={EnableAllProcesses}, Threads={EnableThreads}";

    // =============================
    // Construction
    // =============================

    /// <summary>
    /// Default constructor uses lightweight defaults.
    /// </summary>
    public CpuModuleOptions()
    {
    }

    /// <summary>
    /// Private value constructor used by presets/With* methods.
    /// </summary>
    private CpuModuleOptions(
        bool enableCore,
        bool enablePerCore,
        bool enableLoadAverage,
        bool enableFrequency,
        bool enableThermalAndFan,
        bool enableAllProcesses,
        bool enableThreads)
    {
        EnableCore = enableCore;
        EnablePerCore = enablePerCore;
        EnableLoadAverage = enableLoadAverage;
        EnableFrequency = enableFrequency;
        EnableThermalAndFan = enableThermalAndFan;
        EnableAllProcesses = enableAllProcesses;
        EnableThreads = enableThreads;
    }

    /// <summary>
    /// Returns a new <see cref="CpuModuleOptions"/> instance with the specified changes.
    /// </summary>
    private CpuModuleOptions Clone(
        bool? enableCore = null,
        bool? enablePerCore = null,
        bool? enableLoadAverage = null,
        bool? enableFrequency = null,
        bool? enableThermalAndFan = null,
        bool? enableAllProcesses = null,
        bool? enableThreads = null)
        => new(
            enableCore ?? EnableCore,
            enablePerCore ?? EnablePerCore,
            enableLoadAverage ?? EnableLoadAverage,
            enableFrequency ?? EnableFrequency,
            enableThermalAndFan ?? EnableThermalAndFan,
            enableAllProcesses ?? EnableAllProcesses,
            enableThreads ?? EnableThreads);
}
