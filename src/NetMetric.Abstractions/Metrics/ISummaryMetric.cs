// <copyright file="ISummaryMetric.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Streaming quantile metric (summary). Records scalar observations.
/// </summary>
public interface ISummaryMetric : IMetric
{
    IReadOnlyList<double> Quantiles
    {
        get;
    }
    void Record(double value);
    bool TryRecord(double value);
}
