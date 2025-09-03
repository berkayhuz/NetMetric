// <copyright file="ProcessMemoryCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NetMetric.Memory.Collectors;

/// <summary>
/// Collects point-in-time memory usage metrics for the current process.
/// </summary>
/// <remarks>
/// <para>
/// This collector emits a multi-gauge with a consistent tag schema:
/// <c>kind</c> (e.g., <c>working_set</c>, <c>private</c>, <c>paged</c>, <c>virtual</c>, <c>managed_heap</c>),
/// <c>status</c> (e.g., <c>ok</c>, <c>error</c>, <c>cancelled</c>),
/// and <c>os</c> (e.g., <c>Windows</c>, <c>Linux</c>, <c>macOS</c>).
/// </para>
/// <para>
/// All values are reported in bytes. The collector performs no long-running work
/// and should be safe to call on a short interval (e.g., 5–15 seconds).
/// </para>
/// <para>
/// Error and cancellation paths also return a built multi-gauge with sentinel value <c>0</c>, allowing downstream
/// exporters to preserve the time series while inspecting status tags and optional error metadata (<c>error</c>, <c>reason</c>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var collector = new ProcessMemoryCollector(factory);
/// var metric = await collector.CollectAsync(cancellationToken);
/// // Export metric using your pipeline…
/// </code>
/// </example>
public sealed class ProcessMemoryCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessMemoryCollector"/> class.
    /// </summary>
    /// <param name="factory">Factory used to build metric instruments.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public ProcessMemoryCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Collects memory usage metrics for the current process as a multi-gauge.
    /// </summary>
    /// <param name="ct">Token used to observe cancellation requests.</param>
    /// <returns>
    /// A task that resolves to a built <see cref="IMetric"/> representing a multi-gauge instrument.
    /// The instrument contains one time series per <c>kind</c>: <c>working_set</c>, <c>private</c>, <c>paged</c>,
    /// <c>virtual</c>, and <c>managed_heap</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The method reads process memory counters via <see cref="Process.GetCurrentProcess"/> and
    /// managed heap usage via <see cref="GC.GetTotalMemory(bool)"/> with <c>forceFullCollection = false</c>.
    /// </para>
    /// <para>
    /// On <see cref="OperationCanceledException"/>, an instrument is still returned with a single series
    /// whose <c>kind</c> is <c>all</c> and <c>status</c> is <c>cancelled</c>.
    /// </para>
    /// <para>
    /// On unexpected exceptions, an instrument is returned with a single series where <c>status</c> is <c>error</c>,
    /// along with <c>error</c> and truncated <c>reason</c> tags to aid debugging.
    /// </para>
    /// </remarks>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        const string id = "mem.process.bytes";
        const string name = "Process Memory Usage (bytes)";

        try
        {
            ct.ThrowIfCancellationRequested();

            using var p = Process.GetCurrentProcess();

            var mg = _factory.MultiGauge(id, name)
                             .WithResetOnGet(true)
                             .Build();

            mg.SetValue(p.WorkingSet64, Tags("working_set"));
            mg.SetValue(p.PrivateMemorySize64, Tags("private"));
            mg.SetValue(p.PagedMemorySize64, Tags("paged"));
            mg.SetValue(p.VirtualMemorySize64, Tags("virtual"));
            mg.SetValue(GC.GetTotalMemory(forceFullCollection: false), Tags("managed_heap"));

            return Task.FromResult<IMetric?>(mg);
        }
        catch (OperationCanceledException)
        {
            var mg = _factory.MultiGauge(id, name).WithResetOnGet(true).Build();

            // Uniform tag schema on cancellation.
            mg.SetValue(0, Tags("all", status: "cancelled"));

            return Task.FromResult<IMetric?>(mg);
        }
        catch (Exception ex)
        {
            var mg = _factory.MultiGauge(id, name).WithResetOnGet(true).Build();

            // Preserve tag schema and add minimal error diagnostics.
            var tags = Tags("all", status: "error");
            tags["error"] = ex.GetType().Name;
            tags["reason"] = Short(ex.Message);
            mg.SetValue(0, tags);

            return Task.FromResult<IMetric?>(mg);

            throw;
        }
    }

    /// <summary>
    /// Detects the current operating system label for tagging.
    /// </summary>
    /// <returns>A short OS label such as <c>Windows</c>, <c>Linux</c>, or <c>macOS</c>.</returns>
    /// <remarks>
    /// Falls back to <see cref="RuntimeInformation.OSDescription"/> if the OS platform cannot be
    /// mapped to a canonical label.
    /// </remarks>
    private static string DetectOs() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" :
                                                              RuntimeInformation.OSDescription;

    /// <summary>
    /// Cached OS label to avoid repeated platform checks.
    /// </summary>
    private static readonly string Os = DetectOs();

    /// <summary>
    /// Creates a tag dictionary for a single time series in the multi-gauge.
    /// </summary>
    /// <param name="kind">
    /// The logical sub-series name, e.g., <c>working_set</c>, <c>private</c>, <c>paged</c>,
    /// <c>virtual</c>, or <c>managed_heap</c>.
    /// </param>
    /// <param name="status">
    /// The collection status. Defaults to <c>ok</c>. Other typical values are <c>error</c> and <c>cancelled</c>.
    /// </param>
    /// <param name="os">
    /// Optional OS label. If omitted, an OS label is auto-detected once per process and reused.
    /// </param>
    /// <returns>
    /// A new case-sensitive dictionary with keys <c>kind</c>, <c>status</c>, and <c>os</c>.
    /// </returns>
    /// <remarks>
    /// Tag keys are intended to be stable for downstream aggregators and exporters.
    /// </remarks>
    private static Dictionary<string, string> Tags(string kind, string status = "ok", string? os = null)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["status"] = status,
            ["os"] = os ?? Os
        };
    }

    /// <summary>
    /// Produces a bounded-length string suitable for tagging error reasons.
    /// </summary>
    /// <param name="s">Input string to truncate.</param>
    /// <returns>
    /// An empty string when <paramref name="s"/> is null or empty; otherwise
    /// the original value truncated to at most 160 characters.
    /// </returns>
    private static string Short(string s)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);

    /// <inheritdoc />
    ISummaryMetric IMetricCollector.CreateSummary(
        string id, string name, IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
    {
        var q = quantiles?.ToArray() ?? new[] { 0.5, 0.9, 0.99 };
        var sb = _factory.Summary(id, name).WithQuantiles(q);

        if (tags != null)
            foreach (var kv in tags) sb.WithTag(kv.Key, kv.Value);

        return sb.Build();
    }

    /// <inheritdoc />
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id, string name, IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
    {
        var bounds = bucketUpperBounds?.ToArray() ?? Array.Empty<double>();
        var hb = _factory.Histogram(id, name).WithBounds(bounds);

        if (tags != null)
            foreach (var kv in tags) hb.WithTag(kv.Key, kv.Value);

        return hb.Build();
    }
}
