// <copyright file="DbInstrumentation.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Options;

namespace NetMetric.Db.Modules;

/// <summary>
/// Provides lightweight, provider-agnostic database instrumentation hooks
/// that update the associated <see cref="DbMetricsModule"/> with connection and query activity.
/// </summary>
/// <remarks>
/// <para>
/// This type acts as a thin adapter between database client code (ADO.NET, Dapper, EF Core, etc.)
/// and the <see cref="DbMetricsModule"/>. It records connection lifecycle changes,
/// measures query durations, and increments failure counters in a thread-safe manner.
/// </para>
/// <para><strong>Thread Safety:</strong>
/// All public members are safe to call concurrently. The underlying <see cref="DbMetricsModule"/>
/// uses atomic operations for counters and relies on thread-safe metric instruments.
/// </para>
/// <para><strong>Typical usage:</strong> resolve this type via DI in your data-access layer
/// and invoke <see cref="OnConnectionOpened"/> / <see cref="OnConnectionClosed"/> around
/// connection usage, and wrap command execution with <see cref="StartQueryTimer"/>.
/// On errors, call <see cref="OnQueryFailed"/>.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Registration (excerpt)
/// services.AddSingleton<DbMetricsModule>();
/// services.Configure<DbMetricsOptions>(o =>
/// {
///     o.DefaultTags = new Dictionary<string, string>
///     {
///         ["service.name"] = "orders-api",
///         ["db.system"] = "postgres"
///     };
/// });
/// services.AddSingleton<IDbInstrumentation, DbInstrumentation>();
///
/// // Usage in repository/service
/// public sealed class OrderRepository
/// {
///     private readonly IDbInstrumentation _dbi;
///     private readonly NpgsqlConnection _conn;
///
///     public OrderRepository(IDbInstrumentation dbi, NpgsqlConnection conn)
///     {
///         _dbi = dbi;
///         _conn = conn;
///     }
///
///     public async Task<Order?> GetAsync(int id, CancellationToken ct)
///     {
///         _dbi.OnConnectionOpened();
///         try
///         {
///             await _conn.OpenAsync(ct);
///
///             using (_dbi.StartQueryTimer(new Dictionary<string,string>
///             {
///                 ["db.operation"] = "select",
///                 ["db.statement"] = "orders_by_id"
///             }))
///             using var cmd = new NpgsqlCommand("select ... where id = @id", _conn);
///
///             // Execute your command here…
///            var reader = await cmd.ExecuteReaderAsync(ct);
///             return null;
///         }
///         catch
///         {
///             _dbi.OnQueryFailed(new Dictionary<string,string>
///             {
///                 ["error.type"] = "db.query.exception"
///             });
///             throw;
///         }
///         finally
///         {
///             _dbi.OnConnectionClosed();
///             await _conn.CloseAsync();
///         }
///     }
/// }
/// ]]></code>
/// </example>
internal sealed class DbInstrumentation : IDbInstrumentation
{
    private readonly DbMetricsModule _module;
    private readonly DbMetricsOptions _opts;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbInstrumentation"/> class.
    /// </summary>
    /// <param name="module">The database metrics module to update.</param>
    /// <param name="opts">The database metrics configuration options.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="module"/> or <paramref name="opts"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The options are captured to allow future use of default tags or behavior toggles when
    /// exporting tag sets in <see cref="StartQueryTimer"/> or <see cref="OnQueryFailed"/>.
    /// </remarks>
    public DbInstrumentation(DbMetricsModule module, IOptions<DbMetricsOptions> opts)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(opts);

        _module = module;
        _opts = opts.Value;
    }

    /// <summary>
    /// Signals that a database connection has been opened (increments the active connection gauge/counter).
    /// </summary>
    /// <remarks>
    /// Call this immediately after opening a connection or when a pooled connection is leased.
    /// Pair every invocation with a corresponding <see cref="OnConnectionClosed"/> to avoid skewing metrics.
    /// </remarks>
    public void OnConnectionOpened() => _module.IncActive();

    /// <summary>
    /// Signals that a database connection has been closed (decrements the active connection gauge/counter).
    /// </summary>
    /// <remarks>
    /// Call this in a <c>finally</c> block to ensure it always executes, even when exceptions occur.
    /// </remarks>
    public void OnConnectionClosed() => _module.DecActive();

    /// <summary>
    /// Starts a timer scope to measure query execution duration.
    /// </summary>
    /// <param name="tags">
    /// Optional contextual tags (e.g., <c>"db.operation"</c>, <c>"db.statement"</c>) that may be used
    /// by exporters in the future. The current implementation records duration only.
    /// </param>
    /// <returns>
    /// An <see cref="IDisposable"/> scope; disposing it records the elapsed time to
    /// <c>db.client.query.duration</c>.
    /// </returns>
    /// <remarks>
    /// Prefer the <c>using</c> pattern to ensure disposal:
    /// <code language="csharp"><![CDATA[
    /// using (_dbi.StartQueryTimer())
    /// {
    ///     // Execute your command here...
    /// }
    /// ]]></code>
    /// </remarks>
    public IDisposable StartQueryTimer(IReadOnlyDictionary<string, string>? tags = null)
        => _module.StartQuery(); // Future: merge tags with _opts.DefaultTags during export.

    /// <summary>
    /// Signals that a database query has failed and increments the failure counter.
    /// </summary>
    /// <param name="tags">
    /// Optional contextual tags for the failure (e.g., <c>"error.type"</c>, <c>"db.statement"</c>).
    /// The current implementation tracks a total error count only.
    /// </param>
    /// <remarks>
    /// Call this in exception handlers or when a command returns an error status.
    /// </remarks>
    public void OnQueryFailed(IReadOnlyDictionary<string, string>? tags = null)
        => _module.IncError(); // Current counter is untagged; tags may be forwarded by exporters later.
}
