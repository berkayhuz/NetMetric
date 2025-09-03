// <copyright file="CloudWatchExporterOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.AWS.Options;

/// <summary>
/// Provides configuration options for exporting metrics to Amazon CloudWatch.
/// </summary>
/// <remarks>
/// <para>
/// These options control batching, retry policies, buffering, dimension filtering,
/// and CloudWatch namespace settings. Defaults are aligned with CloudWatch service limits
/// and common recommended practices.
/// </para>
/// <para>
/// The class exposes read-only collections for dimension tag keys and blocked dimension key patterns.
/// To replace their contents, use <see cref="SetDimensionTagKeys"/> or <see cref="SetBlockedDimensionKeyPatterns"/>.
/// </para>
/// <para>
/// <b>Limits &amp; CloudWatch considerations</b><br/>
/// CloudWatch enforces a maximum of 20 metric data items per <c>PutMetricData</c> request and
/// at most 10 dimensions per metric. Properties such as <see cref="MaxBatchSize"/> and
/// <see cref="DimensionTagKeys"/> should be configured accordingly.
/// </para>
/// </remarks>
/// <threadsafety>
/// This type is intended to be registered as a singleton options instance and read by multiple threads.
/// Consumers should treat it as immutable after configuration. The mutating helper methods
/// (<see cref="SetDimensionTagKeys"/> and <see cref="SetBlockedDimensionKeyPatterns"/>) are expected
/// to be called during application startup only.
/// </threadsafety>
/// <example>
/// The following example shows how to register and validate these options using <c>Microsoft.Extensions.Options</c>:
/// <code language="csharp">
/// services.AddOptions&lt;CloudWatchExporterOptions&gt;()
///     .Configure(o =>
///     {
///         o.Namespace = "MyCompany.MyService";
///         o.UseDotsCase = true;                 // Keep metric names with dots
///         o.MaxBatchSize = 20;                  // CloudWatch max per request
///         o.TimeoutMs = 5000;                   // 5s request timeout
///         o.MaxRetries = 3;                     // Retry transient failures
///         o.BaseDelayMs = 250;                  // Backoff base delay
///         o.SetDimensionTagKeys(new []
///         {
///             "service.name", "service.version", "deployment.environment", "host.name", "aws.region"
///         });
///         o.SetBlockedDimensionKeyPatterns(new []
///         {
///             "^user\\.", ".*id$", "^request\\.", "^session\\."
///         });
///         o.EnableBuffering = true;
///         o.BufferCapacity = 20_000;
///         o.FlushIntervalMs = 1000;
///         o.MaxFlushBatch = 20;
///     })
///     .ValidateOnStart();
/// </code>
/// </example>
public sealed class CloudWatchExporterOptions
{
    private readonly Collection<string> _dimensionTagKeys =
        new() { "service.name", "service.version", "deployment.environment", "host.name" };

    private readonly Collection<string> _blockedDimensionKeyPatterns =
        new() { "^user\\.", ".*id$", "^request\\.", "^session\\." };

    /// <summary>
    /// Initializes a new instance of the <see cref="CloudWatchExporterOptions"/> class
    /// with sensible defaults aligned to CloudWatch limits and recommended behavior.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><see cref="Namespace"/> defaults to <c>"NetMetric"</c>.</description></item>
    /// <item><description><see cref="UseDotsCase"/> is <see langword="true"/> to preserve dotted metric names.</description></item>
    /// <item><description><see cref="MaxBatchSize"/> defaults to <c>20</c> (CloudWatch maximum).</description></item>
    /// <item><description>Retry policy: <see cref="MaxRetries"/>=<c>4</c>, <see cref="BaseDelayMs"/>=<c>250</c> ms.</description></item>
    /// <item><description>Distributions use <see cref="Amazon.CloudWatch.Model.StatisticSet"/> by default
    /// via <see cref="UseStatisticSetForDistributions"/>.</description></item>
    /// <item><description>Default environment dimensions (e.g., service name, version) are merged when
    /// <see cref="MergeDefaultDimensions"/> is <see langword="true"/>.</description></item>
    /// <item><description>Buffering is enabled with <see cref="BufferCapacity"/>=<c>10,000</c> and
    /// <see cref="FlushIntervalMs"/>=<c>2000</c> ms.</description></item>
    /// <item><description>Cardinality guard defaults: <see cref="MaxDimensionValueLength"/>=<c>250</c>,
    /// <see cref="MaxUniqueValuesPerKey"/>=<c>10,000</c>, and <see cref="DropOnlyOverflowingKey"/>=<see langword="true"/>.</description></item>
    /// </list>
    /// </remarks>
    public CloudWatchExporterOptions()
    {
        Namespace = "NetMetric";
        DropEmptyDimensions = true;
        UseDotsCase = true;
        MaxBatchSize = 20;
        TimeoutMs = 3000;
        MaxRetries = 4;
        BaseDelayMs = 250;

        UseStatisticSetForDistributions = true;
        MergeDefaultDimensions = true;

        EnableBuffering = true;
        BufferCapacity = 10_000;
        FlushIntervalMs = 2000;

        DropOnlyOverflowingKey = true;
        MaxDimensionValueLength = 250;
        MaxUniqueValuesPerKey = 10_000;
    }

