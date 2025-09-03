// <copyright file="IMultiGauge.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Builder contract for creating multi-sample (multi-value) gauge metrics.
/// Collectors add samples through this interface and finally return an <see cref="IMetric"/>.
/// </summary>
public interface IMultiGauge : IMetric
{
    bool ResetOnGet { get; }

    int ApproximateCount { get; }

    /// <summary>
    /// Sets the gauge to a specific value with optional tags.
    /// </summary>
    void SetValue(double value, IReadOnlyDictionary<string, string>? tags = null);

    /// <summary>
    /// Adds another sibling value with id and name.
    /// </summary>
    void AddSibling(string id, string name, double value, IReadOnlyDictionary<string, string>? tags = null);

    /// <summary>
    /// Clears all values from this gauge.
    /// </summary>
    void Clear();
}
