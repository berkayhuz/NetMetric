using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NetMetric.Export.Prometheus.AspNetCore.Util;

/// <summary>
/// Implements a per-IP <em>token bucket</em> rate limiter to control how often clients
/// may perform Prometheus scrapes or related operations.
/// </summary>
/// <remarks>
/// <para>
/// Each client IP address is assigned an independent token bucket with a configurable
/// capacity and refill rate (tokens per second). On each request, one token must be
/// available and is consumed. If the bucket is empty, the request is rejected
/// (i.e., considered rate-limited).
/// </para>
/// <para>
/// Time is measured using <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> for high-resolution,
/// monotonic tracking that is not affected by system clock changes.
/// Buckets are created lazily on first use and stored in a thread-safe cache.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> This class is safe for concurrent use by multiple threads.
/// Individual buckets are mutated atomically during <see cref="TryConsume(System.Net.IPAddress, int, double)"/>.
/// </para>
/// <para>
/// <strong>Choosing values:</strong>
/// <list type="bullet">
///   <item>
///     <description><c>capacity</c> defines the maximum burst that is allowed.</description>
///   </item>
///   <item>
///     <description><c>refillPerSecond</c> defines the sustained rate over time.</description>
///   </item>
/// </list>
/// For example, a capacity of <c>10</c> and a refill of <c>1</c> token/second allows short bursts
/// up to 10 requests and a steady rate of ~1 request/second thereafter.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Allow short bursts up to 10 requests, sustained 2 RPS per IP.
/// var limiter = new IpRateLimiter();
///
/// bool allowed = limiter.TryConsume(
///     ip: System.Net.IPAddress.Parse("192.0.2.10"),
///     capacity: 10,
///     refillPerSecond: 2.0);
///
/// if (!allowed)
/// {
///     // Reject or delay the request.
/// }
/// ]]></code>
/// </example>
/// <example>
/// The following shows a minimal ASP.NET Core middleware that uses the limiter for a <c>/metrics</c> endpoint:
/// <code language="csharp"><![CDATA[
/// var limiter = new IpRateLimiter();
///
/// app.Map("/metrics", builder =>
/// {
///     builder.Run(async context =>
///     {
///         var remoteIp = context.Connection.RemoteIpAddress
///                       ?? System.Net.IPAddress.IPv6None;
///
///         // 20-token burst, ~5 requests/second sustained.
///         if (!limiter.TryConsume(remoteIp, capacity: 20, refillPerSecond: 5.0))
///         {
///             context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
///             await context.Response.WriteAsync("Rate limit exceeded.");
///             return;
///         }
///
///         // Produce metrics...
///         context.Response.ContentType = "text/plain; version=0.0.4";
///         await context.Response.WriteAsync("# Metrics here...");
///     });
/// });
/// ]]></code>
/// </example>
public sealed class IpRateLimiter
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    /// <summary>
    /// Attempts to consume a single token for the given IP address.
    /// </summary>
    /// <param name="ip">The client IP address for which to charge the token.</param>
    /// <param name="capacity">The maximum number of tokens a bucket can hold (burst size).</param>
    /// <param name="refillPerSecond">The refill rate in tokens per second (sustained rate).</param>
    /// <returns>
    /// <see langword="true"/> if a token was available and consumed; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Buckets are created lazily per IP. This method recalculates the available token
    /// count based on the elapsed wall time since the last update using
    /// <see cref="System.Diagnostics.Stopwatch.Frequency"/> and
    /// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>.
    /// </para>
    /// <para>
    /// Passing very small or zero values for <paramref name="capacity"/> or
    /// <paramref name="refillPerSecond"/> effectively disables allowance for that IP.
    /// Use positive values appropriate to the traffic characteristics of your system.
    /// </para>
    /// </remarks>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="ip"/> is <see langword="null"/>.
    /// </exception>
    public bool TryConsume(System.Net.IPAddress ip, int capacity, double refillPerSecond)
    {
        ArgumentNullException.ThrowIfNull(ip);

        var key = ip.ToString();
        var now = Stopwatch.GetTimestamp();
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket(capacity, now));

        return bucket.TryTake(now, capacity, refillPerSecond);
    }

    /// <summary>
    /// Represents a single token bucket for one IP address.
    /// </summary>
    /// <remarks>
    /// The bucket tracks the current token count and the last update timestamp.
    /// Tokens are refilled proportionally to elapsed time (in seconds) and capped at <c>capacity</c>.
    /// </remarks>
    private sealed class Bucket
    {
        private double _tokens;
        private long _lastTick;

        /// <summary>
        /// Initializes a new instance of the <see cref="Bucket"/> class with the given capacity and timestamp.
        /// </summary>
        /// <param name="capacity">Initial token count and maximum capacity.</param>
        /// <param name="nowTick">The current high-resolution timestamp at creation time.</param>
        public Bucket(int capacity, long nowTick)
        {
            _tokens = capacity;
            _lastTick = nowTick;
        }

        /// <summary>
        /// Attempts to consume one token, refilling the bucket based on elapsed time.
        /// </summary>
        /// <param name="nowTick">The current high-resolution timestamp.</param>
        /// <param name="capacity">The maximum bucket capacity.</param>
        /// <param name="refillPerSecond">The refill rate, in tokens per second.</param>
        /// <returns>
        /// <see langword="true"/> if a token was consumed; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// The method computes the elapsed seconds using
        /// <see cref="System.Diagnostics.Stopwatch.Frequency"/> and updates the internal token count,
        /// capping it at <paramref name="capacity"/>. If at least one token is available after refill,
        /// it decrements the count and succeeds.
        /// </remarks>
        /// <seealso cref="System.Diagnostics.Stopwatch.GetTimestamp"/>
        /// <seealso cref="System.Diagnostics.Stopwatch.Frequency"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryTake(long nowTick, int capacity, double refillPerSecond)
        {
            var elapsed = (nowTick - _lastTick) / (double)Stopwatch.Frequency;
            if (elapsed > 0)
            {
                _tokens = Math.Min(capacity, _tokens + elapsed * refillPerSecond);
                _lastTick = nowTick;
            }

            if (_tokens >= 1.0)
            {
                _tokens -= 1.0;
                return true;
            }

            return false;
        }
    }
}
