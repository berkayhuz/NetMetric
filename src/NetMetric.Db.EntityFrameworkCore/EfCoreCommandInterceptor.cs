// <editor-fold desc="NetMetricEfCoreInterceptors.cs">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace NetMetric.Db.EntityFrameworkCore;

/// <summary>
/// An EF Core <see cref="DbCommandInterceptor"/> that measures query latency
/// and increments a failure counter for failed commands.
/// </summary>
/// <remarks>
/// <para>
/// This interceptor collaborates with <see cref="IDbInstrumentation"/> to:
/// </para>
/// <list type="bullet">
///   <item><description>Start a timing scope before a command is executed (sync/async).</description></item>
///   <item><description>Dispose the timing scope after the command completes (sync/async).</description></item>
///   <item><description>Report failures via <see cref="IDbInstrumentation.OnQueryFailed(System.Collections.Generic.IReadOnlyDictionary{string, string}?)"/> when EF Core raises <see cref="DbCommandInterceptor.CommandFailed(DbCommand, CommandErrorEventData)"/>.</description></item>
/// </list>
/// <para>
/// Tag values are derived from the <see cref="DbCommand"/> and connection properties via
/// <see cref="BuildTags(DbCommand)"/>. The following tags are produced when available:
/// <c>db.system</c>, <c>db.name</c>, <c>db.user</c>, <c>db.operation</c>, and (optionally) <c>db.statement</c>.
/// See <see cref="DbCommonTags"/> and <see cref="DbMetricsOptions"/> for configuration.
/// </para>
/// <para><strong>Thread Safety:</strong> The interceptor is stateless except for an internal
/// concurrent map that tracks active timing scopes per <see cref="DbCommand"/> instance.
/// This map is safe for concurrent access.</para>
/// </remarks>
/// <example>
/// Registering the interceptor via DI when configuring a <c>DbContext</c>:
/// <code language="csharp"><![CDATA[
/// services.AddOptions<DbMetricsOptions>().Configure(opts =>
/// {
///     opts.IncludeOperationTag = true;
///     opts.StatementMode = StatementTagMode.Obfuscated; // sanitize SQL in tags
///     opts.MaxStatementTagLength = 512;
/// });
///
/// services.AddSingleton<IDbInstrumentation, DbInstrumentation>();
/// services.AddSingleton<DbCommandInterceptor, NetMetricEfCoreCommandInterceptor>();
///
/// services.AddDbContext<MyDbContext>((sp, options) =>
/// {
///     var interceptor = sp.GetRequiredService<DbCommandInterceptor>();
///     options.AddInterceptors(interceptor);
///     // ... other EF Core configuration
/// });
/// ]]></code>
/// </example>
public sealed class NetMetricEfCoreCommandInterceptor : DbCommandInterceptor
{
    private readonly IDbInstrumentation _ins;
    private readonly DbMetricsOptions _opts;

    // Tracks the currently active timing scope per DbCommand.
    private readonly ConcurrentDictionary<DbCommand, IDisposable> _scopes = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="NetMetricEfCoreCommandInterceptor"/> class.
    /// </summary>
    /// <param name="ins">The database instrumentation instance used for metrics collection.</param>
    /// <param name="opts">The typed options used to control tag emission and statement handling.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="ins"/> or <paramref name="opts"/> is <see langword="null"/>.
    /// </exception>
    public NetMetricEfCoreCommandInterceptor(IDbInstrumentation ins, IOptions<DbMetricsOptions> opts)
    {
        ArgumentNullException.ThrowIfNull(opts);

        _ins = ins ?? throw new ArgumentNullException(nameof(ins));
        _opts = opts.Value;
    }

    // ---------- NonQuery (sync) ----------
    /// <inheritdoc/>
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(command);

        StartScope(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(command);

        StopScope(command);
        return base.NonQueryExecuted(command, eventData, result);
    }

