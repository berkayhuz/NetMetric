// <copyright file="IModuleLifecycle.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Optional lifecycle contract for modules.
/// </summary>
public interface IModuleLifecycle
{
    /// <summary>
    /// Called once during module initialization.
    /// </summary>
    void OnInit();

    /// <summary>
    /// Called immediately before metrics are collected.
    /// </summary>
    void OnBeforeCollect();

    /// <summary>
    /// Called immediately after metrics are collected.
    /// </summary>
    void OnAfterCollect();

    /// <summary>
    /// Called when the module is being disposed.
    /// </summary>
    void OnDispose();
}
