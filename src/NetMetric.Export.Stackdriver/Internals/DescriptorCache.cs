// <copyright file="DescriptorCache.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Google.Api;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.Monitoring.V3;

namespace NetMetric.Export.Stackdriver.Internals;

/// <summary>
/// Caches Google Cloud Monitoring (Stackdriver) <see cref="MetricDescriptor"/> creation
/// to avoid redundant <c>CreateMetricDescriptor</c> requests.
/// </summary>
/// <remarks>
/// <para>
/// When exporting custom metrics, Google Cloud Monitoring requires that a corresponding
/// <see cref="MetricDescriptor"/> exists for the metric type (for example,
/// <c>custom.googleapis.com/my_metric</c>). Creating the same descriptor repeatedly wastes
/// network calls and can trigger rate limits.
/// </para>
/// <para>
/// <see cref="DescriptorCache"/> maintains an in-memory set of metric types that have been
/// confirmed to exist (either pre-existing or successfully created). Once a type is marked as
/// created, subsequent calls to <see cref="EnsureAsync(string, MetricDescriptor.Types.MetricKind, MetricDescriptor.Types.ValueType, string?, string?, CancellationToken)"/>
/// are no-ops and do not perform network I/O.
/// </para>
/// <para>
/// Thread safety: the cache is process-local and is safe for concurrent use across multiple
/// threads. Internally, it uses <see cref="ConcurrentDictionary{TKey,TValue}"/> with an
/// ordinal string comparer.
/// </para>
/// <para>
/// Scope: this cache prevents duplicate descriptor creation <em>within a single process</em>.
/// It does not coordinate across multiple hosts. If you run multiple exporters in parallel
/// (e.g., many pods), Stackdriver may still return <c>AlreadyExists</c> to one caller; this
/// class treats that as success and records the metric type as created.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var client = await MetricServiceClient.CreateAsync();
/// var cache = new DescriptorCache(client, projectId: "my-project");
///
/// // Ensure a gauge descriptor exists for a double-valued custom metric.
/// await cache.EnsureAsync(
///     metricType: "custom.googleapis.com/netmetric/exporter/scrape_duration_seconds",
///     kind: MetricDescriptor.Types.MetricKind.Gauge,
///     valueType: MetricDescriptor.Types.ValueType.Double,
///     unit: "s",
///     desc: "Time spent serving a Prometheus scrape.",
///     ct: CancellationToken.None);
/// ]]></code>
/// </example>
internal sealed class DescriptorCache
{
    private readonly MetricServiceClient _client;
    private readonly ProjectName _project;

    /// <summary>
    /// Tracks metric types that have been observed or created successfully.
    /// The value is unused; the presence of the key indicates success.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _created =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="DescriptorCache"/> class.
    /// </summary>
    /// <param name="client">The Google Cloud Monitoring client used for descriptor lookups and creation.</param>
    /// <param name="projectId">The Google Cloud project ID that owns the metric descriptors.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="client"/> or <paramref name="projectId"/> is <see langword="null"/>.
    /// </exception>
    public DescriptorCache(MetricServiceClient client, string projectId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _project = ProjectName.FromProject(projectId ?? throw new ArgumentNullException(nameof(projectId)));
    }

    /// <summary>
    /// Ensures that a <see cref="MetricDescriptor"/> exists for the specified <paramref name="metricType"/>.
    /// </summary>
    /// <param name="metricType">
    /// The fully qualified metric type, for example <c>custom.googleapis.com/my_metric</c>.
    /// </param>
    /// <param name="kind">The metric kind (e.g., <see cref="MetricDescriptor.Types.MetricKind.Gauge"/>).</param>
    /// <param name="valueType">The value type (e.g., <see cref="MetricDescriptor.Types.ValueType.Double"/>).</param>
    /// <param name="unit">The UCUM unit string (e.g., <c>s</c>, <c>By</c>), or <see langword="null"/> if none.</param>
    /// <param name="desc">An optional human-readable description of the metric.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// A task that completes when the descriptor is confirmed to exist, either by retrieval
    /// or successful creation. If the metric type is already cached as created, the method
    /// returns immediately without performing network I/O.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Behavior:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>If the metric type is already cached, the method performs no network calls.</description>
    ///   </item>
    ///   <item>
    ///     <description>If <c>GetMetricDescriptor</c> succeeds, the type is cached and the method returns.</description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     If retrieval fails because the descriptor is missing, the method attempts to create it.
    ///     Concurrent <c>AlreadyExists</c> races from other processes are treated as success.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    /// <exception cref="Grpc.Core.RpcException">
    /// Propagated if the underlying Google Cloud Monitoring API calls fail with an error
    /// other than <c>AlreadyExists</c> during creation, or with an error other than <c>NotFound</c>
    /// during retrieval.
    /// </exception>
    public async Task EnsureAsync(
        string metricType,
        MetricDescriptor.Types.MetricKind kind,
        MetricDescriptor.Types.ValueType valueType,
        string? unit,
        string? desc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(metricType);

        // Fast path: if we've already confirmed creation in this process, exit.
        if (_created.ContainsKey(metricType)) return;

        try
        {
            // Attempt to fetch the descriptor. If found, cache and return.
            await _client.GetMetricDescriptorAsync(new GetMetricDescriptorRequest
            {
                Name = MetricDescriptorName.FromProjectMetricDescriptor(_project.ProjectId, metricType).ToString()
            }, ct).ConfigureAwait(false);

            _created[metricType] = true;
            return;
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            // Descriptor does not exist; fall through to create it.
        }

        // Create a descriptor with the supplied properties.
        var md = new MetricDescriptor
        {
            Type = metricType,
            MetricKind = kind,
            ValueType = valueType,
            Description = desc ?? string.Empty,
            Unit = unit ?? string.Empty
        };

        try
        {
            await _client.CreateMetricDescriptorAsync(_project, md, cancellationToken: ct)
                         .ConfigureAwait(false);
            _created[metricType] = true;
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.AlreadyExists)
        {
            // Another process or thread created it concurrently; treat as success.
            _created[metricType] = true;
        }
    }
}
