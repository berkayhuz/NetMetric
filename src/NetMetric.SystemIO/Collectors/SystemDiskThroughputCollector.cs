// <copyright file="SystemDiskThroughputCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SystemIO.Collectors;

internal sealed class SystemDiskThroughputCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = new[] { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;
    private readonly ISystemIoReader _reader;

    private DateTime _lastTs;
    private readonly Dictionary<string, (ulong r, ulong w)> _last = new(StringComparer.Ordinal);

    private readonly string _id = "system.io.disk.throughput";
    private readonly string _name = "Disk IO throughput (B/s)";

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemDiskThroughputCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metric instances.</param>
    /// <param name="reader">The reader used to collect system I/O data.</param>
    public SystemDiskThroughputCollector(IMetricFactory factory, ISystemIoReader reader)
    {
        _factory = factory;
        _reader = reader;
    }

    /// <summary>
    /// Collects system disk I/O throughput metrics asynchronously.
    /// </summary>
    /// <param name="ct">The cancellation token to cancel the operation if requested.</param>
    /// <returns>A task that represents the asynchronous operation, with the result being an <see cref="IMetric"/> instance containing the throughput metrics.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled.</exception>
    /// <exception cref="Exception">Thrown when an unexpected error occurs during the collection process.</exception>
    public Task<IMetric?> CollectAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            // Create a multi-gauge metric
            var mg = _factory
                .MultiGauge(_id, _name)
                .WithResetOnGet(true)
                .WithUnit("B/s")
                .WithDescription("Bytes per second")
                .Build();

            // Read the current device I/O data
            var res = _reader.TryReadDevices();

            if (res is not { } cur)
            {
                mg.SetValue(0, new Dictionary<string, string> { { "status", "unavailable" } });
                return Task.FromResult<IMetric?>(mg);
            }

            var (ts, devices) = cur;

            // Calculate throughput if previous timestamp exists and is newer
            if (_lastTs != default && ts > _lastTs)
            {
                var dt = (ts - _lastTs).TotalSeconds;
                dt = Math.Max(dt, 1e-9);

                foreach (var d in devices)
                {
                    _last.TryGetValue(d.Device, out var prev);
                    double rbps = (d.ReadBytes - prev.r) / dt;
                    double wbps = (d.WriteBytes - prev.w) / dt;

                    var tagsRead = new Dictionary<string, string> { { "device", d.Device }, { "dir", "read" }, { "status", "ok" } };
                    var tagsWrite = new Dictionary<string, string> { { "device", d.Device }, { "dir", "write" }, { "status", "ok" } };

                    mg.AddSibling(_id + ".read", _name + " (read)", rbps, tagsRead);
                    mg.AddSibling(_id + ".write", _name + " (write)", wbps, tagsWrite);

                    _last[d.Device] = (d.ReadBytes, d.WriteBytes);
                }
            }
            else
            {
                // Warm-up: cache counters and calculate delta in the next cycle
                foreach (var d in devices)
                {
                    _last[d.Device] = (d.ReadBytes, d.WriteBytes);
                }

                mg.SetValue(0, new Dictionary<string, string> { { "status", "warmup" } });
            }

            _lastTs = ts;

            return Task.FromResult<IMetric?>(mg);
        }
        catch (OperationCanceledException)
        {
            var mg = _factory.MultiGauge(_id, _name).WithResetOnGet(true).Build();
            mg.SetValue(0, new Dictionary<string, string> { { "status", "cancelled" } });
            return Task.FromResult<IMetric?>(mg);
        }
        catch (IOException ioEx)
        {
            var mg = _factory.MultiGauge(_id, _name).WithResetOnGet(true).Build();
            mg.SetValue(0, new Dictionary<string, string> { { "status", "error" }, { "error", ioEx.GetType().Name } });
            return Task.FromResult<IMetric?>(mg);
        }
        catch (ObjectDisposedException ode)
        {
            var mg = _factory.MultiGauge(_id, _name).WithResetOnGet(true).Build();
            mg.SetValue(0, new Dictionary<string, string> { { "status", "error" }, { "error", ode.GetType().Name } });
            return Task.FromResult<IMetric?>(mg);
        }
        catch (PlatformNotSupportedException pnse)
        {
            var mg = _factory.MultiGauge(_id, _name).WithResetOnGet(true).Build();
            mg.SetValue(0, new Dictionary<string, string> { { "status", "unsupported" }, { "error", pnse.GetType().Name } });
            return Task.FromResult<IMetric?>(mg);
        }
    }

    /// <summary>
    /// Creates a summary metric based on the provided quantiles and tags.
    /// </summary>
    /// <param name="id">The metric ID.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="quantiles">Requested quantiles.</param>
    /// <param name="tags">Optional tags.</param>
    /// <param name="resetOnGet">Reset-on-get flag.</param>
    public ISummaryMetric CreateSummary(string id, string name, IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
        => _factory
            .Summary(id, name)
            .WithQuantiles(quantiles is null ? DefaultQuantiles : quantiles as double[] ?? quantiles.ToArray())
            .Build();


    /// <summary>
    /// Creates a bucket histogram metric based on the provided bucket bounds and tags.
    /// </summary>
    /// <param name="id">The metric ID.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="bucketUpperBounds">Upper bounds for buckets.</param>
    /// <param name="tags">Optional tags.</param>
    public IBucketHistogramMetric CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
        => _factory
            .Histogram(id, name)
            .WithBounds(bucketUpperBounds is null ? Array.Empty<double>() : bucketUpperBounds as double[] ?? bucketUpperBounds.ToArray())
            .Build();
}