    /// <summary>
    /// Gets or sets the CloudWatch namespace under which metrics are published.
    /// </summary>
    /// <value>A non-empty string representing the CloudWatch namespace.</value>
    /// <remarks>
    /// Choose a stable, vendor/owner-prefixed value such as <c>"MyCompany.MyService"</c>.
    /// Requests to CloudWatch will fail if the namespace is empty.
    /// </remarks>
    public string Namespace { get; set; }

    /// <summary>
    /// Gets the dimension tag keys allowed to be promoted as CloudWatch dimensions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default keys include <c>service.name</c>, <c>service.version</c>,
    /// <c>deployment.environment</c>, and <c>host.name</c>.
    /// </para>
    /// <para>
    /// CloudWatch allows at most 10 dimensions per metric. Keep this list concise
    /// and prefer low-cardinality keys to avoid cost and query explosion.
    /// </para>
    /// </remarks>
    /// <value>
    /// A snapshot read-only view of the current allowed keys. Use <see cref="SetDimensionTagKeys"/>
    /// to replace the underlying sequence.
    /// </value>
    public ReadOnlyCollection<string> DimensionTagKeys => new(_dimensionTagKeys);

    /// <summary>
    /// Replaces the allowed dimension tag keys with the provided sequence.
    /// </summary>
    /// <param name="keys">New set of dimension keys. If <see langword="null"/>, the list is cleared.</param>
    /// <remarks>
    /// <para>
    /// Call this method during startup to override the default set of allowed keys.  
    /// The list is fully replaced; to add a single key, fetch the current list, append,
    /// and pass back the new sequence.
    /// </para>
    /// <para>
    /// Keys are case-sensitive and compared using <see cref="StringComparer.Ordinal"/> downstream.
    /// Empty or duplicate keys are rejected by the validator.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// options.SetDimensionTagKeys(new []
    /// {
    ///     "service.name", "service.version", "deployment.environment", "host.name", "aws.region"
    /// });
    /// </code>
    /// </example>
    public void SetDimensionTagKeys(IEnumerable<string>? keys)
    {
        _dimensionTagKeys.Clear();
        if (keys is null) return;

        foreach (var k in keys)
        {
            _dimensionTagKeys.Add(k);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to drop dimensions whose values are empty.
    /// </summary>
    /// <value><see langword="true"/> to drop empty values; otherwise, <see langword="false"/>.</value>
    /// <remarks>
    /// When enabled, dimensions with null/whitespace values are filtered out before sending to CloudWatch.
    /// This typically improves signal quality and reduces unnecessary cardinality.
    /// </remarks>
    public bool DropEmptyDimensions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to keep metric names in dotted case (<see langword="true"/>)
    /// or replace dots with underscores (<see langword="false"/>).
    /// </summary>
    /// <value><see langword="true"/> to preserve dotted metric names; otherwise, <see langword="false"/>.</value>
    /// <remarks>
    /// Some downstream tools prefer underscores. Choose based on your query/visualization ecosystem.
    /// </remarks>
    public bool UseDotsCase { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of metrics per batch sent to CloudWatch.  
    /// Must be ≤ 20, due to CloudWatch service limits.
    /// </summary>
    /// <value>An integer between 1 and 20.</value>
    /// <remarks>
    /// If configured above 20, the exporter clamps it to 20; if below 1, validation fails.
    /// </remarks>
    public int MaxBatchSize { get; set; }

    /// <summary>
    /// Gets or sets the timeout (in milliseconds) for each CloudWatch <c>PutMetricData</c> call.
    /// </summary>
    /// <value>A positive integer (milliseconds).</value>
    /// <remarks>
    /// Consider balancing this with <see cref="MaxRetries"/> and <see cref="BaseDelayMs"/> to achieve
    /// your desired error budget and latency profile.
    /// </remarks>
    public int TimeoutMs { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for transient failures.
    /// </summary>
    /// <value>A non-negative integer; 0 disables retries.</value>
    /// <remarks>
    /// Retries are applied to throttling (HTTP 429), server errors (5xx), and common limit responses.
    /// Exponential backoff with jitter is used between attempts.
    /// </remarks>
    public int MaxRetries { get; set; }

    /// <summary>
    /// Gets or sets the base delay (in milliseconds) for exponential backoff retries.
    /// </summary>
    /// <value>A positive integer (milliseconds).</value>
    /// <remarks>
    /// The effective delay grows exponentially per attempt and includes random jitter
    /// to reduce thundering herd effects.
    /// </remarks>
    public int BaseDelayMs { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use <see cref="Amazon.CloudWatch.Model.StatisticSet"/>
    /// for distribution metrics instead of sending individual values.
    /// </summary>
    /// <value><see langword="true"/> to prefer statistic sets; otherwise, <see langword="false"/>.</value>
    /// <remarks>
    /// When enabled, enumerable numeric values are summarized into count, sum, min, and max,
    /// reducing payload size and preserving key distribution characteristics.
    /// </remarks>
    public bool UseStatisticSetForDistributions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to merge default environment dimensions
    /// (from <c>IAwsEnvironmentInfo</c>) with metric dimensions.
    /// </summary>
    /// <value><see langword="true"/> to merge; otherwise, <see langword="false"/>.</value>
    /// <remarks>
    /// Typical defaults include <c>service.name</c>, <c>service.version</c>,
    /// <c>deployment.environment</c>, and <c>host.name</c>.
    /// </remarks>
    public bool MergeDefaultDimensions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable background buffering
    /// via an in-memory channel with periodic flush.  
    /// If disabled, metrics are exported immediately without buffering.
    /// </summary>
    /// <value><see langword="true"/> to enable buffering; otherwise, <see langword="false"/>.</value>
    /// <remarks>
    /// Buffering reduces call overhead on hot paths by decoupling application threads
    /// from CloudWatch I/O. See <see cref="BufferCapacity"/>, <see cref="FlushIntervalMs"/>,
    /// and <see cref="MaxFlushBatch"/> for flush loop behavior.
    /// </remarks>
    public bool EnableBuffering { get; set; }

    /// <summary>
    /// Gets or sets the capacity of the in-memory channel (in number of metrics).  
    /// When exceeded, the oldest metrics are dropped.
    /// </summary>
    /// <value>A positive integer representing the maximum buffered metric count.</value>
    /// <remarks>
    /// Choose a value based on your peak throughput and acceptable loss during bursts.
    /// </remarks>
    public int BufferCapacity { get; set; }

    /// <summary>
    /// Gets or sets the automatic flush interval (in milliseconds) when buffering is enabled.
    /// </summary>
    /// <value>A positive integer (milliseconds).</value>
    /// <remarks>
    /// Shorter intervals reduce end-to-end latency at the cost of more frequent CloudWatch calls.
    /// </remarks>
    public int FlushIntervalMs { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of metrics flushed per interval (≤ 20).
    /// If set to 0, <see cref="MaxBatchSize"/> is used instead.
    /// </summary>
    /// <value>An integer between 0 and 20, where 0 means "use <see cref="MaxBatchSize"/>".</value>
    /// <remarks>
    /// This upper bound is applied by the background flush service to limit per-interval throughput.
    /// </remarks>
    public int MaxFlushBatch { get; set; } // default 0 → use MaxBatchSize

    /// <summary>
    /// Gets the regex patterns that define dimension keys to be blocked
    /// (e.g., <c>^user\.</c>, <c>.*id$</c>).
    /// </summary>
    /// <value>A read-only view of the current deny-list patterns.</value>
    /// <remarks>
    /// Use <see cref="SetBlockedDimensionKeyPatterns"/> to replace the underlying sequence.
    /// Patterns are compiled with <see cref="RegexOptions.IgnoreCase"/> and
    /// <see cref="RegexOptions.CultureInvariant"/>.
    /// </remarks>
    public ReadOnlyCollection<string> BlockedDimensionKeyPatterns => new(_blockedDimensionKeyPatterns);

    /// <summary>
    /// Replaces the blocked dimension key regex patterns with the provided sequence.
    /// </summary>
    /// <param name="patterns">New set of regex patterns. If <see langword="null"/>, the list is cleared.</param>
    /// <remarks>
    /// Use this to prevent high-cardinality or sensitive dimension keys from being added.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// options.SetBlockedDimensionKeyPatterns(new []
    /// {
    ///     "^user\\.", ".*id$", "^session\\.", "^request\\."
    /// });
    /// </code>
    /// </example>
    public void SetBlockedDimensionKeyPatterns(IEnumerable<string>? patterns)
    {
        _blockedDimensionKeyPatterns.Clear();
        if (patterns is null) return;

        foreach (var p in patterns)
        {
            _blockedDimensionKeyPatterns.Add(p);
        }
    }

    /// <summary>
    /// Gets or sets the maximum allowed length for dimension values.  
    /// CloudWatch hard limit is 1024; 250 is a practical default.
    /// </summary>
    /// <value>A positive integer less than or equal to 1024.</value>
    /// <remarks>
    /// Values longer than this threshold are truncated before sending to CloudWatch.
    /// </remarks>
    public int MaxDimensionValueLength { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of unique values tracked per dimension key (in-memory).  
    /// Set to 0 to disable.
    /// </summary>
    /// <value>A non-negative integer; 0 disables tracking enforcement.</value>
    /// <remarks>
    /// Used by the cardinality guard to mitigate cost and performance issues stemming from
    /// unbounded label value growth (for example, per-user IDs).
    /// </remarks>
    public int MaxUniqueValuesPerKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to drop only the overflowing key
    /// when <see cref="MaxUniqueValuesPerKey"/> is exceeded, instead of dropping the entire metric.
    /// </summary>
    /// <value><see langword="true"/> to drop only the overflowing dimension; otherwise, <see langword="false"/>.</value>
    /// <remarks>
    /// When <see langword="true"/>, the metric is still emitted but the specific dimension
    /// causing the overflow is omitted. When <see langword="false"/>, the entire metric is skipped.
    /// </remarks>
    public bool DropOnlyOverflowingKey { get; set; }
}
