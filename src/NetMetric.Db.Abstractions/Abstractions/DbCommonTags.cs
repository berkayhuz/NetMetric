// <copyright file="DbCommonTags.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Db.Abstractions;

/// <summary>
/// Provides a curated set of well-known tag keys used to annotate database-related metrics
/// (e.g., connection gauges, pool statistics, query timers, and error counters).
/// </summary>
/// <remarks>
/// <para>
/// These tag keys are intended to bring consistency to database telemetry across different
/// providers (SQL Server, PostgreSQL, MySQL, SQLite, etc.). Using a common vocabulary makes it
/// easier to correlate and aggregate metrics across services and environments.
/// </para>
/// <para>
/// <strong>Cardinality guidance:</strong>
/// Some tags (such as <see cref="Statement"/>) may carry high cardinality if populated with raw
/// SQL text or unique query shapes. Consider disabling or redacting such tags in production
/// unless you explicitly need them.
/// </para>
/// <para>
/// Typical usage includes attaching these keys to metrics created via the NetMetric abstractions
/// (e.g., gauges, counters, timers) or providing them as default tags via your database module options.
/// </para>
/// </remarks>
/// <example>
/// <para>Attach common DB tags when building instruments:</para>
/// <code language="csharp"><![CDATA[
/// var baseTags = new Dictionary<string, string>
/// {
///     [DbCommonTags.DbSystem] = "postgresql",
///     [DbCommonTags.DbName]   = "orders",
///     [DbCommonTags.NetPeer]  = "db.internal.mycorp",
///     [DbCommonTags.NetPort]  = "5432",
///     [DbCommonTags.DbUser]   = "orders_app"
/// };
///
/// var connectionsGauge = metricFactory
///     .Gauge("db.client.connections.active", "Active DB connections")
///     .WithTags(t => { foreach (var kv in baseTags) t.Add(kv.Key, kv.Value); })
///     .Build();
/// ]]></code>
///
/// <para>Provide default tags via your module configuration (example):</para>
/// <code language="csharp"><![CDATA[
/// services.Configure<YourDbMetricsOptions>(opts =>
/// {
///     opts.DefaultTags = new Dictionary<string, string>
///     {
///         [DbCommonTags.DbSystem] = "mssql",
///         [DbCommonTags.DbName]   = "inventory"
///     };
/// });
/// ]]></code>
///
/// <para>Attach operation- and statement-level tags at the call site (if enabled):</para>
/// <code language="csharp"><![CDATA[
/// var queryTags = new Dictionary<string, string>
/// {
///     [DbCommonTags.Operation] = "select",
///     // Use with care; consider parameterization or hashing to limit cardinality:
///     [DbCommonTags.Statement] = "SELECT id, status FROM orders WHERE id = @id"
/// };
///
/// using (dbModule.StartQuery()) // duration recorded by module; tags may be applied downstream
/// {
///     // Execute the command...
/// }
/// ]]></code>
/// </example>
public static class DbCommonTags
{
    /// <summary>
    /// Identifies the database management system (DBMS) in use.
    /// </summary>
    /// <remarks>
    /// Examples: <c>"mssql"</c>, <c>"postgresql"</c>, <c>"mysql"</c>, <c>"sqlite"</c>.
    /// This tag enables cross-vendor dashboards and comparisons.
    /// </remarks>
    public const string DbSystem = "db.system";

    /// <summary>
    /// The logical name of the database targeted by the operation or connection.
    /// </summary>
    /// <remarks>
    /// Keep values stable (e.g., <c>"orders"</c>, <c>"inventory"</c>) to facilitate grouping.
    /// </remarks>
    public const string DbName = "db.name";

    /// <summary>
    /// The network peer name of the database endpoint, such as hostname or data source.
    /// </summary>
    /// <remarks>
    /// Examples: <c>"db.internal.mycorp"</c>, <c>"orders-db.production.svc"</c>, or a
    /// provider-specific data source value.
    /// </remarks>
    public const string NetPeer = "net.peer.name";

    /// <summary>
    /// The TCP port number used to connect to the database endpoint.
    /// </summary>
    /// <remarks>
    /// Examples: <c>"5432"</c> for PostgreSQL, <c>"1433"</c> for SQL Server, <c>"3306"</c> for MySQL.
    /// Prefer string representations to keep tag dictionaries uniform.
    /// </remarks>
    public const string NetPort = "net.peer.port";

    /// <summary>
    /// The database statement text (SQL or equivalent) associated with a metric or event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>High cardinality:</strong> This tag can explode metric series if you include raw
    /// SQL text with literals or unique shapes. Consider one of the following:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Disable this tag in production and enable it only during investigations.</description></item>
    ///   <item><description>Use parameterized statements or normalized query shapes.</description></item>
    ///   <item><description>Hash or truncate the value to reduce uniqueness.</description></item>
    /// </list>
    /// </remarks>
    public const string Statement = "db.statement";

    /// <summary>
    /// The high-level database operation type associated with a query.
    /// </summary>
    /// <remarks>
    /// Recommended values: <c>"select"</c>, <c>"insert"</c>, <c>"update"</c>, <c>"delete"</c>,
    /// or <c>"other"</c> when the action does not match a standard verb (e.g., DDL).
    /// </remarks>
    public const string Operation = "db.operation";

    /// <summary>
    /// The database user or role on behalf of which the connection or query executes.
    /// </summary>
    /// <remarks>
    /// For pooled connections, this may reflect the application principal used for the pool.
    /// Avoid storing end-user identities here; prefer application/service principals.
    /// </remarks>
    public const string DbUser = "db.user";
}
