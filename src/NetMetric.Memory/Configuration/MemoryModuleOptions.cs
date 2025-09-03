// <copyright file="MemoryModuleOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>



namespace NetMetric.Memory.Configuration;

/// <summary>
/// Represents configuration options for the memory module.
/// Allows enabling or disabling various memory collection options, including process memory, system memory, GC memory, and cgroup memory.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class MemoryModuleOptions
{
    /// <summary>
    /// Enum for predefined memory module configurations.
    /// </summary>
    public enum MemoryModulePreset
    {
        /// <summary>
        /// A light configuration with limited memory collection options.
        /// </summary>
        Light,

        /// <summary>
        /// The default configuration with basic memory collection options enabled.
        /// </summary>
        Default,

        /// <summary>
        /// A verbose configuration with all memory collection options enabled.
        /// </summary>
        Verbose
    }

    /// <summary>
    /// Creates a new instance of <see cref="MemoryModuleOptions"/> from a preset configuration.
    /// </summary>
    /// <param name="preset">The preset configuration to use.</param>
    /// <returns>A new instance of <see cref="MemoryModuleOptions"/> based on the specified preset.</returns>
    public static MemoryModuleOptions FromPreset(MemoryModulePreset preset) => preset switch
    {
        MemoryModulePreset.Light => new(enableProcess: true, enableSystem: true, enableGc: false, enableCgroup: false),

        MemoryModulePreset.Default => new(),

        MemoryModulePreset.Verbose => new(enableProcess: true, enableSystem: true, enableGc: true, enableCgroup: true),

        _ => new()
    };

    /// <summary>
    /// Gets or sets a value indicating whether to enable process memory collection.
    /// </summary>
    public bool EnableProcess { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable system memory collection.
    /// </summary>
    public bool EnableSystem { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable garbage collection memory collection.
    /// </summary>
    public bool EnableGc { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable cgroup memory collection (Linux containers).
    /// </summary>
    public bool EnableCgroup { get; init; }

    /// <summary>
    /// Gets a value indicating whether any memory collection options are enabled.
    /// </summary>
    public bool AnyEnabled => EnableProcess || EnableSystem || EnableGc || EnableCgroup;

    /// <summary>
    /// Returns a string representation of the current memory module options.
    /// </summary>
    /// <returns>A string representing the memory module options.</returns>
    public override string ToString() => $"Process={EnableProcess}, System={EnableSystem}, GC={EnableGc}, Cgroup={EnableCgroup}";

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryModuleOptions"/> class with default values.
    /// </summary>
    public MemoryModuleOptions() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryModuleOptions"/> class with specific values.
    /// </summary>
    /// <param name="enableProcess">Whether to enable process memory collection.</param>
    /// <param name="enableSystem">Whether to enable system memory collection.</param>
    /// <param name="enableGc">Whether to enable garbage collection memory collection.</param>
    /// <param name="enableCgroup">Whether to enable cgroup memory collection.</param>
    private MemoryModuleOptions(bool enableProcess, bool enableSystem, bool enableGc, bool enableCgroup)
    {
        EnableProcess = enableProcess;
        EnableSystem = enableSystem;
        EnableGc = enableGc;
        EnableCgroup = enableCgroup;
    }

    /// <summary>
    /// Returns a new instance of <see cref="MemoryModuleOptions"/> with the specified process memory setting.
    /// </summary>
    /// <param name="on">Set to <c>true</c> to enable process memory collection, <c>false</c> to disable.</param>
    /// <returns>A new instance of <see cref="MemoryModuleOptions"/> with the updated setting.</returns>
    public MemoryModuleOptions WithProcess(bool on = true) => new(on, EnableSystem, EnableGc, EnableCgroup);

    /// <summary>
    /// Returns a new instance of <see cref="MemoryModuleOptions"/> with the specified system memory setting.
    /// </summary>
    /// <param name="on">Set to <c>true</c> to enable system memory collection, <c>false</c> to disable.</param>
    /// <returns>A new instance of <see cref="MemoryModuleOptions"/> with the updated setting.</returns>
    public MemoryModuleOptions WithSystem(bool on = true) => new(EnableProcess, on, EnableGc, EnableCgroup);

    /// <summary>
    /// Returns a new instance of <see cref="MemoryModuleOptions"/> with the specified garbage collection memory setting.
    /// </summary>
    /// <param name="on">Set to <c>true</c> to enable garbage collection memory collection, <c>false</c> to disable.</param>
    /// <returns>A new instance of <see cref="MemoryModuleOptions"/> with the updated setting.</returns>
    public MemoryModuleOptions WithGc(bool on = true) => new(EnableProcess, EnableSystem, on, EnableCgroup);

    /// <summary>
    /// Returns a new instance of <see cref="MemoryModuleOptions"/> with the specified cgroup memory setting.
    /// </summary>
    /// <param name="on">Set to <c>true</c> to enable cgroup memory collection, <c>false</c> to disable.</param>
    /// <returns>A new instance of <see cref="MemoryModuleOptions"/> with the updated setting.</returns>
    public MemoryModuleOptions WithCgroup(bool on = true) => new(EnableProcess, EnableSystem, EnableGc, on);
}
