// <copyright file="PrometheusTextExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics.CodeAnalysis;

namespace NetMetric.Export.Prometheus.Exporters;

/// <summary>
/// Exports NetMetric metrics in Prometheus text-based exposition format.
/// </summary>
/// <remarks>
/// <para>
/// This exporter writes metrics to a <see cref="System.IO.TextWriter"/> obtained from a
/// user-provided factory delegate. It is suitable for serving HTTP responses at a
/// <c>/metrics</c> endpoint to be scraped by Prometheus.
/// </para>
/// <para>
/// Each call to
/// <see cref="ExportAsync(System.Collections.Generic.IEnumerable{NetMetric.Abstractions.IMetric}, System.Threading.CancellationToken)"/>
/// creates a fresh <see cref="System.IO.TextWriter"/> from the configured factory, writes a complete
/// snapshot, and then disposes the writer when the export completes.
/// </para>
/// <para>
/// <strong>Trimming / Reflection:</strong> The underlying <c>PrometheusFormatter</c> uses reflection
/// to read metric members. When IL trimming is enabled, public properties and methods of your
/// metric implementations might be removed unless preserved via
/// <see cref="DynamicallyAccessedMembersAttribute"/>,
/// <see cref="DynamicDependencyAttribute"/>, or a linker descriptor (XML). See the method remarks for guidance.
/// </para>
/// <example>
/// Simple usage with an HTTP response writer:
/// <code>
/// var exporter = new PrometheusTextExporter(
///     writerFactory: () => new StreamWriter(httpContext.Response.Body, leaveOpen: true),
///     options: new PrometheusExporterOptions());
///
/// await exporter.ExportAsync(myMetrics, httpContext.RequestAborted);
/// </code>
/// </example>
/// <example>
/// Collecting into a string (for tests or diagnostics):
/// <code>
/// var sb = new StringBuilder();
/// var exporter = new PrometheusTextExporter(
///     writerFactory: () => new StringWriter(sb),
///     options: new PrometheusExporterOptions());
///
/// await exporter.ExportAsync(myMetrics);
/// var payload = sb.ToString();
/// </code>
/// </example>
/// </remarks>
public sealed class PrometheusTextExporter : IMetricExporter
{
    private readonly Func<System.IO.TextWriter> _writerFactory;
    private readonly PrometheusExporterOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrometheusTextExporter"/> class.
    /// </summary>
    /// <param name="writerFactory">
    /// A factory that creates the <see cref="System.IO.TextWriter"/> used for output
    /// (for example, <see cref="System.IO.StringWriter"/>, <see cref="System.IO.StreamWriter"/>,
    /// or an HTTP response writer).
    /// </param>
    /// <param name="options">Exporter configuration options.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown if <paramref name="writerFactory"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public PrometheusTextExporter(Func<System.IO.TextWriter> writerFactory, PrometheusExporterOptions options)
    {
        _writerFactory = writerFactory ?? throw new ArgumentNullException(nameof(writerFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Exports the provided metrics in Prometheus text format using the configured <see cref="System.IO.TextWriter"/>.
    /// </summary>
    /// <param name="metrics">The sequence of metrics to export.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when all metrics have been written.</returns>
    /// <remarks>
    /// <para>
    /// This method calls into <c>PrometheusFormatter</c>, which uses reflection to access members of metric types.
    /// When IL trimming is enabled, ensure the public properties and methods of your metric implementations
    /// are preserved. You can do this by:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     Decorating your metric types (or the element type of the <paramref name="metrics"/> sequence) with
    ///     <see cref="DynamicallyAccessedMembersAttribute"/> for
    ///     <see cref="DynamicallyAccessedMemberTypes.PublicProperties"/> and
    ///     <see cref="DynamicallyAccessedMemberTypes.PublicMethods"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Adding <see cref="DynamicDependencyAttribute"/> entries for known metric types to preserve required members.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Providing a linker descriptor (XML) that preserves public properties and methods for the namespace
    ///     containing your metric types.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    [RequiresUnreferencedCode(
        "PrometheusFormatter uses reflection. When trimming is enabled, public properties and methods of " +
        "metric implementations must be preserved (e.g., via DynamicallyAccessedMembers, DynamicDependency, or a linker descriptor).")]
    public async Task ExportAsync(
        System.Collections.Generic.IEnumerable<NetMetric.Abstractions.IMetric> metrics,
        System.Threading.CancellationToken ct = default)
    {
#pragma warning disable CA2007
        await using var writer = _writerFactory.Invoke();
#pragma warning restore CA2007

        var formatter = new PrometheusFormatter(writer, _options);
        await formatter.WriteAsync(metrics, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Optional generic overload that provides the trimmer with preservation hints for the metric element type.
    /// </summary>
    /// <typeparam name="TMetric">
    /// The concrete metric type. Applying <see cref="DynamicallyAccessedMembersAttribute"/> here
    /// ensures its public properties and methods are preserved when trimming is enabled.
    /// </typeparam>
    /// <param name="metrics">The sequence of metrics to export.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when all metrics have been written.</returns>
    /// <remarks>
    /// <para>
    /// This overload forwards to the non-generic implementation after casting to
    /// <see cref="NetMetric.Abstractions.IMetric"/>. Use it when you want compile-time flow of
    /// <see cref="DynamicallyAccessedMembersAttribute"/> to the element type.
    /// </para>
    /// <example>
    /// <code>
    /// await exporter.ExportAsync&lt;MyCounterMetric&gt;(metrics, ct);
    /// </code>
    /// </example>
    /// </remarks>
    [RequiresUnreferencedCode(
        "PrometheusFormatter uses reflection. When trimming is enabled, public properties and methods of metric implementations must be preserved.")]
    public Task ExportAsync<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.PublicMethods)]
    TMetric>(
        System.Collections.Generic.IEnumerable<TMetric> metrics,
        System.Threading.CancellationToken ct = default)
        where TMetric : NetMetric.Abstractions.IMetric
        => ExportAsync(metrics.Cast<NetMetric.Abstractions.IMetric>(), ct);
}
