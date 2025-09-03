// <copyright file="MetricTimerSink.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Timer.Adapters;

/// <summary>
/// Adapter that writes durations into a provided <see cref="ITimerMetric"/> for each unique metric <c>id</c>.
/// This implementation uses a resolver to obtain the <see cref="ITimerMetric"/> based on the <c>(id, name)</c> pair.
/// <para><b>Note:</b> This sink does not utilize <c>tags</c>. If you need to store tags, supply a metric implementation
/// that supports them.</para>
/// </summary>
public sealed class MetricTimerSink : ITimerSink
{
    private readonly Func<string, string, ITimerMetric> _metricResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricTimerSink"/> class.
    /// </summary>
    /// <param name="metricResolver">A delegate that resolves an <see cref="ITimerMetric"/> based on the <c>(id, name)</c> pair.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metricResolver"/> is null.</exception>
    /// <remarks>
    /// This constructor allows the user to provide a custom resolution function for obtaining the <see cref="ITimerMetric"/>
    /// instance associated with a specific <c>id</c> and <c>name</c>. The <paramref name="metricResolver"/> is called
    /// each time a metric is recorded.
    /// </remarks>
    public MetricTimerSink(Func<string, string, ITimerMetric> metricResolver)
        => _metricResolver = metricResolver ?? throw new ArgumentNullException(nameof(metricResolver));

    /// <inheritdoc />
    /// <summary>
    /// Records a timing measurement for a specific metric identified by <paramref name="id"/> and <paramref name="name"/>.
    /// </summary>
    /// <param name="id">The unique identifier for the metric.</param>
    /// <param name="name">The name of the metric.</param>
    /// <param name="elapsedMs">The elapsed time in milliseconds to record.</param>
    /// <param name="tags">Optional tags associated with the metric (not used by this implementation).</param>
    /// <remarks>
    /// This method uses the <see cref="_metricResolver"/> to resolve the appropriate <see cref="ITimerMetric"/> instance
    /// and then records the elapsed time using the appropriate method of the resolved metric.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(string id, string name, double elapsedMs, IReadOnlyDictionary<string, string>? tags = null)
        => _metricResolver(id, name).RecordMilliseconds(elapsedMs);
}
