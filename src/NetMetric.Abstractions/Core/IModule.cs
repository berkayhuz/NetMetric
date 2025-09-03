// <copyright file="IModule.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Represents a logical module that groups together a set of metric collectors.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Gets the name of the module.
    /// </summary>
    string Name
    {
        get;
    }

    /// <summary>
    /// Returns the metric collectors that belong to this module.
    /// </summary>
    IEnumerable<IMetricCollector> GetCollectors();
}
