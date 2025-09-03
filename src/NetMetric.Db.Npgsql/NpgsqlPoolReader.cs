// <copyright file="NpgsqlPoolReader.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Db.Modules;
using Npgsql;

namespace NetMetric.Db.Npgsql;

/// <summary>
/// Placeholder component for connection pool statistics collection in Npgsql.
/// </summary>
/// <remarks>
/// <para>
/// As of current Npgsql releases, there is no stable public API to retrieve connection
/// pool statistics directly. Consequently, this reader is implemented as a no-op to keep
/// the surface area ready for future Npgsql capabilities without affecting callers.
/// </para>
/// <para>
/// Database client metrics such as active connections, query latency, and error counts
/// are still recorded via interceptors/adapters and surfaced through
/// <see cref="NetMetric.Db.Modules.DbMetricsModule"/>.
/// </para>
/// <para><strong>Thread safety:</strong> The provided API is stateless and side-effect free.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Acquire your data source (example only):
/// var dataSource = NpgsqlDataSource.Create("Host=localhost;Username=app;Password=secret;Database=orders");
///
/// // Optionally resolve the module (used by other collectors):
/// var module = serviceProvider.GetRequiredService<DbMetricsModule>();
///
/// // Call the pool reader periodically; currently a no-op and safe to invoke:
/// NpgsqlPoolReader.Sample(dataSource, logicalName: "orders-pool");
///
/// // Note:
/// //   Pool-level metrics are not emitted by this reader today.
/// //   Connection counts, query duration, and error totals are still available
/// //   via DbInstrumentation / DbMetricsModule and other collectors.
/// ]]></code>
/// </example>
/// <seealso cref="NetMetric.Db.Modules.DbMetricsModule"/>
/// <seealso cref="NetMetric.Db.Collectors.PoolCollector"/>
public sealed class NpgsqlPoolReader
{
    private readonly DbMetricsModule _module;

    /// <summary>
    /// Initializes a new instance of the <see cref="NpgsqlPoolReader"/> class.
    /// </summary>
    /// <param name="module">The database metrics module used elsewhere for instrumentation.</param>
    /// <remarks>
    /// The instance is currently a placeholder. When Npgsql exposes a public pool-statistics API,
    /// this reader can be extended to publish sibling measurements to the module’s multi-gauge.
    /// </remarks>
    public NpgsqlPoolReader(DbMetricsModule module) => _module = module;

    /// <summary>
    /// Attempts to sample connection pool statistics for the specified
    /// <see cref="global::Npgsql.NpgsqlDataSource"/>.
    /// </summary>
    /// <remarks>
    /// At present, this method performs no work because Npgsql does not expose pool metrics
    /// via a public API. It exists to provide a stable call site that can be enhanced in the future
    /// without breaking callers.
    /// </remarks>
    /// <param name="ds">The Npgsql data source (kept for future use).</param>
    /// <param name="logicalName">
    /// An optional logical pool name to associate with future measurements (e.g., <c>"primary"</c>).
    /// </param>
    public static void Sample(NpgsqlDataSource ds, string? logicalName = null)
    {
        _ = (ds, logicalName);
    }
}
