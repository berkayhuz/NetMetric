// <copyright file="SqlClientConnectionWrapper.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using NetMetric.Db.Abstractions;

namespace NetMetric.Db.SqlClient;

/// <summary>
/// Provides a thin, instrumented wrapper around <see cref="Microsoft.Data.SqlClient.SqlConnection"/>
/// that preserves the behavior of the underlying connection while emitting telemetry
/// for connection lifecycle events.
/// </summary>
/// <remarks>
/// <para>
/// All operations are delegated to the inner <see cref="Microsoft.Data.SqlClient.SqlConnection"/> instance.
/// After a successful open, <see cref="NetMetric.Db.Abstractions.IDbInstrumentation.OnConnectionOpened"/> is
/// invoked; after a successful close, <see cref="NetMetric.Db.Abstractions.IDbInstrumentation.OnConnectionClosed"/>
/// is invoked.
/// </para>
/// <para><strong>Thread safety:</strong> This type does not introduce additional synchronization beyond
/// what the underlying <see cref="Microsoft.Data.SqlClient.SqlConnection"/> provides. Callers are expected
/// to follow the usual ADO.NET threading guidelines.</para>
/// <para><strong>Disposal:</strong> Disposing this wrapper also disposes the inner
/// <see cref="Microsoft.Data.SqlClient.SqlConnection"/>.</para>
/// </remarks>
/// <example>
/// Creating and using an instrumented SQL connection:
/// <code language="csharp"><![CDATA[
/// IDbInstrumentation ins = /* resolve from DI, e.g., new DbInstrumentation(module, options) */;
///
/// var csb = new SqlConnectionStringBuilder
/// {
///     DataSource = "localhost",
///     InitialCatalog = "AppDb",
///     IntegratedSecurity = true
/// };
///
/// await using var conn = new NetMetricSqlConnection(csb.ConnectionString, ins);
/// await conn.OpenAsync(CancellationToken.None);
///
/// using var cmd = conn.CreateCommand();
/// cmd.CommandText = "SELECT 1";
/// var result = (int)(await cmd.ExecuteScalarAsync(CancellationToken.None))!;
/// ]]></code>
/// </example>
/// <seealso cref="Microsoft.Data.SqlClient.SqlConnection"/>
/// <seealso cref="NetMetric.Db.Abstractions.IDbInstrumentation"/>
public sealed class NetMetricSqlConnection : DbConnection
{
    private readonly SqlConnection _inner;
    private readonly IDbInstrumentation _ins;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetMetricSqlConnection"/> class.
    /// </summary>
    /// <param name="connectionString">The connection string used by the inner <see cref="Microsoft.Data.SqlClient.SqlConnection"/>.</param>
    /// <param name="ins">The instrumentation sink that receives lifecycle callbacks.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="connectionString"/> or <paramref name="ins"/> is <see langword="null"/>.
    /// </exception>
    public NetMetricSqlConnection(string connectionString, IDbInstrumentation ins)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(ins);

        // CA2000-safe: ensure disposal if assignment fails later.
        var conn = new SqlConnection(connectionString);
        try
        {
            _inner = conn;
            _ins = ins;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => _inner.BeginTransaction(isolationLevel);

    /// <inheritdoc/>
    public override void ChangeDatabase(string databaseName)
        => _inner.ChangeDatabase(databaseName);

    /// <summary>
    /// Opens the underlying connection and emits a "connection opened" instrumentation callback on success.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If the underlying <see cref="Microsoft.Data.SqlClient.SqlConnection.Open()"/> throws, no callback is emitted.
    /// </para>
    /// </remarks>
    public override void Open()
    {
        _inner.Open();
        _ins.OnConnectionOpened();
    }

    /// <summary>
    /// Asynchronously opens the underlying connection and emits a "connection opened" instrumentation callback on success.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <remarks>
    /// If <see cref="Microsoft.Data.SqlClient.SqlConnection.OpenAsync(System.Threading.CancellationToken)"/> throws or is canceled,
    /// no callback is emitted.
    /// </remarks>
    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        await _inner.OpenAsync(cancellationToken).ConfigureAwait(false);
        _ins.OnConnectionOpened();
    }

    /// <summary>
    /// Closes the underlying connection and emits a "connection closed" instrumentation callback on success.
    /// </summary>
    /// <remarks>
    /// If the underlying <see cref="Microsoft.Data.SqlClient.SqlConnection.Close"/> throws, no callback is emitted.
    /// </remarks>
    public override void Close()
    {
        _inner.Close();
        _ins.OnConnectionClosed();
    }

