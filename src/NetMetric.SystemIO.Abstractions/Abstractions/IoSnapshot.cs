// <copyright file="IoSnapshot.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SystemIO.Abstractions;

/// <summary>
/// Represents a snapshot of I/O statistics for a process or system at a given point in time.
/// </summary>
/// <param name="ReadBytes">The total number of bytes read.</param>
/// <param name="WriteBytes">The total number of bytes written.</param>
/// <param name="TsUtc">The timestamp of the snapshot in UTC.</param>
public readonly record struct IoSnapshot(ulong ReadBytes, ulong WriteBytes, DateTime TsUtc);
