// <copyright file="MetricOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Configuration options that manage various settings for metric collection.
/// This class contains important settings such as how metrics are collected,
/// parallel execution, sampling rate, and more.
/// </summary>
public sealed class MetricOptions
{
    /// <summary>
    /// Optional exporter responsible for exporting metrics.
    /// If not provided, only the registry snapshot will be taken.
    /// </summary>
    public IMetricExporter? Exporter
    {
        get; set;
    }

    /// <summary>
    /// A filter function applied to each collected metric.  
    /// The function is invoked for every metric and should return <c>true</c> 
    /// if the metric should be included.  
    /// If <c>null</c>, all metrics pass through.
    /// </summary>
    public Func<IMetric, bool>? MetricFilter
    {
        get; init;
    }

    /// <summary>
    /// Determines whether collectors within a module should be allowed to run in parallel.  
    /// By default, parallel execution is enabled.
    /// </summary>
    public bool EnableParallelCollectors { get; init; } = true;

    /// <summary>
    /// If a cancellation request (token) is received during collection/export,  
    /// the operation will be stopped.  
    /// By default, collection is cancelled when a token is active.
    /// </summary>
    public bool CancelOnToken { get; init; } = true;

    /// <summary>
    /// An optional random number generator used for sampling.  
    /// By default, the system's built-in random generator is used.  
    /// This can be customized for deterministic testing.
    /// </summary>
    public Func<double>? RandomNextDouble
    {
        get; init;
    }

    /// <summary>
    /// Optional delegate for logging informational messages.  
    /// Can be used to gather logs during collection.
    /// </summary>
    public Action<string>? LogInfo
    {
        get; init;
    }

    /// <summary>
    /// Optional delegate for logging errors.  
    /// Any error that occurs during collection will be logged here.
    /// </summary>
    public Action<string>? LogError
    {
        get; init;
    }
    /// <summary>
    /// Sampling rate. Must be a value between 0.0 and 1.0.  
    /// A value of 1.0 means all metrics will be collected,  
    /// while 0.0 means no metrics will be collected.  
    /// Default value: 1.0 (all metrics are collected).
    /// </summary>
    public double SamplingRate { get; init; } = 1.0;
    /// <summary>
    /// A fixed timeout (in milliseconds) applied to each collector.  
    /// If set to 0 or a negative value, there is no timeout.
    /// </summary>
    public int CollectorTimeoutMs { get; init; } = 750;

    /// <summary>
    /// The degree of parallelism for collectors within a module.  
    /// If <c>null</c>, the CPU core count will be used as the level of parallelism.
    /// </summary>
    public int? CollectorParallelism
    {
        get; init;
    }

    public IReadOnlyDictionary<string, string>? GlobalTags
    {
        get; init;
    }

    public ResourceAttributes? NmResource
    {
        get; init;
    }

    public int MaxTagKeyLength { get; init; } = 64;
    public int MaxTagValueLength { get; init; } = 256;
    public int? MaxTagsPerMetric { get; init; } = 64;

    public bool EnableSelfMetrics { get; init; } = true;
    public string? SelfMetricsPrefix { get; init; } = "netmetric";
}
