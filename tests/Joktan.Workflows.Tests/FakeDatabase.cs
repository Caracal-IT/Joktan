using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Data.Postgres;

namespace Joktan.Workflows.Tests;

/// <summary>A database that lives in the test: its tables' columns are given, and each statement sent is kept and answered by <see cref="Answer"/>.</summary>
public sealed class FakeDatabase : IDatabaseConnection
{
    /// <summary>The columns of each table, by schema and table.</summary>
    public Dictionary<string, Dictionary<string, List<TableColumn>>> Schemas { get; } = new() { ["public"] = [] };

    public ConcurrentQueue<SqlText> Sent { get; } = new();

    /// <summary>What a statement gives; an empty list by default.</summary>
    public Func<SqlText, string> Answer { get; set; } = _ => "[]";

    /// <summary>Thrown by every call, to act as a server that is down or refuses.</summary>
    public Exception? Throws { get; set; }

    public int Opened { get; set; }

    public int Disposed { get; private set; }

    public FakeDatabase Table(string table, params TableColumn[] columns) => Table("public", table, columns);

    public FakeDatabase Table(string schema, string table, params TableColumn[] columns)
    {
        if (!Schemas.TryGetValue(schema, out var tables))
            Schemas[schema] = tables = [];
        tables[table] = [.. columns];
        return this;
    }

    private void Check()
    {
        if (Throws is not null)
            throw Throws;
    }

    public Task<IReadOnlyList<string>> SchemasAsync(CancellationToken cancel = default)
    {
        Check();
        return Task.FromResult<IReadOnlyList<string>>(Schemas.Keys.Order().ToList());
    }

    public Task<IReadOnlyList<string>> TablesAsync(string schema, CancellationToken cancel = default)
    {
        Check();
        return Task.FromResult<IReadOnlyList<string>>(Schemas.GetValueOrDefault(schema)?.Keys.Order().ToList() ?? []);
    }

    public Task<IReadOnlyList<TableColumn>> ColumnsAsync(string schema, string table, CancellationToken cancel = default)
    {
        Check();
        return Task.FromResult<IReadOnlyList<TableColumn>>(Schemas.GetValueOrDefault(schema)?.GetValueOrDefault(table) ?? []);
    }

    public Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<SqlText> statements, CancellationToken cancel = default)
    {
        Check();
        var answers = new List<string>();
        foreach (var statement in statements)
        {
            Sent.Enqueue(statement);
            answers.Add(Answer(statement));
        }

        return Task.FromResult<IReadOnlyList<string>>(answers);
    }

    public ValueTask DisposeAsync()
    {
        Disposed++;
        return ValueTask.CompletedTask;
    }

    public Task<IReadOnlyList<TableRow>> RowsAsync(string schema, string table, IReadOnlyList<string> columns, IReadOnlyList<string> orderBy, int limit, int offset,
        CancellationToken cancel = default) => throw new NotSupportedException();

    public Task<long> CountAsync(string schema, string table, CancellationToken cancel = default) => throw new NotSupportedException();

    public Task CreateSchemaAsync(string name, CancellationToken cancel = default) => throw new NotSupportedException();

    public Task CreateTableAsync(string schema, string name, bool withId, CancellationToken cancel = default) => throw new NotSupportedException();

    public Task AddColumnAsync(string schema, string table, ColumnSpec column, CancellationToken cancel = default) => throw new NotSupportedException();

    public Task DropSchemaAsync(string name, CancellationToken cancel = default) => throw new NotSupportedException();

    public Task DropTableAsync(string schema, string table, CancellationToken cancel = default) => throw new NotSupportedException();

    public Task DropColumnAsync(string schema, string table, string column, CancellationToken cancel = default) => throw new NotSupportedException();

    public Task InsertAsync(string schema, string table, IReadOnlyList<KeyValuePair<string, string?>> values, CancellationToken cancel = default)
        => throw new NotSupportedException();

    public Task<int> UpdateAsync(string schema, string table, string rowId, IReadOnlyList<KeyValuePair<string, string?>> values, CancellationToken cancel = default)
        => throw new NotSupportedException();

    public Task<int> DeleteAsync(string schema, string table, string rowId, CancellationToken cancel = default) => throw new NotSupportedException();
}
