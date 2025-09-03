// <copyright file="KafkaStatisticsSink.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Kafka.Statistics;

/// <summary>
/// Thread-safe, lock-free sink that retains the most recent Kafka client
/// statistics payload as raw JSON together with its capture time (UTC).
/// </summary>
/// <remarks>
/// <para>
/// This type is intended to bridge vendor/client callbacks that periodically emit
/// statistics (e.g., Confluent.Kafka <c>Statistics</c> events) and downstream
/// collectors that need to read the latest snapshot on demand.
/// </para>
/// <para>
/// Internally, it keeps <em>only one</em> snapshot—the latest JSON string—plus its
/// timestamp. Writes and reads use <see cref="Volatile"/> operations to ensure
/// memory visibility across threads without locks.
/// </para>
/// <para>
/// Typical integration flow:
/// </para>
/// <list type="number">
///   <item>
///     <description>Register a single <see cref="KafkaStatisticsSink"/> instance with the same lifetime as the Kafka client.</description>
///   </item>
///   <item>
///     <description>From the client's stats callback, call <see cref="Handle(string)"/> with the raw JSON payload.</description>
///   </item>
///   <item>
///     <description>From your metric collector, call <see cref="TryGet(out string, out DateTime)"/> to retrieve the latest snapshot, if any.</description>
///   </item>
/// </list>
/// </remarks>
/// <threadsafety>
/// <para>
/// All members are safe for concurrent use by multiple threads. The implementation
/// uses <see cref="Volatile.Read{T}(ref readonly T)"/> and <see cref="Volatile.Write{T}"/>
/// to publish and consume updates without explicit locks.
/// </para>
/// </threadsafety>
/// <example>
/// The following example wires the sink to a Kafka client's statistics callback and
/// later reads the most recent snapshot to forward it to a metrics pipeline:
/// <code>
/// var sink = new KafkaStatisticsSink();
///
/// // Example: from a Kafka client callback (executed on a client thread)
/// void OnStatistics(string json)
/// {
///     sink.Handle(json);
/// }
///
/// // Elsewhere: read and use the latest snapshot
/// if (sink.TryGet(out var json, out var capturedUtc))
/// {
///     Console.WriteLine($"Kafka stats captured at {capturedUtc:o}");
///     // Forward json to a parser / collector...
/// }
/// else
/// {
///     Console.WriteLine("No statistics available yet.");
/// }
/// </code>
/// </example>
/// <seealso cref="IKafkaStatsSource"/>
/// <seealso cref="NetMetric.Kafka.Adapters.ConfluentKafkaStatsSource"/>
public sealed class KafkaStatisticsSink
{
    private string? _lastJson;
    private long _lastTicksUtc;

    /// <summary>
    /// Publishes a new statistics snapshot and records the capture time in UTC.
    /// </summary>
    /// <param name="json">The raw JSON payload emitted by the Kafka client.</param>
    /// <remarks>
    /// <para>
    /// This operation replaces any previously stored snapshot. The capture
    /// timestamp is obtained from <see cref="DateTime.UtcNow"/>.
    /// </para>
    /// <para>
    /// Passing an empty string is allowed and will be stored as-is; however,
    /// a <see langword="null"/> value is rejected and results in an exception.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="json"/> is <see langword="null"/>.
    /// </exception>
    public void Handle(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        Volatile.Write(ref _lastJson, json);
        Volatile.Write(ref _lastTicksUtc, DateTime.UtcNow.Ticks);
    }

    /// <summary>
    /// Attempts to retrieve the latest statistics snapshot and its capture time.
    /// </summary>
    /// <param name="json">
    /// When the method returns <see langword="true"/>, contains the JSON payload of the most recent snapshot;
    /// otherwise, an empty string.
    /// </param>
    /// <param name="utc">
    /// When the method returns <see langword="true"/>, contains the UTC time at which the snapshot was recorded;
    /// otherwise, <see cref="DateTime.MinValue"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a snapshot has been published via <see cref="Handle(string)"/> and was retrieved successfully;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// If no statistics have been handled yet, the method returns <see langword="false"/> and outputs default values.
    /// </para>
    /// <para>
    /// The read is performed using <see cref="Volatile"/> to ensure a consistent view of the last published snapshot.
    /// </para>
    /// </remarks>
    public bool TryGet(out string json, out DateTime utc)
    {
        json = Volatile.Read(ref _lastJson) ?? string.Empty;
        var t = Volatile.Read(ref _lastTicksUtc);
        utc = t == 0 ? default : new DateTime(t, DateTimeKind.Utc);
        return t != 0 && !string.IsNullOrEmpty(json);
    }
}
