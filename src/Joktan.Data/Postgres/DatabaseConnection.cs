using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace Joktan.Data.Postgres;

/// <summary>What the database browser does with one database. <see cref="PostgresConnection"/> does it over the network; tests use their own.</summary>
public interface IDatabaseConnection : IAsyncDisposable
{
    Task<IReadOnlyList<string>> SchemasAsync(CancellationToken cancel = default);

    Task<IReadOnlyList<string>> TablesAsync(string schema, CancellationToken cancel = default);

    Task<IReadOnlyList<TableColumn>> ColumnsAsync(string schema, string table, CancellationToken cancel = default);

    /// <summary>A page of rows, in the order of <paramref name="orderBy"/>, or of ctid when it is empty.</summary>
    Task<IReadOnlyList<TableRow>> RowsAsync(string schema, string table, IReadOnlyList<string> columns, IReadOnlyList<string> orderBy, int limit, int offset,
        CancellationToken cancel = default);

    Task<long> CountAsync(string schema, string table, CancellationToken cancel = default);

    Task CreateSchemaAsync(string name, CancellationToken cancel = default);

    Task CreateTableAsync(string schema, string name, bool withId, CancellationToken cancel = default);

    Task AddColumnAsync(string schema, string table, ColumnSpec column, CancellationToken cancel = default);

    Task DropSchemaAsync(string name, CancellationToken cancel = default);

    Task DropTableAsync(string schema, string table, CancellationToken cancel = default);

    Task DropColumnAsync(string schema, string table, string column, CancellationToken cancel = default);

    /// <summary>Inserts a row with these columns' values, as text; columns left out get their defaults.</summary>
    Task InsertAsync(string schema, string table, IReadOnlyList<KeyValuePair<string, string?>> values, CancellationToken cancel = default);

    /// <summary>Changes these columns of the row with this ctid to these values, as text.</summary>
    /// <returns>How many rows changed: 0 when the row had already changed or gone.</returns>
    Task<int> UpdateAsync(string schema, string table, string rowId, IReadOnlyList<KeyValuePair<string, string?>> values, CancellationToken cancel = default);

    /// <returns>How many rows went: 0 when the row had already changed or gone.</returns>
    Task<int> DeleteAsync(string schema, string table, string rowId, CancellationToken cancel = default);

    /// <summary>
    /// Runs statements that each give one text value, such as those of <see cref="RowQueries"/>, in one
    /// transaction, so that all of them happen or none; gives their values in order.
    /// </summary>
    Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<SqlText> statements, CancellationToken cancel = default);
}

/// <summary>A database on this computer, reached with Npgsql. Failures are thrown as <see cref="NpgsqlException"/>s, whose message can be shown.</summary>
public sealed class PostgresConnection : IDatabaseConnection
{
    private readonly NpgsqlDataSource _source;

