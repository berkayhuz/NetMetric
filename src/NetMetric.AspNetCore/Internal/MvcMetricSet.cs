// <copyright file="MvcMetricSet.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AspNetCore.Internal;

/// <summary>
/// Provides a set of ASP.NET Core MVC-related metrics, primarily for measuring
/// stage execution durations (e.g., Authorization, Resource, Action, Exception, Result).
/// </summary>
/// <remarks>
/// <para>
/// Instances of <see cref="MvcMetricSet"/> manage histograms created through an
/// <see cref="IMetricFactory"/> and cache them in an internal dictionary keyed by
/// the <c>(route, method, stage)</c> combination.
/// </para>
/// <para>
/// The created metrics include common HTTP dimensions such as <c>route</c>,
/// <c>method</c>, <c>scheme</c>, and <c>http.flavor</c>, as well as the specific MVC pipeline
/// <c>stage</c>. The histogram uses millisecond buckets as configured via
/// <see cref="AspNetCoreMetricOptions.DurationBucketsMs"/>.
/// </para>
/// <para><strong>Thread Safety:</strong> The type is safe for concurrent use. Metric instances are
/// cached via <see cref="ConcurrentDictionary{TKey,TValue}"/> to avoid repeated construction under load.
/// </para>
/// </remarks>
/// <seealso cref="MvcMetricNames"/>
/// <seealso cref="MvcStageNames"/>
/// <seealso cref="AspNetCoreMetricOptions"/>
/// <seealso cref="IMetricFactory"/>
public sealed class MvcMetricSet
{
    private readonly IMetricFactory _factory;
    private readonly AspNetCoreMetricOptions _opt;
    private readonly ConcurrentDictionary<string, IBucketHistogramMetric> _durations = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="MvcMetricSet"/> class.
    /// </summary>
    /// <param name="factory">The <see cref="IMetricFactory"/> used to create histogram metrics.</param>
    /// <param name="opt">The <see cref="AspNetCoreMetricOptions"/> containing configuration (e.g., histogram bounds).</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="factory"/> or <paramref name="opt"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The instance keeps references to <paramref name="factory"/> and <paramref name="opt"/> for the lifetime of this set.
    /// </remarks>
    public MvcMetricSet(IMetricFactory factory, AspNetCoreMetricOptions opt)
    {
        _factory = factory;
        _opt = opt;
    }

    /// <summary>
    /// Gets or creates a histogram metric for the given route, HTTP method, MVC stage, scheme, and protocol flavor.
    /// </summary>
    /// <param name="route">The normalized route template label (e.g., <c>/api/items/{id}</c>).</param>
    /// <param name="method">The HTTP method (e.g., <c>GET</c>, <c>POST</c>).</param>
    /// <param name="stage">The MVC pipeline stage (e.g., <c>MvcStageNames.Action</c>, <c>MvcStageNames.Authorization</c>).</param>
    /// <param name="scheme">The HTTP scheme (e.g., <c>http</c>, <c>https</c>).</param>
    /// <param name="flavor">The HTTP protocol flavor (e.g., <c>"1.1"</c>, <c>"2"</c>, <c>"3"</c>).</param>
    /// <returns>
    /// An <see cref="IBucketHistogramMetric"/> representing the elapsed time for the specified stage,
    /// configured with tags and bucket bounds.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The histogram is cached in a <see cref="ConcurrentDictionary{TKey,TValue}"/> under a key composed of
    /// <c>route</c>, <c>method</c>, and <c>stage</c>. Subsequent calls with the same dimensions return the
    /// cached metric instance.
    /// </para>
    /// <para>
    /// Tags applied include:
    /// <list type="bullet">
    ///   <item><description><see cref="TagKeys.Route"/> — normalized route template.</description></item>
    ///   <item><description><see cref="TagKeys.Method"/> — HTTP method.</description></item>
    ///   <item><description><see cref="TagKeys.Scheme"/> — HTTP scheme.</description></item>
    ///   <item><description><see cref="TagKeys.Flavor"/> — HTTP protocol flavor.</description></item>
    ///   <item><description><c>"stage"</c> — one of <see cref="MvcStageNames"/> constants.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Cardinality:</strong> This type does not enforce route cardinality limits on its own; it expects
    /// upstream callers to supply normalized and possibly limited route labels.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any of <paramref name="route"/>, <paramref name="method"/>, <paramref name="stage"/>,
    /// <paramref name="scheme"/>, or <paramref name="flavor"/> is <see langword="null"/>.
    /// </exception>
    public IBucketHistogramMetric GetOrCreate(string route, string method, string stage, string scheme, string flavor)
    {
        var limitedRoute = route;
        var key = $"{limitedRoute}|{method}|{stage}";

        return _durations.GetOrAdd(key, _ =>
            _factory.Histogram(MvcMetricNames.StageDuration, "ASP.NET Core stage duration (ms)")
                .WithUnit("ms")
                .WithDescription("Elapsed time in milliseconds for a specific ASP.NET Core stage")
                .WithTags(t =>
                {
                    t.Add(TagKeys.Route, limitedRoute);
                    t.Add(TagKeys.Method, method);
                    t.Add(TagKeys.Scheme, scheme);
                    t.Add(TagKeys.Flavor, flavor);
                    t.Add("stage", stage);
                })
                .WithBounds(_opt.DurationBucketsMs.ToArray())
                .Build());
    }
}
