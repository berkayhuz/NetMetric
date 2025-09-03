// StackExchangeRedisClient.cs
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0

using StackExchange.Redis;

namespace NetMetric.Redis.Client;

/// <summary>
/// A thin, resilient Redis client built on top of <see cref="StackExchange.Redis.ConnectionMultiplexer"/> that
/// provides safe reconnects with exponential backoff, basic server operations (PING, INFO, SLOWLOG, LATENCY),
/// and configurable timeouts.
/// </summary>
/// <remarks>
/// <para>
/// This client encapsulates a single <see cref="StackExchange.Redis.ConnectionMultiplexer"/> instance and
/// serializes reconnect attempts with a <see cref="System.Threading.SemaphoreSlim"/> to avoid connection storms.
/// Read-style calls (e.g., <see cref="PingAsync(System.Threading.CancellationToken)"/>, <see cref="InfoAsync(string, System.Threading.CancellationToken)"/>)
/// obtain the current multiplexer via <see cref="System.Threading.Volatile.Read{T}(ref readonly T)"/> to ensure safe concurrent access.
/// </para>
/// <para><b>Thread safety</b><br/>
/// All public members are designed for concurrent use. Reconfiguration operations such as
/// <see cref="AllowAdmin(bool, System.Threading.CancellationToken)"/> and <see cref="SetCommandTimeout(int, System.Threading.CancellationToken)"/>
/// trigger a serialized reconnect to apply settings safely.
/// </para>
/// <para><b>Usage</b></para>
/// <example>
/// <code>
/// using System;
/// using System.Threading;
/// using NetMetric.Redis.Client;
///
/// var client = await StackExchangeRedisClient.ConnectAsync(
///     cs: "localhost:6379",
///     connectTimeoutMs: 5000,
///     commandTimeoutMs: 2000,
///     allowAdmin: true);
///
/// // Health check
/// bool ok = await client.PingAsync(CancellationToken.None);
///
/// // Fetch server info (single section)
/// string mem = await client.InfoAsync("memory", CancellationToken.None);
///
/// // Read SLOWLOG LEN
/// long? slowCount = await client.SlowlogLenAsync(CancellationToken.None);
///
/// // Read latest latency samples
/// var latest = await client.LatencyLatestAsync(CancellationToken.None);
/// foreach (var (evtName, ms) in latest)
///     Console.WriteLine($"{evtName}: {ms} ms");
///
/// await client.DisposeAsync();
/// </code>
/// </example>
/// </remarks>
/// <seealso href="https://stackexchange.github.io/StackExchange.Redis/Configuration.html">StackExchange.Redis configuration</seealso>
internal sealed class StackExchangeRedisClient : IRedisClient
{
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);
    private ConnectionMultiplexer _mux;

    private readonly string _connectionString;
    private readonly int _connectTimeoutMs;
    private int _commandTimeoutMs;
    private bool _allowAdmin;

    private const int ReconnectMaxAttempts = 5;
    private const int ReconnectBaseDelayMs = 100;
    private const int ReconnectMaxDelayMs = 5000;

    /// <summary>
    /// Initializes a new instance of the <see cref="StackExchangeRedisClient"/> class.
    /// </summary>
    /// <param name="mux">The connected <see cref="StackExchange.Redis.ConnectionMultiplexer"/> to wrap.</param>
    /// <param name="cs">The Redis connection string used to create the multiplexer.</param>
    /// <param name="connectTimeoutMs">The connect timeout (ms) applied on (re)connect.</param>
    /// <param name="commandTimeoutMs">The command timeout (ms) applied on (re)connect.</param>
    /// <param name="allowAdmin">Whether administrative commands are permitted (e.g., INFO, SLOWLOG).</param>
    private StackExchangeRedisClient(ConnectionMultiplexer mux, string cs, int connectTimeoutMs, int commandTimeoutMs, bool allowAdmin)
    {
        _mux = mux;
        _connectionString = cs;
        _connectTimeoutMs = connectTimeoutMs;
        _commandTimeoutMs = commandTimeoutMs;
        _allowAdmin = allowAdmin;
    }

    /// <summary>
    /// Establishes a new Redis connection and returns a configured <see cref="StackExchangeRedisClient"/>.
    /// </summary>
    /// <param name="cs">The Redis connection string.</param>
    /// <param name="connectTimeoutMs">The connect timeout in milliseconds.</param>
    /// <param name="commandTimeoutMs">The command timeout in milliseconds. Default is <c>1000</c>.</param>
    /// <param name="allowAdmin">Whether administrative commands are allowed. Default is <c>true</c>.</param>
    /// <returns>An initialized <see cref="StackExchangeRedisClient"/>.</returns>
    /// <remarks>
    /// Internally invokes <see cref="ConnectMuxAsync(string, int, int, bool)"/> to create a multiplexer with sensible defaults,
    /// <c>AbortOnConnectFail=false</c>, and an exponential reconnect retry policy on .NET 6+.
    /// </remarks>
    /// <exception cref="StackExchange.Redis.RedisConnectionException">The connection could not be established.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">A socket error occurred while connecting.</exception>
    public static async Task<StackExchangeRedisClient> ConnectAsync(string cs, int connectTimeoutMs, int commandTimeoutMs = 1000, bool allowAdmin = true)
    {
        var mux = await ConnectMuxAsync(cs, connectTimeoutMs, commandTimeoutMs, allowAdmin).ConfigureAwait(false);

        return new StackExchangeRedisClient(mux, cs, connectTimeoutMs, commandTimeoutMs, allowAdmin);
    }

    /// <summary>
    /// Creates and configures a <see cref="StackExchange.Redis.ConnectionMultiplexer"/> instance.
    /// </summary>
    /// <param name="cs">The Redis connection string.</param>
    /// <param name="connectTimeoutMs">Connect timeout (ms).</param>
    /// <param name="commandTimeoutMs">Sync/async command timeouts (ms).</param>
    /// <param name="allowAdmin">Whether administrative commands are permitted.</param>
    /// <returns>A connected <see cref="StackExchange.Redis.ConnectionMultiplexer"/>.</returns>
    /// <remarks>
    /// Sets <c>ClientName</c>, <c>KeepAlive</c>, <c>AbortOnConnectFail=false</c>;
    /// on .NET 6+ also sets <see cref="StackExchange.Redis.ExponentialRetry"/> as <c>ReconnectRetryPolicy</c>.
    /// </remarks>
    /// <exception cref="StackExchange.Redis.RedisConnectionException">If the connection cannot be established.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">If a socket error occurs.</exception>
    private static async Task<ConnectionMultiplexer> ConnectMuxAsync(string cs, int connectTimeoutMs, int commandTimeoutMs, bool allowAdmin)
    {
        var opt = ConfigurationOptions.Parse(cs);

        opt.ConnectTimeout = connectTimeoutMs;
        opt.DefaultDatabase = 0;
        opt.SyncTimeout = commandTimeoutMs;
        opt.AsyncTimeout = commandTimeoutMs;
        opt.AbortOnConnectFail = false;
        opt.AllowAdmin = allowAdmin;

        opt.ClientName = nameof(StackExchangeRedisClient);
        opt.KeepAlive = Math.Max(15, commandTimeoutMs / 1000);

#if NET6_0_OR_GREATER
        opt.ReconnectRetryPolicy = new ExponentialRetry(5000);
#endif

        return await ConnectionMultiplexer.ConnectAsync(opt).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a connected <see cref="StackExchange.Redis.IServer"/> for the given endpoint, or a best effort fallback.
    /// </summary>
    /// <param name="preferred">An optional preferred <see cref="System.Net.EndPoint"/>.</param>
    /// <returns>A connected <see cref="StackExchange.Redis.IServer"/>.</returns>
    /// <exception cref="System.InvalidOperationException">If no Redis endpoints are available.</exception>
    private IServer GetServerSafe(System.Net.EndPoint? preferred = null)
    {
        var mux = System.Threading.Volatile.Read(ref _mux);
        var eps = mux.GetEndPoints();

        if (eps is null || eps.Length == 0)
        {
            throw new InvalidOperationException("No Redis endpoints are available.");
        }

        if (preferred is not null)
        {
            var usePreferred = eps.Any(ep => ep.Equals(preferred));
            if (usePreferred)
            {
                var srv = mux.GetServer(preferred);

                if (srv?.IsConnected == true)
                {
                    return srv;
                }
            }
        }

        foreach (var ep in eps)
        {
            var srv = mux.GetServer(ep);

            if (srv?.IsConnected == true)
            {
                return srv;
            }
        }

        return mux.GetServer(eps[0]);
    }

    /// <summary>
    /// Gets the list of endpoints exposed by the current connection.
    /// </summary>
    /// <returns>A read-only list of <see cref="System.Net.EndPoint"/> values.</returns>
    public IReadOnlyList<System.Net.EndPoint> Endpoints()
    {
        var mux = System.Threading.Volatile.Read(ref _mux);
        var arr = mux.GetEndPoints();

        return Array.AsReadOnly(arr);
    }

    /// <summary>
    /// Enables or disables administrative commands and applies the change via a serialized reconnect.
    /// </summary>
    /// <param name="allow"><c>true</c> to allow admin commands; otherwise <c>false</c>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the setting has been applied.</returns>
    /// <remarks>
    /// If the requested value matches the current one, the method is a no-op.
    /// Otherwise it calls <see cref="ReconnectAsync(string, int, int, bool, System.Threading.CancellationToken)"/>.
    /// </remarks>
    /// <exception cref="System.OperationCanceledException">If <paramref name="ct"/> is canceled.</exception>
    /// <exception cref="StackExchange.Redis.RedisConnectionException">If reconnection fails.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">If a socket error occurs during reconnect.</exception>
    public async Task AllowAdmin(bool allow, System.Threading.CancellationToken ct)
    {
        if (allow == _allowAdmin)
        {
            return;
        }

        await ReconnectAsync(_connectionString, _connectTimeoutMs, _commandTimeoutMs, allow, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the command timeout and applies the change via a serialized reconnect.
    /// </summary>
    /// <param name="milliseconds">New command timeout in milliseconds; must be &gt; 0.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the timeout has been applied.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">If <paramref name="milliseconds"/> is not positive.</exception>
    /// <exception cref="System.OperationCanceledException">If <paramref name="ct"/> is canceled.</exception>
    /// <exception cref="StackExchange.Redis.RedisConnectionException">If reconnection fails.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">If a socket error occurs during reconnect.</exception>
    public async Task SetCommandTimeout(int milliseconds, System.Threading.CancellationToken ct)
    {
        System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(milliseconds, nameof(milliseconds));

        if (milliseconds == _commandTimeoutMs)
        {
            return;
        }

        await ReconnectAsync(_connectionString, _connectTimeoutMs, milliseconds, _allowAdmin, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Calculates the delay (ms) for the current reconnect attempt using capped exponential backoff with jitter.
    /// </summary>
    /// <param name="attempt">Zero-based attempt number.</param>
    /// <returns>The delay in milliseconds.</returns>
    private static int BackoffDelayMs(int attempt)
    {
        var exp = Math.Min(attempt, 8);
        var baseDelay = ReconnectBaseDelayMs * (1 << exp);

        int jitter = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 100);

        return Math.Min(ReconnectMaxDelayMs, baseDelay + jitter);
    }

    /// <summary>
    /// Reconnects to Redis with retries and exponential backoff, applying the supplied settings atomically.
    /// </summary>
    /// <param name="cs">The Redis connection string.</param>
    /// <param name="connectTimeoutMs">The connect timeout (ms).</param>
    /// <param name="commandTimeoutMs">The command timeout (ms).</param>
    /// <param name="allowAdmin">Whether administrative commands are permitted.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when a new connection is established and swapped in.</returns>
    /// <remarks>
    /// On success, the previous multiplexer is disposed asynchronously. If all attempts fail, the last
    /// captured exception is rethrown; if none was captured, a <see cref="StackExchange.Redis.RedisConnectionException"/>
    /// with <see cref="StackExchange.Redis.ConnectionFailureType.UnableToConnect"/> is thrown.
    /// </remarks>
    /// <exception cref="System.OperationCanceledException">If <paramref name="ct"/> is canceled.</exception>
    /// <exception cref="StackExchange.Redis.RedisConnectionException">If the reconnect ultimately fails.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">If a socket error occurs during reconnect.</exception>
    public async Task ReconnectAsync(string cs, int connectTimeoutMs, int commandTimeoutMs, bool allowAdmin, System.Threading.CancellationToken ct)
    {
        await _reconnectGate.WaitAsync(ct).ConfigureAwait(false);

        Exception? last = null;

        try
        {
            for (int attempt = 0; attempt < ReconnectMaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var newMux = await ConnectMuxAsync(cs, connectTimeoutMs, commandTimeoutMs, allowAdmin).ConfigureAwait(false);
                    var old = System.Threading.Interlocked.Exchange(ref _mux, newMux);

                    _commandTimeoutMs = commandTimeoutMs;
                    _allowAdmin = allowAdmin;

                    if (old is not null)
                    {
                        await old.DisposeAsync().ConfigureAwait(false);
                    }

                    return;
                }
                catch (Exception ex) when ((ex is StackExchange.Redis.RedisConnectionException || ex is System.Net.Sockets.SocketException) && !ct.IsCancellationRequested && attempt < ReconnectMaxAttempts - 1)
                {
                    last = ex;

                    await Task.Delay(BackoffDelayMs(attempt), ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    last = ex;

                    break;

                    throw;
                }
            }

            if (last is not null)
            {
                throw last;
            }

            throw new StackExchange.Redis.RedisConnectionException(StackExchange.Redis.ConnectionFailureType.UnableToConnect, "Reconnect attempts exhausted.");
        }
        finally
        {
            _reconnectGate.Release();
        }
    }

    /// <summary>
    /// Sends a <c>PING</c> to Redis to verify responsiveness.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>true</c> if a response was received; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Any exceptions during PING are swallowed and reported as <c>false</c>.
    /// </remarks>
    public async Task<bool> PingAsync(System.Threading.CancellationToken ct)
    {
        try
        {
            var res = await System.Threading.Volatile.Read(ref _mux).GetDatabase().PingAsync().ConfigureAwait(false);

            return res.TotalMilliseconds >= 0;
        }
        catch
        {
            return false;

            throw;
        }
    }

    /// <summary>
    /// Executes <c>INFO &lt;section&gt;</c> on a connected server selected automatically.
    /// </summary>
    /// <param name="section">The INFO section name (e.g., <c>"server"</c>, <c>"memory"</c>).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The INFO payload as a string, or <see cref="string.Empty"/> if unavailable.</returns>
    /// <remarks>
    /// If server selection fails (no endpoints), an <see cref="System.InvalidOperationException"/> is thrown.
    /// Otherwise transient Redis errors are caught and an empty string is returned.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">If no connected server can be selected.</exception>
    public async Task<string> InfoAsync(string section, System.Threading.CancellationToken ct)
    {
        return await InfoAsyncAt(section, GetServerSafe().EndPoint, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes <c>INFO &lt;section&gt;</c> against the specified endpoint.
    /// </summary>
    /// <param name="section">The INFO section name (e.g., <c>"server"</c>, <c>"memory"</c>).</param>
    /// <param name="endpoint">The target <see cref="System.Net.EndPoint"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The INFO payload as a string, or <see cref="string.Empty"/> if unavailable.</returns>
    /// <remarks>
    /// Returns an empty string on <see cref="StackExchange.Redis.RedisTimeoutException"/>,
    /// <see cref="StackExchange.Redis.RedisConnectionException"/>, or <see cref="StackExchange.Redis.RedisServerException"/>.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">If <paramref name="endpoint"/> is not recognized or no server is connected.</exception>
    public async Task<string> InfoAsyncAt(string section, System.Net.EndPoint endpoint, System.Threading.CancellationToken ct)
    {
        try
        {
            var srv = GetServerSafe(endpoint);

            var s = await srv.InfoRawAsync(section).ConfigureAwait(false);

            return s ?? string.Empty;
        }
        catch
        {

            return string.Empty;

            throw;
        }
    }

    /// <summary>
    /// Returns the server SLOWLOG length via <c>SLOWLOG LEN</c> from an automatically selected server.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The slowlog length, or <c>null</c> if unavailable.</returns>
    /// <exception cref="System.InvalidOperationException">If no connected server can be selected.</exception>
    public async Task<long?> SlowlogLenAsync(System.Threading.CancellationToken ct)
    {
        return await SlowlogLenAtAsync(GetServerSafe().EndPoint, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the server SLOWLOG length via <c>SLOWLOG LEN</c> from the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The target <see cref="System.Net.EndPoint"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The slowlog length, or <c>null</c> if unavailable.</returns>
    /// <remarks>
    /// On Redis timeout/connection/server exceptions, returns <c>null</c>.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">If <paramref name="endpoint"/> is not recognized or no server is connected.</exception>
    public async Task<long?> SlowlogLenAtAsync(System.Net.EndPoint endpoint, System.Threading.CancellationToken ct)
    {
        try
        {
            var srv = GetServerSafe(endpoint);
            var res = await srv.ExecuteAsync("SLOWLOG", "LEN").ConfigureAwait(false);

            return long.TryParse(res?.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var len) ? len : (long?)null;
        }
        catch
        {
            return null;

            throw;
        }
    }

    /// <summary>
    /// Retrieves the latest latency events reported by <c>LATENCY LATEST</c> from an automatically selected server.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A list of <c>(Event, Ms)</c> tuples, or an empty list if none are available.</returns>
    /// <remarks>
    /// Delegates to <see cref="LatencyLatestAtAsync(System.Net.EndPoint, System.Threading.CancellationToken)"/>.
    /// </remarks>
    public async Task<IReadOnlyList<(string Event, double Ms)>> LatencyLatestAsync(System.Threading.CancellationToken ct)
    {
        return await LatencyLatestAtAsync(GetServerSafe().EndPoint, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the latest latency events reported by <c>LATENCY LATEST</c> from the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The target <see cref="System.Net.EndPoint"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A list of <c>(Event, Ms)</c> tuples, or an empty list if none are available.</returns>
    /// <remarks>
    /// <para>
    /// Supports both RESP array and textual responses. For robustness, parsing errors and common Redis exceptions
    /// (<see cref="StackExchange.Redis.RedisTimeoutException"/>, <see cref="StackExchange.Redis.RedisConnectionException"/>,
    /// <see cref="StackExchange.Redis.RedisServerException"/>) are swallowed and an empty result is returned.
    /// </para>
    /// </remarks>
    /// <exception cref="System.OperationCanceledException">If <paramref name="ct"/> is canceled.</exception>
    /// <exception cref="System.InvalidOperationException">If <paramref name="endpoint"/> is not recognized or no server is connected.</exception>
    public async Task<IReadOnlyList<(string Event, double Ms)>> LatencyLatestAtAsync(System.Net.EndPoint endpoint, System.Threading.CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var results = new List<(string Event, double Ms)>();
        var srv = GetServerSafe(endpoint);

        try
        {
            RedisResult res = await srv.ExecuteAsync("LATENCY", "LATEST").ConfigureAwait(false);

            if (res.Resp2Type == ResultType.Array)
            {
                var outer = (RedisResult[]?)res;

                if (outer is null || outer.Length == 0)
                {
                    return results;
                }

                foreach (var item in outer)
                {
                    if (item.Resp2Type != ResultType.Array)
                        continue;

                    var inner = (RedisResult[]?)item;

                    if (inner is null || inner.Length < 3)
                    {
                        continue;
                    }

                    string ev;

                    try
                    {
                        ev = (string)inner[0]!;
                    }
                    catch
                    {
                        ev = inner[0]?.ToString() ?? "unknown";

                        throw;
                    }
                    if (string.IsNullOrWhiteSpace(ev)) ev = "unknown";

                    if (inner[2].IsNull)
                    {
                        continue;
                    }

                    double latest;
                    bool ok;

                    try
                    {
                        if (inner[2].Resp2Type == ResultType.Integer)
                        {
                            var l = (long)inner[2];
                            latest = Convert.ToDouble(l, System.Globalization.CultureInfo.InvariantCulture);
                            ok = true;
                        }
                        else
                        {
                            latest = (double)inner[2];
                            ok = true;
                        }
                    }
                    catch
                    {
                        ok = double.TryParse(inner[2]?.ToString(), System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out latest);

                        throw;
                    }

                    if (ok)
                    {
                        results.Add((ev, latest));
                    }
                }
            }
            else
            {
                var s = res?.ToString();

                if (string.IsNullOrWhiteSpace(s))
                {
                    return results;
                }

                var span = s.AsSpan();

                int start = 0;

                for (int i = 0; i <= span.Length; i++)
                {
                    if (i == span.Length || span[i] == '\n')
                    {
                        var line = span.Slice(start, i - start).Trim();

                        if (!line.IsEmpty)
                        {
                            int c1 = line.IndexOf(':');

                            if (c1 > 0)
                            {
                                var afterEvent = line.Slice(c1 + 1);

                                int c2 = afterEvent.IndexOf(':');

                                if (c2 >= 0)
                                {
                                    var latestSpan = afterEvent.Slice(c2 + 1);

                                    int c3 = latestSpan.IndexOf(':');

                                    if (c3 >= 0)
                                    {
                                        latestSpan = latestSpan.Slice(0, c3);
                                    }

                                    if (double.TryParse(latestSpan, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var latest))
                                    {
                                        var ev = line.Slice(0, c1).ToString();

                                        if (string.IsNullOrWhiteSpace(ev))
                                        {
                                            ev = "unknown";
                                        }

                                        results.Add((ev, latest));
                                    }
                                }
                            }
                        }
                        start = i + 1;
                    }
                }
            }
        }
        catch (RedisTimeoutException)
        {

        }
        catch (RedisConnectionException)
        {

        }
        catch (RedisServerException)
        {

        }

        return results;
    }

    /// <summary>
    /// Closes and disposes the underlying connection and releases synchronization resources.
    /// </summary>
    /// <returns>A task that completes when disposal has finished.</returns>
    /// <remarks>
    /// Attempts an orderly <see cref="ConnectionMultiplexer.CloseAsync"/> before disposing.
    /// </remarks>
    /// <exception cref="System.ObjectDisposedException">If already disposed and underlying resources throw during disposal.</exception>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _mux.CloseAsync().ConfigureAwait(false);
        }
        catch
        {
            throw;
        }
        finally
        {
            await _mux.DisposeAsync().ConfigureAwait(false);
        }

        _mux.Dispose();
        _reconnectGate.Dispose();
    }
}
