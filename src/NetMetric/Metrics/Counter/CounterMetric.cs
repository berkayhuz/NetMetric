// <copyright file="CounterMetric.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Metrics.Counter;

/// <summary>
/// A thread-safe counter metric that tracks how many times a specific event has occurred.
/// </summary>
/// <remarks>
/// <para>
/// Counters are <b>monotonic</b>: they can only increase over time.
/// Attempting to increment with a negative value will throw an <see cref="ArgumentException"/>.
/// </para>
/// <para>
/// Typical use cases include:
/// <list type="bullet">
///   <item><description>Number of HTTP requests received.</description></item>
///   <item><description>Number of exceptions thrown.</description></item>
///   <item><description>Number of messages processed.</description></item>
/// </list>
/// </para>
/// <para>
/// The counter is updated using atomic operations (<see cref="Interlocked"/>) and can be safely
/// incremented concurrently from multiple threads without additional synchronization.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Create a counter for processed messages
/// var counter = factory.Counter("queue.messages.processed", "Processed Messages")
///     .WithTag("queue", "orders")
///     .WithDescription("Number of messages successfully processed from the orders queue")
///     .Build();
///
/// // Increment the counter in worker code
/// counter.Increment();      // +1
/// counter.Increment(5);     // +5
///
/// // Retrieve snapshot
/// var snapshot = (CounterValue)counter.GetValue();
/// Console.WriteLine($"Processed: {snapshot.Value}"); 
/// </code>
/// </example>
public sealed class CounterMetric : MetricBase, ICounterMetric
{
    private long _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="CounterMetric"/> class.
    /// </summary>
    /// <param name="id">The unique metric identifier (e.g., <c>"http.server.requests"</c>).</param>
    /// <param name="name">The human-readable name of the metric (e.g., <c>"HTTP Requests"</c>).</param>
    /// <param name="tags">Optional dimension tags for this metric.</param>
    public CounterMetric(string id, string name, IReadOnlyDictionary<string, string>? tags = null)
        : base(id, name, InstrumentKind.Counter, tags) { }

    /// <summary>
    /// Increments the counter by the specified value.
    /// </summary>
    /// <param name="value">
    /// The amount to increment the counter by. Must be zero or positive.  
    /// Defaults to <c>1</c>.
    /// </param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is negative.</exception>
    /// <remarks>
    /// <para>
    /// This method uses <see cref="Interlocked.Add(ref long, long)"/> to ensure thread safety.  
    /// Multiple threads can call <see cref="Increment(long)"/> concurrently without contention.
    /// </para>
    /// </remarks>
    public void Increment(long value = 1)
    {
        if (value < 0)
            throw new ArgumentException("Increment value cannot be negative.", nameof(value));

        Interlocked.Add(ref _value, value);
    }

    /// <summary>
    /// Returns a snapshot of the current counter value.
    /// </summary>
    /// <returns>
    /// A <see cref="CounterValue"/> containing the total accumulated count since metric creation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Reading the counter does not reset it. Counters are cumulative for the process lifetime,
    /// unless explicitly disposed and re-created.
    /// </para>
    /// <para>
    /// The snapshot is obtained atomically using <see cref="System.Threading.Interlocked.Read(ref readonly long)"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var snapshot = (CounterValue)myCounter.GetValue();
    /// Console.WriteLine($"Total requests: {snapshot.Value}");
    /// </code>
    /// </example>
    public override object? GetValue() => new CounterValue(Interlocked.Read(ref _value));
}
