// <copyright file="CpuModulePreset.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Configuration;

/// <summary>
/// Predefined option sets for convenience.
/// </summary>
public enum CpuModulePreset
{
    /// <summary>Minimal overhead, essentials only.</summary>
    Light,

    /// <summary>Sane defaults (matches <see cref="CpuModuleOptions"/> defaults).</summary>
    Default,

    /// <summary>Enable everything (heaviest footprint).</summary>
    Verbose
}
