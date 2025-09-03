// <copyright file="ConnectionHealthCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.RabbitMQ.Collectors;

/// <summary>
/// Collects and publishes the health of a RabbitMQ connection as a gauge metric.
/// </summary>
/// <remarks>
/// <para>
/// This collector produces a single <c>Gauge</c> metric under the id <c>rabbitmq.connection.health</c>.
/// The metric value is <c>1</c> when the underlying RabbitMQ connection is open, and <c>0</c> otherwise.
/// </para>
/// <para>
/// In failure scenarios, the metric is enriched with diagnostic tags to aid troubleshooting:
/// <list type="bullet">
///   <item><description><c>status</c>: <c>"error"</c> on unexpected exceptions, <c>"cancelled"</c> when an <see cref="OperationCanceledException"/> is observed.</description></item>
///   <item><description><c>reason</c>: a short, human-readable error message(truncated to 160 characters) when<c> status = "error" </c>.</description></item>
/// </list>
/// </para>
/// <para>
/// Typical usage is to register this collector in your metrics pipeline and invoke
/// <see cref="CollectAsync(System.Threading.CancellationToken)"/> on your emit/flush cadence.
/// The collector is lightweight and stateless between invocations.
/// </para>
/// <para><b>Thread safety:</b> All members are safe for concurrent use.</para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Registration (e.g., in DI composition root)
/// services.AddSingleton<IMetricCollector, ConnectionHealthCollector>();
///
/// // Emission loop
/// var metric = await connectionHealthCollector.CollectAsync(ct);
/// if (metric is not null)
/// {
///     await exporter.ExportAsync(metric, ct);
/// }
/// ]]></code>
/// </example>
public sealed class ConnectionHealthCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = new[] { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;
    private readonly IRabbitMqConnectionProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionHealthCollector"/> class.
    /// </summary>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to create metric instances.</param>
    /// <param name="provider">The <see cref="IRabbitMqConnectionProvider"/> that supplies RabbitMQ connections.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> or <paramref name="provider"/> is <see langword="null"/>.</exception>
    public ConnectionHealthCollector(IMetricFactory factory, IRabbitMqConnectionProvider provider)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Asynchronously samples the current RabbitMQ connection health and returns a gauge metric.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation.</param>
    /// <returns>
    /// An <see cref="IMetric"/> representing the current connection health, or <see langword="null"/> when no metric should be emitted.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The metric has the following shape:
    /// <list type="table">
    ///   <listheader>
    ///     <term>Id</term><description>rabbitmq.connection.health</description>
    ///   </listheader>
    ///   <item><term>Name</term><description>RabbitMQ Connection Health</description></item>
    ///   <item><term>Type</term><description>Gauge (double)</description></item>
    ///   <item><term>Value</term><description><c>1</c> (open) or <c>0</c> (closed/error)</description></item>
    ///   <item><term>Tags (optional)</term><description><c>status</c>, <c>reason</c></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// When a cancellation is requested, the returned metric includes <c>status="cancelled"</c> and a value of <c>0</c>.
    /// When an unexpected exception occurs, the metric includes <c>status="error"</c> and a truncated <c>reason</c>.
    /// </para>
    /// </remarks>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        const string Id = "rabbitmq.connection.health";
        const string Name = "RabbitMQ Connection Health";

        try
        {
            ct.ThrowIfCancellationRequested();

            var conn = await _provider.GetOrCreateConnectionAsync(ct).ConfigureAwait(false);
            var g = _factory.Gauge(Id, Name).Build();

            g.SetValue(conn.IsOpen ? 1 : 0);

            return g;
        }
        catch (OperationCanceledException)
        {
            var g = _factory.Gauge(Id, Name).WithTag("status", "cancelled").Build();

            g.SetValue(0);

            return g;
        }
        catch (Exception ex)
        {
            var g = _factory.Gauge(Id, Name).WithTag("status", "error").WithTag("reason", Short(ex.Message)).Build();

            g.SetValue(0);

            return g;

            // Note: unreachable 'throw;' retained to preserve original structure.
            throw;
        }

        static string Short(string s) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
    }

    /// <summary>
    /// Creates a summary metric configured with the specified quantiles.
    /// </summary>
    /// <param name="id">The identifier of the summary metric.</param>
    /// <param name="name">The display name of the summary metric.</param>
    /// <param name="quantiles">
    /// The quantile cutoffs to record. If <see langword="null"/>, defaults are used (<c>0.5</c>, <c>0.9</c>, <c>0.99</c>).
    /// </param>
    /// <param name="tags">Optional tags to associate with the metric instance.</param>
    /// <param name="resetOnGet">Whether the summary should reset its internal state upon retrieval.</param>
    /// <returns>A configured <see cref="ISummaryMetric"/> instance.</returns>
    /// <remarks>
    /// This is an explicit implementation of <see cref="IMetricCollector.CreateSummary(string, string, IEnumerable{double}, IReadOnlyDictionary{string, string}, bool)"/>.
    /// </remarks>
    ISummaryMetric IMetricCollector.CreateSummary(
        string id,
        string name,
        IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags,
        bool resetOnGet)
    {
        var q =
            quantiles is null ? DefaultQuantiles :
            quantiles as double[] ?? quantiles.ToArray();

        return _factory
            .Summary(id, name)
            .WithQuantiles(q)
            .Build();
    }

    /// <summary>
    /// Creates a histogram metric configured with the specified bucket upper bounds.
    /// </summary>
    /// <param name="id">The identifier of the histogram metric.</param>
    /// <param name="name">The display name of the histogram metric.</param>
    /// <param name="bucketUpperBounds">The inclusive upper bounds for each bucket.</param>
    /// <param name="tags">Optional tags to associate with the metric instance.</param>
    /// <returns>A configured <see cref="IBucketHistogramMetric"/> instance.</returns>
    /// <remarks>
    /// This is an explicit implementation of <see cref="IMetricCollector.CreateBucketHistogram(string, string, IEnumerable{double}, IReadOnlyDictionary{string, string})"/>.
    /// </remarks>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id,
        string name,
        IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
    {
        var bounds =
            bucketUpperBounds as double[] ??
            bucketUpperBounds?.ToArray() ??
            Array.Empty<double>();

        return _factory
            .Histogram(id, name)
            .WithBounds(bounds)
            .Build();
    }
}
