// <copyright file="ITimerScope.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Represents a disposable timing scope used to measure the duration of a code block.
/// <para>
/// A timer scope is typically created by a timer metric via a <c>Start()</c> or
/// <c>Track()</c> method, and records the elapsed time automatically when disposed.
/// This pattern ensures that timing is always measured reliably, even when exceptions occur.
/// </para>
/// </summary>
/// <remarks>
/// The recommended usage pattern is the <c>using</c> statement:
/// <code>
/// using (var scope = timerMetric.Start())
/// {
///     // Code to measure
/// }
/// </code>
/// When the <c>using</c> block ends, the scope is disposed and the elapsed
/// duration is recorded.
/// </remarks>
public interface ITimerScope : IDisposable
{
}
