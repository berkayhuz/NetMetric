// <copyright file="IRedisClient.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Net;

namespace NetMetric.Redis.Abstractions;

/// <summary>
/// Defines an asynchronous abstraction for interacting with a Redis server or cluster,
/// covering health checks (<c>PING</c>), <c>INFO</c> retrieval, latency inspection, and select administrative toggles.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IRedisClient"/> interface is transport/client-implementation agnostic and is intended to be used by
/// collectors and modules that need to read server state and emit metrics without binding to a specific Redis client library.
/// </para>
/// <para>
/// Unless otherwise stated, methods are safe to call concurrently from multiple threads. Implementations should ensure
/// reentrancy and avoid blocking calls by using asynchronous I/O throughout.
/// </para>
/// <para>
/// Cancellation: All async operations accept a <see cref="System.Threading.CancellationToken"/>. If cancellation is requested
/// before or during the operation, an <see cref="OperationCanceledException"/> (or <see cref="TaskCanceledException"/>)
/// is expected to be thrown by the implementation.
/// </para>
/// </remarks>
/// <example>
/// <para>
/// Typical usage in a metrics collector:
/// </para>
/// <code language="csharp"><![CDATA[
/// using System;
/// using System.Net;
/// using System.Threading;
/// using NetMetric.Redis.Abstractions;
///
/// public sealed class SampleCollector
/// {
///     private readonly IRedisClient _client;
///
///     public SampleCollector(IRedisClient client) => _client = client;
///
///     public async Task CollectAsync(CancellationToken ct)
///     {
///         // Health check
///         bool ok = await _client.PingAsync(ct).ConfigureAwait(false);
///         Console.WriteLine($"Ping OK: {ok}");
///
///         // Inspect server info
///         string memInfo = await _client.InfoAsync("memory", ct).ConfigureAwait(false);
///         Console.WriteLine(memInfo);
///
///         // Iterate endpoints (cluster/sentinel)
///         foreach (EndPoint ep in _client.Endpoints())
///         {
///             long? slowlogLen = await _client.SlowlogLenAtAsync(ep, ct).ConfigureAwait(false);
///             Console.WriteLine($"Slowlog length at {ep}: {slowlogLen ?? -1}");
///         }
///
///         // Get latest latency samples
///         var events = await _client.LatencyLatestAsync(ct).ConfigureAwait(false);
///         foreach (var e in events)
///         {
///             Console.WriteLine($"Latency event {e.Event}: {e.Ms} ms");
///         }
///     }
/// }
/// ]]></code>
/// </example>
public interface IRedisClient : IAsyncDisposable
{
    /// <summary>
    /// Sends a <c>PING</c> to the Redis server to verify liveness and round-trip responsiveness.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// <see langword="true"/> if the server responded successfully; otherwise, <see langword="false"/> if the client received a non-OK reply.
    /// </returns>
    /// <remarks>
    /// Use this method as a lightweight health probe. It is typically cheaper than running full <c>INFO</c> or other diagnostic commands.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    Task<bool> PingAsync(System.Threading.CancellationToken ct);

    /// <summary>
    /// Retrieves the textual output of the Redis <c>INFO</c> command for a given section from the default server/connection.
    /// </summary>
    /// <param name="section">
    /// The <c>INFO</c> section to query (for example, <c>"server"</c>, <c>"clients"</c>, <c>"memory"</c>, <c>"persistence"</c>, <c>"stats"</c>,
    /// <c>"replication"</c>, <c>"cpu"</c>, <c>"cluster"</c>, <c>"keyspace"</c>). Use an empty string to request all sections.
    /// </param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The raw <c>INFO</c> payload as a UTF-8 text block.</returns>
    /// <remarks>
    /// The returned format follows Redis conventions: a sequence of <c>key:value</c> lines grouped by section headers.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="section"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    Task<string> InfoAsync(string section, System.Threading.CancellationToken ct);

    /// <summary>
    /// Retrieves the textual output of the Redis <c>INFO</c> command for a given section from a specific <see cref="EndPoint"/>.
    /// </summary>
    /// <param name="section">
    /// The <c>INFO</c> section to query (for example, <c>"server"</c>, <c>"memory"</c>, or <c>"keyspace"</c>).
    /// </param>
    /// <param name="endpoint">The Redis endpoint (node) to target. Useful in cluster/sentinel deployments.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The raw <c>INFO</c> payload for the specified section at the given <paramref name="endpoint"/>.</returns>
    /// <remarks>
    /// If <paramref name="endpoint"/> is unknown or currently unreachable, implementations may throw or return an error from the underlying client.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="section"/> or <paramref name="endpoint"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    Task<string> InfoAsyncAt(string section, EndPoint endpoint, System.Threading.CancellationToken ct);