    // ---------- NonQuery (async) ----------
    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StartScope(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData,
        int result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StopScope(command);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    // ---------- Scalar (sync) ----------
    /// <inheritdoc/>
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        ArgumentNullException.ThrowIfNull(command);

        StartScope(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        ArgumentNullException.ThrowIfNull(command);

        StopScope(command);
        return base.ScalarExecuted(command, eventData, result);
    }

    // ---------- Scalar (async) ----------
    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StartScope(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData,
        object? result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StopScope(command);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    // ---------- Reader (sync) ----------
    /// <inheritdoc/>
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        ArgumentNullException.ThrowIfNull(command);

        StartScope(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        ArgumentNullException.ThrowIfNull(command);

        StopScope(command);
        return base.ReaderExecuted(command, eventData, result);
    }

    // ---------- Reader (async) ----------
    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StartScope(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StopScope(command);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    /// <summary>
    /// Called by EF Core when command execution fails; increments the failed-query counter
    /// and finalizes any active timing scope for the command.
    /// </summary>
    /// <param name="command">The database command that failed.</param>
    /// <param name="eventData">The EF Core event data for the failure.</param>
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(command);
        _ins.OnQueryFailed(BuildTags(command));
        StopScope(command);
        base.CommandFailed(command, eventData);
    }

    /// <summary>
    /// Async counterpart of <see cref="CommandFailed(DbCommand, CommandErrorEventData)"/>.
    /// </summary>
    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ins.OnQueryFailed(BuildTags(command));
        StopScope(command);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    /// <summary>
    /// Starts a timing scope for the specified <see cref="DbCommand"/> and remembers it
    /// so it can be disposed when execution completes.
    /// </summary>
    /// <param name="command">The command to start timing for.</param>
    private void StartScope(DbCommand command)
    {
        var scope = _ins.StartQueryTimer(BuildTags(command));
        _scopes[command] = scope;
    }

    /// <summary>
    /// Stops (disposes) the timing scope for the specified <see cref="DbCommand"/>, if present.
    /// </summary>
    /// <param name="command">The command whose timing scope should be stopped.</param>
    private void StopScope(DbCommand command)
    {
        if (_scopes.TryRemove(command, out var scope))
        {
            scope.Dispose();
        }
    }

    /// <summary>
    /// Builds the set of metric tags for the given command,
    /// including <c>db.system</c>, <c>db.name</c>, <c>db.user</c>, <c>db.operation</c>, and optionally <c>db.statement</c>.
    /// </summary>
    /// <param name="cmd">The database command used as the tag source.</param>
    /// <returns>A tag dictionary suitable for <see cref="IDbInstrumentation"/> methods.</returns>
    /// <remarks>
    /// <para>
    /// Statement inclusion is controlled via <see cref="DbMetricsOptions.StatementMode"/>. When set to
    /// <see cref="StatementTagMode.Obfuscated"/>, the SQL is sanitized using <see cref="SqlStatementSanitizer.Obfuscate(string)"/>.
    /// The length is capped by <see cref="DbMetricsOptions.MaxStatementTagLength"/>.
    /// </para>
    /// <para>
    /// The <c>db.system</c> is inferred from the connection type namespace (e.g., <c>Npgsql</c> → <c>postgresql</c>,
    /// <c>SqlClient</c> → <c>mssql</c>). Unknown providers default to <c>unknown</c>.
    /// </para>
    /// </remarks>
    private Dictionary<string, string> BuildTags(DbCommand cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(cmd.Connection?.Database))
        {
            dict[DbCommonTags.DbName] = cmd.Connection!.Database!;
        }

        var ns = cmd.Connection?.GetType().Namespace ?? string.Empty;
        dict[DbCommonTags.DbSystem] = ns.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ? "postgresql"
                                   : ns.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) ? "mssql"
                                   : "unknown";

        if (_opts.IncludeConnectionTags)
        {
            if (!string.IsNullOrWhiteSpace(cmd.Connection?.DataSource))
            {
                dict[DbCommonTags.NetPeer] = cmd.Connection!.DataSource!;
            }

            var user = TryGetUser(cmd.Connection);
            if (!string.IsNullOrEmpty(user))
            {
                dict[DbCommonTags.DbUser] = user!;
            }
        }

        if (_opts.IncludeOperationTag)
        {
            dict[DbCommonTags.Operation] = DetectOperation(cmd.CommandText);
        }

        if (_opts.StatementMode != StatementTagMode.None && !string.IsNullOrWhiteSpace(cmd.CommandText))
        {
            var stmt = cmd.CommandText!;
            if (_opts.StatementMode == StatementTagMode.Obfuscated)
            {
                stmt = SqlStatementSanitizer.Obfuscate(stmt);
            }

            if (_opts.MaxStatementTagLength > 0 && stmt.Length > _opts.MaxStatementTagLength)
            {
                stmt = stmt[.._opts.MaxStatementTagLength];
            }

            dict[DbCommonTags.Statement] = stmt;
        }

        if (_opts.DefaultTags is { } defaults)
        {
            foreach (var kv in defaults)
            {
                if (!dict.ContainsKey(kv.Key))
                {
                    dict[kv.Key] = kv.Value;
                }
            }
        }

        return dict;
    }

    /// <summary>
    /// Detects the SQL operation type based on the first token of the statement.
    /// </summary>
    /// <param name="sql">The SQL text to analyze.</param>
    /// <returns>One of <c>select</c>, <c>insert</c>, <c>update</c>, <c>delete</c>, <c>merge</c>, or <c>other</c>.</returns>
    private static string DetectOperation(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return "other";
        var s = sql.TrimStart();
        var i = 0;
        while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
        var first = s[..i].Trim().ToUpperInvariant();
        return first switch
        {
            "SELECT" => "select",
            "INSERT" => "insert",
            "UPDATE" => "update",
            "DELETE" => "delete",
            "MERGE" => "merge",
            _ => "other"
        };
    }

