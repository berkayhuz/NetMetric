// <copyright file="IBucketHistogramMetric.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Fixed-bucket histogram metric.
/// </summary>
public interface IBucketHistogramMetric : IMetric
{
    void Observe(double value);
    bool TryObserve(double value);
}
