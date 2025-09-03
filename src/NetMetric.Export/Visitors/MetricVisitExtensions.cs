// <copyright file="MetricVisitExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.Visitors;

/// <summary>
/// Provides extension methods for visiting metric values using the
/// <see cref="IMetricValueVisitor"/> interface.
/// </summary>
/// <remarks>
/// This helper implements a lightweight <em>Visitor pattern</em> for metrics.
/// The extension methods resolve the runtime metric value and dispatch it to
/// the appropriate <c>Visit</c> overload on the supplied visitor instance.
/// </remarks>
/// <threadsafety>
/// The methods are stateless and thus thread-safe <em>provided</em> the supplied
/// <c>metric</c> and <c>visitor</c> instances are themselves thread-safe for concurrent use.
/// </threadsafety>
/// <example>
/// <para>Create a simple visitor that formats gauge and counter values:</para>
/// <code language="csharp"><![CDATA[
/// using NetMetric.Export.Visitors;
///
/// // A trivial visitor implementation (only two value kinds shown for brevity).
/// public sealed class FormattingVisitor : IMetricValueVisitor
/// {
///     public void Visit(GaugeValue value, IMetric metric)
///         => Console.WriteLine($"[GAUGE] {metric.Id}: {value.Value}");
///
///     public void Visit(CounterValue value, IMetric metric)
///         => Console.WriteLine($"[COUNTER] {metric.Id}: {value.Value}");
///
///     // Implement other Visit overloads as needed...
/// }
///
/// // Usage:
/// IMetric cpuLoad = GetCpuLoadMetric(); // returns an IMetric with a GaugeValue, for example
/// var visitor = new FormattingVisitor();
/// cpuLoad.Accept(visitor); // Dispatches to the appropriate Visit overload
/// ]]></code>
/// </example>
/// <seealso cref="IMetricValueVisitor"/>
/// <seealso cref="NetMetric.Export.Exporters.JsonLinesExporter"/>
public static class MetricVisitExtensions
{
    /// <summary>
    /// Accepts a generic <see cref="IMetricValueVisitor"/> and dispatches
    /// the underlying metric value to the correct <c>Visit</c> overload.
    /// </summary>
    /// <param name="metric">The metric instance containing the value to be visited.</param>
    /// <param name="visitor">The visitor that processes the metric value.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="metric"/> or <paramref name="visitor"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// The method inspects the result of <c>metric.GetValue()</c> and invokes the matching
    /// <c>Visit</c> method on the provided <paramref name="visitor"/>. If the runtime value type
    /// is not recognized, a fallback visit with an undefined gauge (<c>double.NaN</c>) is performed.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// IMetric metric = registry.Get("requests_total");
    /// IMetricValueVisitor aggregator = new AggregatingVisitor();
    /// metric.Accept(aggregator); // Calls the appropriate Visit overload
    /// ]]></code>
    /// </example>
    public static void Accept(this IMetric metric, IMetricValueVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(visitor);
        Dispatch(metric, visitor);
    }

    /// <summary>
    /// Accepts a specialized <see cref="NetMetric.Export.Exporters.JsonLinesExporter"/> visitor and dispatches
    /// the underlying metric value to the correct <c>Visit</c> overload.
    /// </summary>
    /// <param name="metric">The metric instance containing the value to be visited.</param>
    /// <param name="visitor">The JSON Lines exporter that processes the metric value.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="metric"/> or <paramref name="visitor"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// This overload exists as a minor performance convenience for a common exporter,
    /// avoiding interface dispatch in hot paths when using JSON Lines export. It behaves
    /// equivalently to the generic overload.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var exporter = new NetMetric.Export.Exporters.JsonLinesExporter(writer, NetMetricJsonContext.Default);
    /// foreach (var metric in metrics)
    /// {
    ///     metric.Accept(exporter); // Dispatches each value to the exporter Visit methods
    /// }
    /// ]]></code>
    /// </example>
    public static void Accept(this IMetric metric, NetMetric.Export.Exporters.JsonLinesExporter visitor)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(visitor);
        Dispatch(metric, visitor);
    }

    /// <summary>
    /// Dispatches the underlying metric value to the correct <c>Visit</c> method
    /// based on the runtime type of the value.
    /// </summary>
    /// <param name="metric">The metric instance to dispatch.</param>
    /// <param name="visitor">The visitor to which the metric value will be dispatched.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="metric"/> or <paramref name="visitor"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The following value kinds are recognized and routed to their corresponding
    /// <c>Visit</c> overload, if present on <paramref name="visitor"/>:
    /// <list type="bullet">
    /// <item><description><c>GaugeValue</c></description></item>
    /// <item><description><c>CounterValue</c></description></item>
    /// <item><description><c>DistributionValue</c></description></item>
    /// <item><description><c>SummaryValue</c></description></item>
    /// <item><description><c>BucketHistogramValue</c></description></item>
    /// <item><description><c>MultiSampleValue</c></description></item>
    /// <item><description><c>MetricValue</c> (base/default)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// If the runtime type is not recognized, a fallback dispatch occurs using a
    /// placeholder gauge value (<c>double.NaN</c>) to ensure the visitor is still invoked.
    /// </para>
    /// </remarks>
    private static void Dispatch(IMetric metric, IMetricValueVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(visitor);

        var val = metric.GetValue();
        switch (val)
        {
            case GaugeValue g: visitor.Visit(g, metric); break;
            case CounterValue c: visitor.Visit(c, metric); break;
            case DistributionValue d: visitor.Visit(d, metric); break;
            case SummaryValue s: visitor.Visit(s, metric); break;
            case BucketHistogramValue bh: visitor.Visit(bh, metric); break;
            case MultiSampleValue m: visitor.Visit(m, metric); break;
            case MetricValue mv: visitor.Visit(mv, metric); break;
            default: visitor.Visit(new GaugeValue(double.NaN), metric); break;
        }
    }
}
