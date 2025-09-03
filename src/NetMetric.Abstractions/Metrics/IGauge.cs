// <copyright file="IGauge.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Simple builder/adapter for a single-value gauge.
/// </summary>
public interface IGauge : IMetric
{
    /// <summary>
    /// Sets the gauge to a specific value.
    /// </summary>
    void SetValue(double value);
}
