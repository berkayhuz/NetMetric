// <copyright file="ChannelCountCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NetMetric.RabbitMQ.Collectors;

/// <summary>
/// Collects basic RabbitMQ connection/channel information and publishes it as metrics.
/// </summary>
/// <remarks>
/// <para>
/// This collector queries a RabbitMQ connection to report:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><c>rabbitmq.channels.connection_open</c> — 1 if the underlying connection is open; otherwise 0.</description>
///   </item>
///   <item>
///     <description><c>rabbitmq.channels.channel_max</c> — the negotiated maximum number of channels allowed by the broker/connection.</description>
///   </item>
/// </list>
/// <para>
/// On cancellation or error, a status gauge <c>rabbitmq.channels.status</c> is emitted with value 0 and appropriate tags:
/// <list type="bullet">
///   <item><description><c>status=cancelled</c> when the operation was cancelled.</description></item>
///   <item><description><c>status=error</c> with a truncated <c>reason</c> tag when an exception occurs.</description></item>
/// </list>
/// </para>
/// <para>
/// The multi-gauge built for <c>rabbitmq.channels</c> is configured with <c>ResetOnGet=true</c>, ensuring values reflect
/// the latest snapshot per collection cycle.
/// </para>
/// <para><b>Thread-safety:</b> Instances are typically registered as singletons or scoped components. The collector makes no static state mutations and is safe to call concurrently, assuming the provided dependencies are thread-safe.</para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Registration (e.g., in DI composition root)
/// services.AddSingleton<IMetricCollector, ChannelCountCollector>();
///
/// // Periodic collection
/// var collector = provider.GetRequiredService<IMetricCollector>();
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
/// var metric = await collector.CollectAsync(cts.Token);
///
/// if (metric is not null)
/// {
///     // Export, observe, or forward the metric via your exporter
/// }
/// ]]></code>
/// </example>
public sealed class ChannelCountCollector : IMetricCollector
{
    private static readonly double[] DefaultQuantiles = new[] { 0.5, 0.9, 0.99 };

    private readonly IMetricFactory _factory;
    private readonly IRabbitMqConnectionProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelCountCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to construct gauges, histograms, and summaries.</param>
    /// <param name="provider">The RabbitMQ connection provider used to resolve or create a connection.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> or <paramref name="provider"/> is <see langword="null"/>.</exception>
    public ChannelCountCollector(IMetricFactory factory, IRabbitMqConnectionProvider provider)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

#pragma warning disable CA1031
    /// <summary>
    /// Collects the current RabbitMQ channel/connection snapshot and returns it as a metric.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// A multi-gauge metric with the following siblings:
    /// <list type="bullet">
    ///   <item><description><c>rabbitmq.channels.connection_open</c></description></item>
    ///   <item><description><c>rabbitmq.channels.channel_max</c></description></item>
    /// </list>
    /// If the operation is cancelled or an error occurs, a status gauge <c>rabbitmq.channels.status</c> is returned instead.
    /// </returns>
    /// <remarks>
    /// The resulting multi-gauge has <c>ResetOnGet</c> enabled to prevent value accumulation across scrapes.
    /// </remarks>
    /// <exception cref="OperationCanceledException">The operation was cancelled via <paramref name="ct"/>.</exception>
    public async Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        const string Id = "rabbitmq.channels";
        const string Name = "RabbitMQ Channel Info";

        try
        {
            ct.ThrowIfCancellationRequested();

            var mg = _factory
                .MultiGauge(Id, Name)
                .WithResetOnGet(true)
                .Build();

            var conn = await _provider.GetOrCreateConnectionAsync(ct).ConfigureAwait(false);

            // connection_open: 1 when connection is open; otherwise 0
            mg.AddSibling(
                $"{Id}.connection_open",
                "connection_open",
                conn.IsOpen ? 1 : 0,
                new Dictionary<string, string> { ["metric"] = "connection_open" });

            // channel_max: negotiated maximum channel count
            mg.AddSibling(
                $"{Id}.channel_max",
                "channel_max",
                conn.ChannelMax,
                new Dictionary<string, string> { ["metric"] = "channel_max" });

            return mg;
        }
        catch (OperationCanceledException)
        {
            var g = _factory
                .Gauge($"{Id}.status", "RabbitMQ Channel Status")
                .WithTag("status", "cancelled")
                .Build();

            g.SetValue(0);
            return g;
        }
        catch (Exception ex)
        {
            var g = _factory
                .Gauge($"{Id}.status", "RabbitMQ Channel Status")
                .WithTag("status", "error")
                .WithTag("reason", Short(ex.Message))
                .Build();

            g.SetValue(0);

            return g;
        }

        static string Short(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
    }
#pragma warning restore CA1031
    /// <summary>
    /// Creates a summary metric with optional quantiles.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="quantiles">
    /// The desired quantiles (for example, 0.5, 0.9, 0.99).
    /// If <see langword="null"/>, a default set is used (<c>0.5</c>, <c>0.9</c>, <c>0.99</c>).
    /// </param>
    /// <param name="tags">Optional key-value tags to attach to the metric.</param>
    /// <param name="resetOnGet">Whether to reset counters when the metric is retrieved by the exporter.</param>
    /// <returns>A built <see cref="ISummaryMetric"/>.</returns>
    /// <remarks>
    /// This is an explicit interface implementation used by the collection pipeline; callers typically access it through <see cref="IMetricCollector"/>.
    /// </remarks>
    ISummaryMetric IMetricCollector.CreateSummary(
        string id,
        string name,
        IEnumerable<double> quantiles,
        IReadOnlyDictionary<string, string>? tags,
        bool resetOnGet)
    {
        var q = quantiles is null
            ? DefaultQuantiles
            : quantiles as double[] ?? quantiles.ToArray();

        return _factory
            .Summary(id, name)
            .WithQuantiles(q)
            .Build();
    }

    /// <summary>
    /// Creates a bucket histogram metric with the specified upper bounds.
    /// </summary>
    /// <param name="id">The metric identifier.</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="bucketUpperBounds">
    /// The inclusive upper boundaries of the histogram buckets, ordered ascending.
    /// If <see langword="null"/>, an empty set of bounds is used.
    /// </param>
    /// <param name="tags">Optional key-value tags to attach to the metric.</param>
    /// <returns>A built <see cref="IBucketHistogramMetric"/>.</returns>
    /// <remarks>
    /// This is an explicit interface implementation used by the collection pipeline; callers typically access it through <see cref="IMetricCollector"/>.
    /// </remarks>
    IBucketHistogramMetric IMetricCollector.CreateBucketHistogram(
        string id,
        string name,
        IEnumerable<double> bucketUpperBounds,
        IReadOnlyDictionary<string, string>? tags)
    {
        return _factory
            .Histogram(id, name)
            .WithBounds(bucketUpperBounds?.ToArray() ?? Array.Empty<double>())
            .Build();
    }
}
