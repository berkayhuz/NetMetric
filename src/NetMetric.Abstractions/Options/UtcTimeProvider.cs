// <copyright file="UtcTimeProvider.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Default implementation of <see cref="ITimeProvider"/> that retrieves the current time
/// from <see cref="DateTime.UtcNow"/>.
/// <para>
/// This provider is suitable for most production scenarios where system wall-clock time
/// is sufficient. For testing, alternative implementations of <see cref="ITimeProvider"/>
/// (e.g., mock or deterministic providers) can be substituted.
/// </para>
/// </summary>
public sealed class UtcTimeProvider : ITimeProvider
{
    /// <summary>
    /// Gets the current UTC time using <see cref="DateTime.UtcNow"/>.
    /// </summary>
    public DateTime UtcNow => DateTime.UtcNow;
}
