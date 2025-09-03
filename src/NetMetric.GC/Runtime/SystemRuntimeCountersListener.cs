// <copyright file="SystemRuntimeCountersListener.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics.Tracing;

namespace NetMetric.GC.Runtime;

/// <summary>
/// Listens to the "System.Runtime" EventCounters and stores the collected data in a lightweight ring buffer.
/// Provides GC-related metrics such as time-in-GC percentage, heap size, and GC collection counts for generations 0, 1, and 2.
/// </summary>
public sealed class SystemRuntimeCountersListener : EventListener, IRuntimeGcMetricsSource
{
    private readonly int _intervalSec;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemRuntimeCountersListener"/> class.
    /// </summary>
    /// <param name="intervalSec">The interval (in seconds) at which event counters are sampled. Default is 1 second.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="intervalSec"/> is less than 1.</exception>
    public SystemRuntimeCountersListener(int intervalSec = 1)
    {
        _intervalSec = Math.Max(1, intervalSec);
    }

    // Ring buffer for storing the last N samples
    private const int Capacity = 90;
    private readonly double[] _timeInGcPercent = new double[Capacity];
    private int _count;
    private int _writeIndex;
    private readonly object _lock = new();

    // Volatile fields for GC metrics with NaN sentinel values
    private double _heapBytes = double.NaN;
    private double _gen0 = double.NaN;
    private double _gen1 = double.NaN;
    private double _gen2 = double.NaN;

    /// <summary>
    /// Called when an event source is created. Enables event listening on the "System.Runtime" event source.
    /// </summary>
    /// <param name="eventSource">The event source being created.</param>
    protected override void OnEventSourceCreated(EventSource? eventSource)
    {
        ArgumentNullException.ThrowIfNull(eventSource);

        base.OnEventSourceCreated(eventSource);
        if (eventSource?.Name == "System.Runtime")
        {
            try
            {
                EnableEvents(
                    eventSource,
                    EventLevel.Informational,
                    EventKeywords.All,
                    new Dictionary<string, string?> { ["EventCounterIntervalSec"] = _intervalSec.ToString() }
                );
            }
            catch
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Called when an event is written. Processes event data related to GC metrics (time-in-GC, heap size, generation counts).
    /// </summary>
    /// <param name="eventData">The event data associated with the written event.</param>
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.EventName != "EventCounters" || eventData.Payload is null || eventData.Payload.Count == 0)
        {
            return;
        }

        if (eventData.Payload[0] is not IDictionary<string, object> payload)
        {
            return;
        }

        if (!payload.TryGetValue("Name", out var nameObj) || nameObj is null)
        {
            return;
        }

        var name = nameObj.ToString();

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        static double? ReadNumber(IDictionary<string, object> p, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (p.TryGetValue(k, out var v) && v is IConvertible c)
                {
                    try
                    {
                        return Convert.ToDouble(c);
                    }
                    catch
                    {
                        throw;
                    }
                }
            }

            return null;
        }

        // Process GC-related metrics from EventCounters
        switch (name)
        {
            case "time-in-gc":
                {
                    var percent = ReadNumber(payload, "Mean", "Value", "Increment");

                    if (percent.HasValue)
                    {
                        lock (_lock)
                        {
                            _timeInGcPercent[_writeIndex] = percent.Value;
                            _writeIndex = (_writeIndex + 1) % Capacity;
                            if (_count < Capacity)
                            {
                                _count++;
                            }
                        }
                    }
                }
                break;

            case "gc-heap-size":
                {
                    var hb = ReadNumber(payload, "Mean", "Value", "Increment");

                    if (hb.HasValue)
                    {
                        // Convert heap size from MB to bytes
                        var bytes = hb.Value * 1024 * 1024;

                        Volatile.Write(ref _heapBytes, bytes);
                    }
                }
                break;

            case "gen-0-gc-count":
                {
                    var v = ReadNumber(payload, "Mean", "Value", "Increment");

                    if (v.HasValue)
                    {
                        Volatile.Write(ref _gen0, v.Value);
                    }
                }
                break;

            case "gen-1-gc-count":
                {
                    var v = ReadNumber(payload, "Mean", "Value", "Increment");

                    if (v.HasValue)
                    {
                        Volatile.Write(ref _gen1, v.Value);
                    }
                }
                break;

            case "gen-2-gc-count":
                {
                    var v = ReadNumber(payload, "Mean", "Value", "Increment");

                    if (v.HasValue)
                    {
                        Volatile.Write(ref _gen2, v.Value);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Captures a snapshot of the time spent in GC as a percentage of total time.
    /// </summary>
    /// <returns>An array of GC time-in-percentage samples.</returns>
    public double[] SnapshotTimeInGcPercent()
    {
        lock (_lock)
        {
            if (_count == 0)
            {
                return Array.Empty<double>();
            }

            var arr = new double[_count];
            var start = (_writeIndex - _count + Capacity) % Capacity;

            for (int i = 0; i < _count; i++)
            {
                arr[i] = _timeInGcPercent[(start + i) % Capacity];
            }

            return arr;
        }
    }

    /// <summary>
    /// Retrieves the current heap size in bytes.
    /// </summary>
    /// <returns>The current heap size in bytes, or <c>null</c> if unavailable.</returns>
    public double? CurrentHeapBytes()
    {
        var v = Volatile.Read(ref _heapBytes);

        return double.IsNaN(v) ? (double?)null : v;
    }

    /// <summary>
    /// Retrieves the current GC collection counts for generations 0, 1, and 2.
    /// </summary>
    /// <returns>A tuple containing the GC collection counts for each generation (Gen0, Gen1, and Gen2).</returns>
    public (double? gen0, double? gen1, double? gen2) CurrentGenCounts()
    {
        var a = Volatile.Read(ref _gen0);
        var b = Volatile.Read(ref _gen1);
        var c = Volatile.Read(ref _gen2);

        return (
            double.IsNaN(a) ? (double?)null : a,
            double.IsNaN(b) ? (double?)null : b,
            double.IsNaN(c) ? (double?)null : c
        );
    }

    /// <summary>
    /// Disposes the listener and releases any resources used.
    /// </summary>
    public new void Dispose()
    {
        base.Dispose();

        System.GC.SuppressFinalize(this);
    }
}
