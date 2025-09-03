// <copyright file="DbMetricsOptions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Db.Abstractions;

/// <summary>
/// Specifies how the <c>db.statement</c> tag should be populated for query metrics,
/// balancing observability needs against privacy and metric-cardinality constraints.
/// </summary>
/// <remarks>
/// <para>
/// High-cardinality tags and personally identifiable information (PII) can negatively impact
/// storage and performance characteristics of metric backends. The values in this enum help
/// you decide whether to omit the statement entirely, keep a truncated preview, or emit an
/// obfuscated form where literals are masked.
/// </para>
/// <para>
/// This setting should be chosen in accordance with your organization's data-governance and
/// logging policies.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Example: configure to mask literals in emitted statements
/// services.Configure<DbMetricsOptions>(o =>
/// {
///     o.StatementMode = StatementTagMode.Obfuscated;
///     o.MaxStatementTagLength = 200;
/// });
/// ]]></code>
/// </example>
public enum StatementTagMode
{
    /// <summary>
    /// Do not include the <c>db.statement</c> tag at all.  
    /// Recommended as a safe default to minimize metric-cardinality
    /// and avoid exposing sensitive query text.
    /// </summary>
    None = 0,

    /// <summary>
    /// Include a truncated preview of the query text.  
    /// The length is capped by <see cref="DbMetricsOptions.MaxStatementTagLength"/>.
    /// </summary>
    Truncated = 1,

    /// <summary>
    /// Include an obfuscated version of the query text in which literals (e.g., numbers,
    /// strings, dates) are masked. The result may still be truncated depending on
    /// <see cref="DbMetricsOptions.MaxStatementTagLength"/>.
    /// </summary>
    Obfuscated = 2
}

/// <summary>
/// Provides configuration options for database metrics instrumentation exposed by NetMetric DB modules.
/// </summary>
/// <remarks>
/// <para>
/// These options influence emitted tags and the behavior of internal collectors such as
/// connection-pool sampling and statement handling.
/// </para>
/// <para>
/// Unless otherwise noted, these options are safe to change at application startup and remain
/// constant for the lifetime of the process.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Typical registration using Microsoft.Extensions.Options:
/// services.Configure<DbMetricsOptions>(opts =>
/// {
///     // Attach constant tags to all DB metrics (kept low-cardinality)
///     opts.DefaultTags = new Dictionary<string, string>
///     {
///         ["service.name"] = "orders-api",
///         ["db.system"]    = "postgres",
///         ["db.name"]      = "orders"
///     };
///
///     // Sample connection-pool stats every 5 seconds
///     opts.PoolSamplePeriodMs = 5000;
///
///     // Include operation and connection tags
///     opts.IncludeOperationTag  = true;   // e.g., SELECT/INSERT/UPDATE/DELETE
///     opts.IncludeConnectionTags = true;  // e.g., db.user, net.peer.name
///
///     // Handle db.statement conservatively
///     opts.StatementMode = StatementTagMode.Truncated;
///     opts.MaxStatementTagLength = 160;
/// });
/// ]]></code>
/// </example>
public sealed class DbMetricsOptions
{
    /// <summary>
    /// Additional tags to apply to all database metrics (for example, <c>db.system</c>, <c>db.name</c>, <c>service.name</c>, <c>net.peer.name</c>).  
    /// Keep this set low-cardinality (stable values) to avoid excessive series creation in metric backends.
    /// </summary>
    /// <remarks>
    /// These tags are typically applied at instrument creation time and should represent
    /// environment- or service-level constants (not per-request or per-query values).
    /// </remarks>
    public IReadOnlyDictionary<string, string>? DefaultTags { get; init; }

    /// <summary>
    /// The sampling period, in milliseconds, for emitting connection pool statistics.  
    /// A value of <c>0</c> or negative disables pool sampling.
    /// </summary>
    /// <remarks>
    /// Pool sampling can be relatively expensive depending on the provider. Choose a period
    /// that balances freshness with overhead (commonly between 2–15 seconds).
    /// </remarks>
    public int PoolSamplePeriodMs { get; init; } = 5000;

    /// <summary>
    /// Whether only failed queries should be timed.  
    /// </summary>
    /// <remarks>
    /// This flag is reserved for future behavior. At present, all queries are timed regardless
    /// of this setting. It exists to allow future optimizations where successful queries could
    /// be short-circuited to reduce overhead.
    /// </remarks>
    public bool TimeOnlyFailedQueries { get; init; }

    /// <summary>
    /// Controls how the <c>db.statement</c> tag is populated (disabled, truncated, or obfuscated).  
    /// Use this to limit PII exposure and mitigate high-cardinality risks.
    /// </summary>
    /// <seealso cref="StatementTagMode"/>
    public StatementTagMode StatementMode { get; init; } = StatementTagMode.None;

    /// <summary>
    /// The maximum length of the <c>db.statement</c> tag when
    /// <see cref="StatementMode"/> is <see cref="StatementTagMode.Truncated"/> or <see cref="StatementTagMode.Obfuscated"/>.
    /// </summary>
    /// <remarks>
    /// Applies after obfuscation (if enabled). Choose a limit that provides enough context for
    /// debugging while avoiding excessively long tag values in your metrics pipeline.
    /// </remarks>
    public int MaxStatementTagLength { get; init; } = 120;

    /// <summary>
    /// Whether to include the <c>db.operation</c> tag derived from the statement verb (for example, SELECT, INSERT, UPDATE, DELETE).
    /// </summary>
    /// <remarks>
    /// This tag is generally low-cardinality and useful for aggregate dashboards (e.g., p95 latency by operation).
    /// </remarks>
    public bool IncludeOperationTag { get; init; } = true;

    /// <summary>
    /// Whether to include connection-related tags such as <c>db.user</c> and <c>net.peer.name</c> when available.
    /// </summary>
    /// <remarks>
    /// These tags can help segment metrics by logical user or endpoint, but ensure values remain low-cardinality
    /// (e.g., a small set of connection users or hosts) to avoid series explosion.
    /// </remarks>
    public bool IncludeConnectionTags { get; init; } = true;
}