    public PostgresConnection(string host, int port, string username, string password, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host, Port = port, Username = username, Password = password, Database = database,
            Timeout = 5, ApplicationName = "Joktan",
        };
        _source = NpgsqlDataSource.Create(builder);
    }

    public Task<IReadOnlyList<string>> SchemasAsync(CancellationToken cancel = default)
        => ReadAsync(PostgresSql.Schemas, [], r => r.GetString(0), cancel);

    public Task<IReadOnlyList<string>> TablesAsync(string schema, CancellationToken cancel = default)
        => ReadAsync(PostgresSql.Tables, [schema], r => r.GetString(0), cancel);

    public Task<IReadOnlyList<TableColumn>> ColumnsAsync(string schema, string table, CancellationToken cancel = default)
        => ReadAsync(PostgresSql.Columns, [schema, table],
            r => new TableColumn(r.GetString(0), r.GetString(1), r.GetBoolean(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetBoolean(4), r.GetBoolean(5)), cancel);

    public async Task<long> CountAsync(string schema, string table, CancellationToken cancel = default)
        => (await ReadAsync(PostgresSql.Count(schema, table), [], r => r.GetInt64(0), cancel))[0];

    public Task<IReadOnlyList<TableRow>> RowsAsync(string schema, string table, IReadOnlyList<string> columns, IReadOnlyList<string> orderBy, int limit, int offset,
        CancellationToken cancel = default)
        => ReadAsync(PostgresSql.SelectRows(schema, table, columns, orderBy, limit, offset), [], r =>
        {
            var values = new string?[columns.Count];
            for (var i = 0; i < values.Length; i++)
                values[i] = r.IsDBNull(i + 1) ? null : r.GetString(i + 1);
            return new TableRow(r.GetString(0), values);
        }, cancel);

    public Task CreateSchemaAsync(string name, CancellationToken cancel = default) => RunAsync(PostgresSql.CreateSchema(name), [], cancel);

    public Task CreateTableAsync(string schema, string name, bool withId, CancellationToken cancel = default)
        => RunAsync(PostgresSql.CreateTable(schema, name, withId), [], cancel);

    public Task AddColumnAsync(string schema, string table, ColumnSpec column, CancellationToken cancel = default)
        => RunAsync(PostgresSql.AddColumn(schema, table, column), [], cancel);

    public Task DropSchemaAsync(string name, CancellationToken cancel = default) => RunAsync(PostgresSql.DropSchema(name), [], cancel);

    public Task DropTableAsync(string schema, string table, CancellationToken cancel = default)
        => RunAsync(PostgresSql.DropTable(schema, table), [], cancel);

    public Task DropColumnAsync(string schema, string table, string column, CancellationToken cancel = default)
        => RunAsync(PostgresSql.DropColumn(schema, table, column), [], cancel);

    public Task InsertAsync(string schema, string table, IReadOnlyList<KeyValuePair<string, string?>> values, CancellationToken cancel = default)
    {
        var columns = new List<string>();
        var parameters = new List<string?>();
        foreach (var (column, value) in values)
        {
            columns.Add(column);
            parameters.Add(value);
        }

        return RunAsync(PostgresSql.Insert(schema, table, columns), parameters, cancel);
    }

    public Task<int> UpdateAsync(string schema, string table, string rowId, IReadOnlyList<KeyValuePair<string, string?>> values, CancellationToken cancel = default)
    {
        if (values.Count == 0)
            return Task.FromResult(0);
        var parameters = values.Select(v => v.Value).Append(rowId).ToList();
        return RunAsync(PostgresSql.Update(schema, table, values.Select(v => v.Key).ToList()), parameters, cancel);
    }

    public Task<int> DeleteAsync(string schema, string table, string rowId, CancellationToken cancel = default)
        => RunAsync(PostgresSql.Delete(schema, table), [rowId], cancel);

    public async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<SqlText> statements, CancellationToken cancel = default)
    {
        await using var connection = await _source.OpenConnectionAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);
        var values = new List<string>();
        foreach (var statement in statements)
        {
            await using var command = new NpgsqlCommand(statement.Sql, connection, transaction);
            AddParameters(command, statement.Parameters);
            values.Add(await command.ExecuteScalarAsync(cancel) as string ?? "");
        }

        await transaction.CommitAsync(cancel);
        return values;
    }

    private async Task<IReadOnlyList<T>> ReadAsync<T>(string sql, IReadOnlyList<string?> parameters, Func<NpgsqlDataReader, T> read, CancellationToken cancel)
    {
        await using var command = Command(sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancel);
        var list = new List<T>();
        while (await reader.ReadAsync(cancel))
            list.Add(read(reader));
        return list;
    }

    private async Task<int> RunAsync(string sql, IReadOnlyList<string?> parameters, CancellationToken cancel)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteNonQueryAsync(cancel);
    }

    // Values go as text of no stated type, so PostgreSQL reads each as its column's type, as it would a literal.
    private NpgsqlCommand Command(string sql, IReadOnlyList<string?> parameters)
    {
        var command = _source.CreateCommand(sql);
        AddParameters(command, parameters);
        return command;
    }

    private static void AddParameters(NpgsqlCommand command, IReadOnlyList<string?> parameters)
    {
        foreach (var value in parameters)
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Unknown, Value = (object?)value ?? DBNull.Value });
    }

    public ValueTask DisposeAsync() => _source.DisposeAsync();
}