    /// <summary>
    /// Creates an instrumented <see cref="System.Data.Common.DbCommand"/> that wraps the provider command
    /// returned by <see cref="Microsoft.Data.SqlClient.SqlConnection.CreateCommand"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="DbCommand"/> that measures query duration and reports failures via
    /// <see cref="NetMetric.Db.Abstractions.IDbInstrumentation"/>.
    /// </returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// using var cmd = conn.CreateCommand(); // returns NetMetricSqlCommand (DbCommand)
    /// cmd.CommandText = "UPDATE dbo.Items SET Price = @p WHERE Id = @id";
    /// cmd.Parameters.Add(new SqlParameter("@p", SqlDbType.Money) { Value = 9.99M });
    /// cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = 123 });
    /// var rows = await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    /// ]]></code>
    /// </example>
    protected override DbCommand CreateDbCommand()
        => new NetMetricSqlCommand(_inner.CreateCommand(), _ins);

    /// <summary>
    /// Gets or sets the connection string used by the underlying <see cref="Microsoft.Data.SqlClient.SqlConnection"/>.
    /// </summary>
    /// <remarks>
    /// The base setter allows assigning <see langword="null"/>; this override preserves that contract.
    /// </remarks>
    [AllowNull]
    public override string ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value;
    }
    // Equivalent alternative using a parameter-targeted attribute:
    // public override string ConnectionString
    // {
    //     get => _inner.ConnectionString;
    //     [param: AllowNull] set => _inner.ConnectionString = value;
    // }

    /// <inheritdoc/>
    public override string Database => _inner.Database;

    /// <inheritdoc/>
    public override string DataSource => _inner.DataSource;

    /// <inheritdoc/>
    public override string ServerVersion => _inner.ServerVersion;

    /// <inheritdoc/>
    public override ConnectionState State => _inner.State;

    /// <summary>
    /// Releases resources used by the connection.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to release managed and unmanaged resources; otherwise, <see langword="false"/>.
    /// </param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Provides a thin, instrumented wrapper around <see cref="Microsoft.Data.SqlClient.SqlCommand"/>
/// that preserves normal command behavior while emitting timing and failure telemetry.
/// </summary>
/// <remarks>
/// <para><strong>Security:</strong> The wrapper does not alter SQL text. Callers must use parameterized
/// queries to avoid SQL injection.</para>
/// <para>
/// Each execution method starts a timing scope via
/// <see cref="NetMetric.Db.Abstractions.IDbInstrumentation.StartQueryTimer(System.Collections.Generic.IReadOnlyDictionary{string, string}?)"/>
/// and calls <see cref="NetMetric.Db.Abstractions.IDbInstrumentation.OnQueryFailed(System.Collections.Generic.IReadOnlyDictionary{string, string}?)"/>
/// if the inner command throws.
/// </para>
/// </remarks>
/// <example>
/// Measuring latency and capturing failures:
/// <code language="csharp"><![CDATA[
/// using var cmd = conn.CreateCommand();
/// cmd.CommandText = "SELECT COUNT(*) FROM dbo.Items WHERE Price > @p";
/// cmd.Parameters.Add(new SqlParameter("@p", SqlDbType.Money) { Value = 10M });
/// var count = (int)(await cmd.ExecuteScalarAsync(CancellationToken.None))!;
/// ]]></code>
/// </example>
/// <seealso cref="Microsoft.Data.SqlClient.SqlCommand"/>
/// <seealso cref="NetMetric.Db.Abstractions.IDbInstrumentation"/>
internal sealed class NetMetricSqlCommand : DbCommand
{
    private readonly SqlCommand _inner;
    private readonly IDbInstrumentation _ins;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetMetricSqlCommand"/> class.
    /// </summary>
    /// <param name="inner">The underlying <see cref="Microsoft.Data.SqlClient.SqlCommand"/> to delegate to.</param>
    /// <param name="ins">The instrumentation sink that receives query telemetry.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="inner"/> or <paramref name="ins"/> is <see langword="null"/>.
    /// </exception>
    public NetMetricSqlCommand(SqlCommand inner, IDbInstrumentation ins)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(ins);
        _inner = inner;
        _ins = ins;
    }

    /// <summary>
    /// Gets or sets the SQL statement to execute at the data source.
    /// </summary>
    /// <remarks>
    /// Some target frameworks surface the base signature with relaxed nullability. This override preserves
    /// the base contract and suppresses nullability warnings where the compiler cannot infer intent.
    /// </remarks>
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Pass-through wrapper; callers are responsible for parameterization.")]
#pragma warning disable CS8765
    public override string CommandText
    {
        get => _inner.CommandText;
        set => _inner.CommandText = value;
    }
