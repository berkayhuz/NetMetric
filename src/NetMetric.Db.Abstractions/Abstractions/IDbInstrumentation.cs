// <copyright file="IDbInstrumentation.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Db.Abstractions;

/// <summary>
/// Defines the instrumentation API for database providers or interceptors
/// to report connection and query lifecycle events into the metrics system.
/// </summary>
/// <remarks>
/// Implementations of this interface update the associated <c>DbMetricsModule</c>
/// with metrics such as active connections, query duration, and failed queries.
/// </remarks>
public interface IDbInstrumentation
{
    /// <summary>
    /// Signals that a database connection has been opened.  
    /// Increments the active connection counter.
    /// </summary>
    void OnConnectionOpened();

    /// <summary>
    /// Signals that a database connection has been closed.  
    /// Decrements the active connection counter.
    /// </summary>
    void OnConnectionClosed();

    /// <summary>
    /// Starts a high-resolution timer for measuring query execution duration.
    /// </summary>
    /// <param name="tags">
    /// Optional metric tags that can be associated with the query.  
    /// Implementations may apply these tags during export.
    /// </param>
    /// <returns>
    /// An <see cref="IDisposable"/> scope that stops the timer when disposed.
    /// </returns>
    IDisposable StartQueryTimer(IReadOnlyDictionary<string, string>? tags = null);

    /// <summary>
    /// Signals that a database query has failed.  
    /// Increments the query error counter.
    /// </summary>
    /// <param name="tags">Optional metric tags to associate with the failure.</param>
    void OnQueryFailed(IReadOnlyDictionary<string, string>? tags = null);
}
