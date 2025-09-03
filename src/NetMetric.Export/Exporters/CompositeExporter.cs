// <copyright file="CompositeExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Exporters;

/// <summary>
/// Forwards metrics to multiple underlying exporters in the specified order.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="CompositeExporter"/> when a single process must publish metrics to more
/// than one backend (for example, to expose a Prometheus scrape endpoint while also pushing
/// to Google Cloud Monitoring).
/// </para>
/// <para>
/// Export calls are executed <strong>sequentially</strong> in the order the exporters were
/// provided to the constructor. Each exporter is awaited before the next one runs to ensure
/// predictable ordering and to avoid overwhelming downstream systems.
/// </para>
/// <para>
/// Failure semantics:
/// if one exporter throws an exception, the method stops and the exception is propagated to
/// the caller; any exporters scheduled after the failing one will not run. If you need
/// fail-open behavior (i.e., always attempt all exporters), wrap each exporter with your own
/// error-handling adapter before constructing this composite.
/// </para>
/// <para>
/// Thread safety:
/// this type holds only an immutable snapshot of exporters and performs no shared mutable state
/// updates; it is safe to share across threads. However, the thread-safety of individual
/// exporters is implementation-specific.
/// </para>
/// <para>
/// Trimming/AOT:
/// some exporters may rely on reflection over metric types. When publishing a trimmed build,
/// ensure those exporters and the metric implementations preserve required members (for example,
/// by using <see cref="DynamicallyAccessedMembersAttribute"/> on public properties or linker
/// descriptors).
/// </para>
/// <example>
/// <code language="csharp"><![CDATA[
/// using NetMetric.Abstractions;
/// using NetMetric.Export.Exporters;
/// using NetMetric.Export.Prometheus.Exporters;
/// using NetMetric.Export.Stackdriver.Exporters;
///
/// IMetricExporter prometheus = new PrometheusExporter(/* options */);
/// IMetricExporter stackdriver = new StackdriverExporter(/* options */);
///
/// // Export to Prometheus first, then Stackdriver (order is preserved).
/// IMetricExporter exporter = new CompositeExporter(prometheus, stackdriver);
///
/// // Somewhere in your pipeline:
/// await exporter.ExportAsync(metrics, cancellationToken);
/// ]]></code>
/// </example>
/// </remarks>
/// <seealso cref="IMetricExporter"/>
public sealed class CompositeExporter : IMetricExporter
{
    private readonly IReadOnlyList<IMetricExporter> _exporters;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeExporter"/> class.
    /// </summary>
    /// <param name="exporters">
    /// The exporters to wrap, in the order they should be invoked.
    /// May be <see langword="null"/> or empty, in which case <see cref="ExportAsync(IEnumerable{IMetric}, CancellationToken)"/>
    /// becomes a no-op.
    /// </param>
    /// <remarks>
    /// The provided array is copied to an immutable snapshot to avoid later modification.
    /// </remarks>
    public CompositeExporter(params IMetricExporter[] exporters)
        => _exporters = exporters?.ToArray() ?? Array.Empty<IMetricExporter>();

    /// <summary>
    /// Exports the specified metrics by invoking each underlying exporter sequentially.
    /// </summary>
    /// <param name="metrics">The metrics to export. Must be a finite, forward-only enumerable.</param>
    /// <param name="ct">A token to observe cancellation.</param>
    /// <returns>
    /// A task that completes when all exporters have finished processing the metrics.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Exporters are awaited one by one in their configured order. If an exporter throws,
    /// the exception is propagated and remaining exporters are not invoked.
    /// </para>
    /// <para>
    /// The <paramref name="metrics"/> sequence may be enumerated multiple times if an underlying
    /// exporter buffers or re-reads values. To avoid surprises, pass a materialized collection
    /// (e.g., <see cref="List{T}"/> or array) rather than a single-use iterator.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is canceled.</exception>
    [RequiresUnreferencedCode("Some exporters reflect over metric types. When trimming is enabled, ensure public properties/methods of the concrete metric implementations are preserved (DynamicDependency/DynamicallyAccessedMembers or a linker descriptor).")]
    public async Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        foreach (var e in _exporters)
        {
            ct.ThrowIfCancellationRequested();
            await e.ExportAsync(metrics, ct).ConfigureAwait(false);
        }
    }
}
