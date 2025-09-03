// <copyright file="DnsProbeCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.DNS.Modules;

namespace NetMetric.DNS.Collectors;

/// <summary>
/// Collector that performs DNS resolution probes for a list of hostnames and tracks success and failure counts.
/// It also measures the duration of DNS resolution attempts, both in success and failure cases.
/// </summary>
/// <remarks>
/// This collector asynchronously resolves hostnames and categorizes the results as successes (resolved addresses) or failures (timeouts or errors).
/// It tracks the resolution duration in milliseconds using a timer metric and counts the number of successful and failed resolutions using counter metrics.
/// </remarks>
internal sealed class DnsProbeCollector : DnsCollectorBase
{
    private readonly ITimerMetric _latency;
    private readonly ICounterMetric _ok;
    private readonly ICounterMetric _err;

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsProbeCollector"/> class.
    /// </summary>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to create metrics for DNS probe results.</param>
    /// <param name="options">The configuration options for the DNS collector, including the hostnames to probe and the resolve timeout.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> or <paramref name="options"/> is <c>null</c>.</exception>
    public DnsProbeCollector(IMetricFactory factory, Options.DnsOptions options) : base(factory, options)
    {
        _latency = factory.Timer("dns.resolve.duration", "DNS Resolve Duration (ms)").WithHistogramCapacity(2048).Build();
        _ok = factory.Counter("dns.resolve.success", "DNS Resolve Success Count").Build();
        _err = factory.Counter("dns.resolve.failure", "DNS Resolve Failure Count").Build();
    }

    /// <summary>
    /// Asynchronously collects DNS probe data, including resolution success/failure counts and latency.
    /// </summary>
    /// <param name="ct">The cancellation token to cancel the operation if requested.</param>
    /// <returns>A task that represents the asynchronous operation. The result is an <see cref="IMetric"/> containing DNS probe metrics, primarily the latency metric.</returns>
    /// <remarks>
    /// This method performs DNS resolution for each configured hostname. It tracks the following metrics:
    /// - <see cref="_latency">Latency</see>: The duration of each DNS resolution attempt in milliseconds.
    /// - <see cref="_ok">Success count</see>: The number of successful DNS resolutions (hostnames resolved).
    /// - <see cref="_err">Failure count</see>: The number of failed DNS resolutions (timeouts or errors).
    /// The method uses a semaphore to limit the maximum number of concurrent DNS resolution tasks based on the <see cref="Options.DnsOptions.MaxConcurrency"/> setting.
    /// </remarks>
    public override async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        if (Options.ProbeHostnames.Count == 0)
            return _ok;

        using var sem = new SemaphoreSlim(Math.Max(1, Options.MaxConcurrency), Math.Max(1, Options.MaxConcurrency));
        var tasks = new List<Task>(Options.ProbeHostnames.Count);

        foreach (var host in Options.ProbeHostnames)
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    using var timer = _latency.Start(); // ITimerMetric → Start() scope
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                    cts.CancelAfter(Options.ResolveTimeout);

                    var addrs = await Dns.GetHostAddressesAsync(host, cts.Token).ConfigureAwait(false);

                    // Filter for IPv4 addresses if IPv6 is disabled
                    if (!Options.EnableIPv6)
                        addrs = Array.FindAll(addrs, a => a.AddressFamily == AddressFamily.InterNetwork);

                    // If any addresses are found, increment success counter
                    if (addrs.Length > 0)
                    {
                        _ok.Increment();
                    }
                    else
                    {
                        _err.Increment();
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {

                }
                catch
                {
                    _err.Increment();

                    throw;
                }
                finally
                {
                    sem.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Return the latency metric, which the manager expects to be the single IMetric result.
        return _latency;
    }
}
