// <copyright file="ProcessIoThroughputCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SystemIO.Collectors;

internal sealed class ProcessIoThroughputCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = new[] { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;
    private readonly IProcessIoReader _reader;

    private IoSnapshot? _last;

    private readonly string _id = "system.io.process.throughput";
    private readonly string _name = "Process IO throughput (B/s)";

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessIoThroughputCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to create metric instances.</param>
    /// <param name="reader">The reader used to collect process I/O data.</param>
    public ProcessIoThroughputCollector(IMetricFactory factory, IProcessIoReader reader)
    {
        _factory = factory;
        _reader = reader;
    }

    /// <summary>
    /// Collects process I/O throughput metrics asynchronously.
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

            // Read the current I/O data
            var snap = _reader.TryReadCurrent();

            if (snap is null)
            {
                mg.SetValue(0, new Dictionary<string, string> { { "status", "unavailable" } });

                return Task.FromResult<IMetric?>(mg);
            }

            // Calculate throughput if previous snapshot exists and is newer
            if (_last is IoSnapshot prev && prev.TsUtc < snap.Value.TsUtc)
            {
                var dt = (snap.Value.TsUtc - prev.TsUtc).TotalSeconds;

                dt = Math.Max(dt, 1e-9);

                double rbps = (snap.Value.ReadBytes - prev.ReadBytes) / dt;
                double wbps = (snap.Value.WriteBytes - prev.WriteBytes) / dt;

                mg.AddSibling(_id + ".read", _name + " (read)", rbps, new Dictionary<string, string> { { "dir", "read" }, { "status", "ok" } });
                mg.AddSibling(_id + ".write", _name + " (write)", wbps, new Dictionary<string, string> { { "dir", "write" }, { "status", "ok" } });
            }
            else
            {
                // Mark as warming up if the snapshot is not valid
                mg.SetValue(0, new Dictionary<string, string> { { "status", "warmup" } });
            }

            _last = snap;

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
