// <copyright file="SystemIoModuleOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SystemIO.Configuration;

/// <summary>
/// Provides configuration options for controlling which System I/O metrics are collected,
/// including process-level and system-level metrics.
/// </summary>
public sealed class SystemIoModuleOptions
{
    /// <summary>
    /// Defines preset configurations for the <see cref="SystemIoModuleOptions"/>.
    /// </summary>
    public enum Preset
    {
        /// <summary>
        /// A preset configuration with only process-related metrics enabled.
        /// </summary>
        Light,

        /// <summary>
        /// The default preset configuration with both process and system metrics enabled.
        /// </summary>
        Default,

        /// <summary>
        /// A preset configuration with both process and system metrics enabled, 
        /// providing more detailed and verbose data.
        /// </summary>
        Verbose
    }

    /// <summary>
    /// Creates a new instance of <see cref="SystemIoModuleOptions"/> based on a preset configuration.
    /// </summary>
    /// <param name="p">The preset configuration to use.</param>
    /// <returns>
    /// A new <see cref="SystemIoModuleOptions"/> instance based on the specified preset.
    /// </returns>
    public static SystemIoModuleOptions FromPreset(Preset p) => p switch
    {
        Preset.Light => new(enableProcess: true, enableSystem: false),
        Preset.Verbose => new(enableProcess: true, enableSystem: true),
        _ => new()
    };

    /// <summary>
    /// Gets a value indicating whether process metrics are enabled.
    /// </summary>
    public bool EnableProcess { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether system metrics are enabled.
    /// </summary>
    public bool EnableSystem { get; init; } = true;

    /// <summary>
    /// Returns a string representation of the current <see cref="SystemIoModuleOptions"/> instance.
    /// </summary>
    /// <returns>
    /// A string that represents the current options, including whether 
    /// process and system metrics are enabled.
    /// </returns>
    public override string ToString() => $"Process={EnableProcess}, System={EnableSystem}";

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemIoModuleOptions"/> class 
    /// with default values.
    /// </summary>
    public SystemIoModuleOptions() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemIoModuleOptions"/> class 
    /// with specific values.
    /// </summary>
    /// <param name="enableProcess">A value indicating whether process metrics are enabled.</param>
    /// <param name="enableSystem">A value indicating whether system metrics are enabled.</param>
    private SystemIoModuleOptions(bool enableProcess, bool enableSystem)
    {
        EnableProcess = enableProcess;
        EnableSystem = enableSystem;
    }

    /// <summary>
    /// Creates a new <see cref="SystemIoModuleOptions"/> instance 
    /// with process metrics enabled or disabled.
    /// </summary>
    /// <param name="on">A boolean value indicating whether process metrics are enabled.</param>
    /// <returns>
    /// A new <see cref="SystemIoModuleOptions"/> instance with the specified process setting.
    /// </returns>
    public SystemIoModuleOptions WithProcess(bool on = true) => new(on, EnableSystem);

    /// <summary>
    /// Creates a new <see cref="SystemIoModuleOptions"/> instance 
    /// with system metrics enabled or disabled.
    /// </summary>
    /// <param name="on">A boolean value indicating whether system metrics are enabled.</param>
    /// <returns>
    /// A new <see cref="SystemIoModuleOptions"/> instance with the specified system setting.
    /// </returns>
    public SystemIoModuleOptions WithSystem(bool on = true) => new(EnableProcess, on);
}