#pragma warning restore CS8765

    /// <inheritdoc/>
    public override int CommandTimeout
    {
        get => _inner.CommandTimeout;
        set => _inner.CommandTimeout = value;
    }

    /// <inheritdoc/>
    public override CommandType CommandType
    {
        get => _inner.CommandType;
        set => _inner.CommandType = value;
    }

    /// <summary>
    /// Gets or sets the <see cref="System.Data.Common.DbConnection"/> used by the command.
    /// </summary>
    /// <remarks>
    /// The override preserves the base nullability contract.
    /// </remarks>
    protected override DbConnection? DbConnection
    {
        get => _inner.Connection;
        set => _inner.Connection = (SqlConnection?)value;
    }

    /// <summary>
    /// Gets or sets the <see cref="System.Data.Common.DbTransaction"/> within which the command executes.
    /// </summary>
    /// <remarks>
    /// The override preserves the base nullability contract.
    /// </remarks>
    protected override DbTransaction? DbTransaction
    {
        get => _inner.Transaction;
        set => _inner.Transaction = (SqlTransaction?)value;
    }

    /// <inheritdoc/>
    public override bool DesignTimeVisible
    {
        get => _inner.DesignTimeVisible;
        set => _inner.DesignTimeVisible = value;
    }

    /// <inheritdoc/>
    public override UpdateRowSource UpdatedRowSource
    {
        get => _inner.UpdatedRowSource;
        set => _inner.UpdatedRowSource = value;
    }

    /// <summary>
    /// Creates a new parameter compatible with <see cref="Microsoft.Data.SqlClient.SqlCommand"/>.
    /// </summary>
    protected override DbParameter CreateDbParameter()
        => _inner.CreateParameter();

    /// <summary>
    /// Gets the collection of parameters associated with the command.
    /// </summary>
    protected override DbParameterCollection DbParameterCollection
        => _inner.Parameters;

    /// <inheritdoc/>
    public override void Cancel() => _inner.Cancel();

    /// <inheritdoc/>
    public override void Prepare() => _inner.Prepare();

    /// <summary>
    /// Executes a Transact-SQL statement and returns the number of rows affected.
    /// Emits timing telemetry and marks failures when exceptions are thrown.
    /// </summary>
    public override int ExecuteNonQuery()
    {
        using var scope = _ins.StartQueryTimer();
        try { return _inner.ExecuteNonQuery(); }
        catch { _ins.OnQueryFailed(); throw; }
    }

    /// <summary>
    /// Executes the query and returns the first column of the first row.
    /// Emits timing telemetry and marks failures when exceptions are thrown.
    /// </summary>
    public override object? ExecuteScalar()
    {
        using var scope = _ins.StartQueryTimer();
        try { return _inner.ExecuteScalar(); }
        catch { _ins.OnQueryFailed(); throw; }
    }

    /// <summary>
    /// Sends the command text to the connection and builds a <see cref="System.Data.Common.DbDataReader"/>.
    /// Emits timing telemetry and marks failures when exceptions are thrown.
    /// </summary>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        using var scope = _ins.StartQueryTimer();
        try { return _inner.ExecuteReader(behavior); }
        catch { _ins.OnQueryFailed(); throw; }
    }

    /// <summary>
    /// Asynchronously executes a SQL statement and returns the number of rows affected.
    /// Emits timing telemetry and marks failures when exceptions are thrown.
    /// </summary>
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        using var scope = _ins.StartQueryTimer();
        try { return await _inner.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        catch { _ins.OnQueryFailed(); throw; }
    }

    /// <summary>
    /// Asynchronously executes the query and returns the first column of the first row.
    /// Emits timing telemetry and marks failures when exceptions are thrown.
    /// </summary>
    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        using var scope = _ins.StartQueryTimer();
        try { return await _inner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false); }
        catch { _ins.OnQueryFailed(); throw; }
    }

    /// <summary>
    /// Asynchronously sends the command text and builds a <see cref="System.Data.Common.DbDataReader"/>.
    /// Emits timing telemetry and marks failures when exceptions are thrown.
    /// </summary>
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior, CancellationToken cancellationToken)
    {
        using var scope = _ins.StartQueryTimer();
        try { return await _inner.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false); }
        catch { _ins.OnQueryFailed(); throw; }
    }
}
