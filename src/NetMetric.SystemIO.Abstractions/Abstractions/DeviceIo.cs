// <copyright file="DeviceIo.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SystemIO.Abstractions;

/// <summary>
/// Represents the I/O statistics for a specific device.
/// </summary>
/// <param name="Device">The name or identifier of the device.</param>
/// <param name="ReadBytes">The total number of bytes read from the device.</param>
/// <param name="WriteBytes">The total number of bytes written to the device.</param>
public readonly record struct DeviceIo(string Device, ulong ReadBytes, ulong WriteBytes);
