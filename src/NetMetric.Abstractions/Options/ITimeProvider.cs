// <copyright file="ITimeProvider.cs" company="NetMetric"
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Platform-independent time provider abstraction.
/// </summary>
public interface ITimeProvider
{
    /// <summary>
    /// Gets the current UTC time.
    /// </summary>
    DateTime UtcNow
    {
        get;
    }
}