    /// <summary>
    /// Returns the length of the server slowlog (i.e., total number of recorded slow commands) from the default server/connection.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// The slowlog length if available; otherwise, <see langword="null"/> when the value cannot be determined or the command is not supported.
    /// </returns>
    /// <remarks>
    /// This is a lightweight counter and does not return entries themselves. Use higher-level diagnostics to enumerate slowlog details if needed.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    Task<long?> SlowlogLenAsync(System.Threading.CancellationToken ct);

    /// <summary>
    /// Returns the length of the server slowlog from a specific <see cref="EndPoint"/>.
    /// </summary>
    /// <param name="endpoint">The Redis endpoint (node) to target. Useful in cluster/sentinel deployments.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// The slowlog length at <paramref name="endpoint"/> if available; otherwise, <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Implementations should gracefully handle nodes that are temporarily unavailable (e.g., return <see langword="null"/> or surface a meaningful exception).
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="endpoint"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    Task<long?> SlowlogLenAtAsync(EndPoint endpoint, System.Threading.CancellationToken ct);

    /// <summary>
    /// Retrieves the latest latency events reported by Redis via <c>LATENCY LATEST</c> from the default server/connection.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// A read-only list of pairs where <c>Event</c> is the latency event name (for example, <c>command</c>, <c>fast-command</c>, etc.)
    /// and <c>Ms</c> is the latest observed latency in milliseconds.
    /// </returns>
    /// <remarks>
    /// The returned list reflects only the last recorded sample per event type, not historical series. Use this to derive percentile
    /// summaries across nodes or to alert on spikes.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    Task<IReadOnlyList<(string Event, double Ms)>> LatencyLatestAsync(System.Threading.CancellationToken ct);

    /// <summary>
    /// Retrieves the latest latency events via <c>LATENCY LATEST</c> from a specific <see cref="EndPoint"/>.
    /// </summary>
    /// <param name="endpoint">The Redis endpoint (node) to target. Useful in cluster/sentinel deployments.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// A read-only list of pairs where <c>Event</c> is the latency event name and <c>Ms</c> is the latest observed latency in milliseconds for <paramref name="endpoint"/>.
    /// </returns>
    /// <remarks>
    /// Implementations should normalize the result format even if the underlying client exposes latency data in different shapes.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="endpoint"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    Task<IReadOnlyList<(string Event, double Ms)>> LatencyLatestAtAsync(EndPoint endpoint, System.Threading.CancellationToken ct);

    /// <summary>
    /// Returns all currently known and reachable Redis endpoints for the active connection(s).
    /// </summary>
    /// <returns>
    /// A read-only list of <see cref="EndPoint"/> instances representing nodes the client is aware of (e.g., cluster primaries/replicas, sentinel hosts).
    /// </returns>
    /// <remarks>
    /// The list may change over time as topology discovery or reconnection occurs.
    /// </remarks>
    IReadOnlyList<EndPoint> Endpoints();

    /// <summary>
    /// Enables or disables administrative commands for the underlying Redis client (for example, to access <c>SLOWLOG</c> or <c>LATENCY</c> features),
    /// which may require reconnection in some client libraries.
    /// </summary>
    /// <param name="allow"><see langword="true"/> to allow admin commands; <see langword="false"/> to disallow.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Some Redis client libraries gate potentially sensitive commands behind an "allow admin" switch and apply the setting on (re)connect.
    /// Implementations may close and reopen connections to honor the new setting.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    Task AllowAdmin(bool allow, System.Threading.CancellationToken ct);

    /// <summary>
    /// Sets the command timeout (in milliseconds) for subsequent Redis operations, which may require reconnection in some client libraries.
    /// </summary>
    /// <param name="milliseconds">The desired command timeout in milliseconds. Must be a positive value.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Implementations should validate <paramref name="milliseconds"/> and apply the value atomically to all relevant connections.
    /// If the underlying library applies timeouts during connect, a reconnect cycle may be performed.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="milliseconds"/> is less than or equal to zero.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="ct"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    Task SetCommandTimeout(int milliseconds, System.Threading.CancellationToken ct);
}
