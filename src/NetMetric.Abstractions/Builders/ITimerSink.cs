// <copyright file="ITimerSink.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Consumer for measured durations. Decouples timing from metric/export choice.
/// </summary>
public interface ITimerSink
{
    /// <param name="id">Stable id (e.g., "repo.getById").</param>
    /// <param name="name">Human-friendly name.</param>
    /// <param name="elapsedMs">Measured duration in milliseconds.</param>
    /// <param name="tags">Optional key/values for dimensions (e.g., method, route).</param>
    void Record(string id, string name, double elapsedMs, IReadOnlyDictionary<string, string>? tags = null);
}