    /// <summary>
    /// Attempts to extract a user name field from the connection string.
    /// </summary>
    /// <param name="c">The database connection.</param>
    /// <returns>The user name if present; otherwise, <see langword="null"/>.</returns>
    /// <remarks>
    /// Recognized keys include <c>User ID</c>, <c>User</c>, <c>Username</c>, and <c>UID</c>.
    /// Parsing is tolerant of malformed connection strings and returns <see langword="null"/> on errors.
    /// </remarks>
    private static string? TryGetUser(DbConnection? c)
    {
        try
        {
            var cs = c?.ConnectionString;
            if (string.IsNullOrEmpty(cs)) return null;
            var parts = cs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in parts)
            {
                var kv = p.Split('=', 2);
                if (kv.Length != 2) continue;
                if (string.Equals(kv[0], "User ID", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kv[0], "User", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kv[0], "Username", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kv[0], "UID", StringComparison.OrdinalIgnoreCase))
                {
                    return kv[1];
                }
            }
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// An EF Core <see cref="DbConnectionInterceptor"/> that reports connection open/close
/// events to <see cref="IDbInstrumentation"/>.
/// </summary>
/// <remarks>
/// Use this interceptor when you want metrics for active connection counts without
/// modifying database access code. It is complementary to
/// <see cref="NetMetricEfCoreCommandInterceptor"/>.
/// </remarks>
/// <example>
/// Registering both interceptors:
/// <code language="csharp"><![CDATA[
/// services.AddSingleton<DbConnectionInterceptor, NetMetricEfCoreConnectionInterceptor>();
/// services.AddSingleton<DbCommandInterceptor, NetMetricEfCoreCommandInterceptor>();
/// services.AddDbContext<MyDbContext>((sp, options) =>
/// {
///     options.AddInterceptors(
///         sp.GetRequiredService<DbConnectionInterceptor>(),
///         sp.GetRequiredService<DbCommandInterceptor>());
/// });
/// ]]></code>
/// </example>
public sealed class NetMetricEfCoreConnectionInterceptor : DbConnectionInterceptor
{
    private readonly IDbInstrumentation _ins;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetMetricEfCoreConnectionInterceptor"/> class.
    /// </summary>
    /// <param name="ins">The database instrumentation that tracks connection lifecycle metrics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ins"/> is <see langword="null"/>.</exception>
    public NetMetricEfCoreConnectionInterceptor(IDbInstrumentation ins)
    {
        _ins = ins ?? throw new ArgumentNullException(nameof(ins));
    }

    /// <inheritdoc/>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => _ins.OnConnectionOpened();

    /// <inheritdoc/>
    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _ins.OnConnectionOpened();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
        => _ins.OnConnectionClosed();
}

/// <summary>
/// Provides SQL statement obfuscation for use in metrics tags.
/// String and numeric literals are replaced with placeholders, and whitespace is normalized.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Obfuscate(string)"/> before emitting <c>db.statement</c> to avoid leaking
/// sensitive data (PII, secrets) via metrics backends. The transformation is syntax-agnostic
/// and works reasonably well for common SQL dialects without parsing.
/// </para>
/// <para>
/// <strong>Limitations:</strong> This is a best-effort regex-based sanitizer. It does not honor
/// dialect-specific quoting rules (e.g., dollar-quoted strings in PostgreSQL) or comments; adapt
/// as needed for stricter environments.
/// </para>
/// </remarks>
internal static partial class SqlStatementSanitizer
{
    private static readonly Regex StringRegex = new(@"'([^']|'')*'", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"\b\d+(\.\d+)?\b", RegexOptions.Compiled);

    /// <summary>
    /// Obfuscates an SQL statement by masking string and numeric literals
    /// and collapsing whitespace.
    /// </summary>
    /// <param name="sql">The SQL text to sanitize.</param>
    /// <returns>
    /// The sanitized SQL with literals replaced by placeholders; if <paramref name="sql"/> is
    /// <see langword="null"/> or whitespace, the original value is returned unchanged.
    /// </returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var original = "SELECT * FROM users WHERE email = 'alice@example.com' AND age > 42";
    /// var sanitized = SqlStatementSanitizer.Obfuscate(original);
    /// // Result: "SELECT * FROM users WHERE email = '?' AND age > ?"
    /// ]]></code>
    /// </example>
    public static string Obfuscate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;
        var s = StringRegex.Replace(sql, "'?'");
        s = NumberRegex.Replace(s, "?");
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }
}
