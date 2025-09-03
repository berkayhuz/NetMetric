// <copyright file="TimeMeasure.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Timer.Core;

/// <summary>
/// A lightweight handle that records elapsed time to a sink upon <see cref="Dispose"/>.
/// This is designed to be used with the <c>using</c> statement for zero/low allocation timing.
/// </summary>
public readonly struct TimingScope : IDisposable, IEquatable<TimingScope>
{
    private readonly ITimerSink _sink;
    private readonly string _id;
    private readonly string _name;
    private readonly IReadOnlyDictionary<string, string>? _tags;
    private readonly long _startTs; // Stopwatch timestamp

    /// <summary>
    /// Initializes a new instance of the <see cref="TimingScope"/> struct.
    /// </summary>
    /// <param name="sink">The <see cref="ITimerSink"/> used to record the elapsed time.</param>
    /// <param name="id">The unique identifier for the timing metric.</param>
    /// <param name="name">The display name for the timing metric.</param>
    /// <param name="tags">Optional tags associated with the timing metric.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="sink"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="id"/> or <paramref name="name"/> is null or whitespace.</exception>
    /// <remarks>
    /// The <see cref="TimingScope"/> starts measuring time when instantiated and records the elapsed time when disposed.
    /// </remarks>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal TimingScope(ITimerSink sink, string id, string name, IReadOnlyDictionary<string, string>? tags)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Value cannot be null or whitespace.", nameof(id)) : id;
        _name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Value cannot be null or whitespace.", nameof(name)) : name;
        _tags = tags;
        _startTs = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Records the elapsed time in milliseconds to the sink when the <see cref="TimingScope"/> is disposed.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        var elapsed = Stopwatch.GetElapsedTime(_startTs);
        _sink.Record(_id, _name, elapsed.TotalMilliseconds, _tags);
    }

    /// <summary>
    /// Determines whether this instance is equal to another <see cref="TimingScope"/>.
    /// </summary>
    /// <param name="other">The other <see cref="TimingScope"/> to compare against.</param>
    /// <returns><c>true</c> if the two scopes are equal; otherwise, <c>false</c>.</returns>
    public bool Equals(TimingScope other)
        => ReferenceEquals(_sink, other._sink)
           && _startTs == other._startTs
           && string.Equals(_id, other._id, StringComparison.Ordinal)
           && string.Equals(_name, other._name, StringComparison.Ordinal);

    /// <summary>
    /// Determines whether this instance is equal to a specified object.
    /// </summary>
    /// <param name="obj">The object to compare with this instance.</param>
    /// <returns><c>true</c> if <paramref name="obj"/> is a <see cref="TimingScope"/> equal to this instance; otherwise, <c>false</c>.</returns>
    public override bool Equals(object? obj) => obj is TimingScope ts && Equals(ts);

    /// <summary>
    /// Returns a hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode()
        => HashCode.Combine(_sink, _startTs, _id, _name);

    /// <summary>
    /// Determines whether two <see cref="TimingScope"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance to compare.</param>
    /// <param name="right">The second instance to compare.</param>
    /// <returns><c>true</c> if the instances are equal; otherwise, <c>false</c>.</returns>
    public static bool operator ==(TimingScope left, TimingScope right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="TimingScope"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance to compare.</param>
    /// <param name="right">The second instance to compare.</param>
    /// <returns><c>true</c> if the instances are not equal; otherwise, <c>false</c>.</returns>
    public static bool operator !=(TimingScope left, TimingScope right) => !left.Equals(right);
}
