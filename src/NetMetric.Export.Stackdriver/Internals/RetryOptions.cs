// <copyright file="RetryOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Stackdriver.Internals;

/// <summary>
/// Configures exponential backoff and jitter for retrying transient write failures
/// when exporting time series to Google Cloud Monitoring (Stackdriver).
/// </summary>
/// <remarks>
/// <para>
/// These options are consumed by retry logic that performs <em>exponential backoff</em>
/// with optional <em>jitter</em> to avoid thundering-herd effects under partial outages.
/// The typical base delay for attempt <c>n</c> (1-based) is:
/// </para>
/// <code language="csharp"><![CDATA[
/// // n: retry attempt number (1-based)
/// // baseBackoff = InitialBackoffMs * 2^(n - 1)
/// // clampedBase  = Math.Min(baseBackoff, MaxBackoffMs)
/// // jitterFactor = 1 + uniform(-Jitter, +Jitter)
/// // finalDelayMs = (int)(clampedBase * jitterFactor)
/// ]]></code>
/// <para>
/// Consumers are expected to clamp negative or nonsensical values (e.g., negative delays,
/// jitter outside <c>[0.0, 1.0]</c>) to safe ranges before applying them. This type does not
/// enforce validation by throwing exceptions; it is a plain options contract.
/// </para>
/// <para>
/// A common strategy is to retry on transient transport or service errors
/// (e.g., gRPC <c>Unavailable</c> or <c>DeadlineExceeded</c>) up to <see cref="MaxAttempts"/>
/// while honoring cancellation tokens for shutdown responsiveness.
/// </para>
/// </remarks>
/// <example>
/// The following example computes a delay for a given attempt using <see cref="RetryOptions"/>:
/// <code language="csharp"><![CDATA[
/// using System;
///
/// static int ComputeDelayMs(int attempt, RetryOptions o, Random rng)
/// {
///     if (attempt < 1) attempt = 1;
///
///     // Exponential backoff with clamp
///     var baseMs = (double)o.InitialBackoffMs * Math.Pow(2, attempt - 1);
///     var clamped = Math.Min(baseMs, o.MaxBackoffMs);
///
///     // Jitter in [-Jitter, +Jitter]
///     var j = Math.Clamp(o.Jitter, 0.0, 1.0);
///     var delta = (rng.NextDouble() * 2.0 - 1.0) * j; // [-j, +j]
///     var delay = clamped * (1.0 + delta);
///
///     // Non-negative, integer milliseconds
///     return (int)Math.Max(0, Math.Round(delay));
/// }
///
/// var options = new RetryOptions
/// {
///     MaxAttempts = 5,
///     InitialBackoffMs = 500,
///     MaxBackoffMs = 8000,
///     Jitter = 0.2
/// };
///
/// // Example: compute delay for the 3rd attempt
/// int delayMs = ComputeDelayMs(3, options, new Random());
/// ]]></code>
/// </example>
/// <threadsafety>
/// This type is a simple data container and is not inherently thread-safe.
/// If instances are shared across threads, synchronize writes or configure it once
/// at startup and treat it as immutable thereafter.
/// </threadsafety>
public sealed class RetryOptions
{
    /// <summary>
    /// Gets or sets the maximum number of retry attempts for a failed write operation.
    /// </summary>
    /// <value>
    /// The total number of attempts after the initial failure (i.e., the number of retries).
    /// Defaults to <c>5</c>. Set to <c>0</c> to disable retries.
    /// </value>
    /// <remarks>
    /// Consumers typically stop retrying after this many attempts or sooner if a non-transient
    /// error is encountered or cancellation is requested.
    /// </remarks>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the initial backoff delay, in milliseconds, before the first retry.
    /// </summary>
    /// <value>
    /// Defaults to <c>500</c> ms. The delay doubles on each subsequent attempt
    /// until capped by <see cref="MaxBackoffMs"/>.
    /// </value>
    /// <remarks>
    /// Values less than zero should be treated by consumers as <c>0</c>.
    /// </remarks>
    public int InitialBackoffMs { get; set; } = 500;

    /// <summary>
    /// Gets or sets the maximum backoff delay, in milliseconds, for retries.
    /// </summary>
    /// <value>
    /// Defaults to <c>8000</c> ms. This is an upper bound on the exponentially
    /// increasing delay before jitter is applied.
    /// </value>
    /// <remarks>
    /// Use this to avoid unbounded growth of wait times, especially under prolonged outages.
    /// </remarks>
    public int MaxBackoffMs { get; set; } = 8000;

    /// <summary>
    /// Gets or sets the jitter factor used to randomize the computed backoff.
    /// </summary>
    /// <value>
    /// A value in the range <c>[0.0, 1.0]</c>; <c>0.2</c> means ±20% jitter
    /// (uniformly distributed) is applied to the clamped backoff. Defaults to <c>0.2</c>.
    /// </value>
    /// <remarks>
    /// <para>
    /// Jitter reduces synchronization across multiple clients that fail simultaneously.
    /// A value of <c>0.0</c> disables jitter (deterministic backoff).
    /// </para>
    /// <para>
    /// If a consumer observes values outside the recommended range, it should clamp them
    /// to maintain sane behavior.
    /// </para>
    /// </remarks>
    public double Jitter { get; set; } = 0.2;
}
