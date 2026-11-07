using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Data.Postgres;
using Npgsql;
using Xunit;

// A real server answers in time or the test fails; it needs no cancelling.
#pragma warning disable xUnit1051

namespace Joktan.Data.Tests.Postgres;

/// <summary>
/// Against a real PostgreSQL server, only when JOKTAN_TEST_POSTGRES_PORT names the port of one on
/// this computer, with user postgres, password secret and database test. Each run uses a schema of
/// its own and drops it.
/// </summary>
public class PostgresConnectionTests
{
    private const string Variable = "JOKTAN_TEST_POSTGRES_PORT";

    [Fact]
    public async Task Schemas_tables_columns_and_rows_go_to_a_real_server()
    {
        Assert.SkipUnless(int.TryParse(Environment.GetEnvironmentVariable(Variable), out var port), $"Set {Variable} to test against a real server.");
        var schema = "joktan test " + Guid.NewGuid().ToString("N")[..6];
        await using var connection = new PostgresConnection("localhost", port, "postgres", "secret", "test");
        try
        {
            await connection.CreateSchemaAsync(schema);
            Assert.Contains(schema, await connection.SchemasAsync());
            Assert.DoesNotContain("pg_catalog", await connection.SchemasAsync());

            await connection.CreateTableAsync(schema, "Orders", withId: true);
            await connection.CreateTableAsync(schema, "empty", withId: false);
            Assert.Equal(["Orders", "empty"], await connection.TablesAsync(schema));
            await connection.AddColumnAsync(schema, "Orders", new ColumnSpec("say \"what\"", "text"));
            await connection.AddColumnAsync(schema, "Orders", new ColumnSpec("qty", "integer", NotNull: true, Default: "1"));
            await connection.AddColumnAsync(schema, "Orders", new ColumnSpec("at", "timestamptz"));

            var columns = await connection.ColumnsAsync(schema, "Orders");
            Assert.Equal(["id", "say \"what\"", "qty", "at"], columns.Select(c => c.Name));
            Assert.True(columns[0] is { IsPrimaryKey: true, IsIdentity: true, IsNullable: false, Type: "bigint" });
            Assert.True(columns[2] is { IsNullable: false, Default: "1", Type: "integer" });

            // Text is read as each column's type; left out, a column gets its default.
            await connection.InsertAsync(schema, "Orders", [new("say \"what\"", "it's; DROP TABLE x"), new("qty", "5"), new("at", "2026-11-07 10:00+02")]);
            await connection.InsertAsync(schema, "Orders", [new("say \"what\"", null)]);
            await connection.InsertAsync(schema, "empty", []);
            var names = columns.Select(c => c.Name).ToList();
            var rows = await connection.RowsAsync(schema, "Orders", names, [], 200, 0);
            Assert.Equal(2, await connection.CountAsync(schema, "Orders"));
            Assert.Equal(["2"], (await connection.RowsAsync(schema, "Orders", names, ["id"], 1, 1)).Select(r => r.Values[0]));
            Assert.Equal(2, rows.Count);
            Assert.Equal(["1", "it's; DROP TABLE x", "5"], rows[0].Values.Take(3));
            Assert.StartsWith("2026-11-07 08:00:00", rows[0].Values[3]);
            Assert.Equal(["2", null, "1", null], rows[1].Values);

            var problem = await Assert.ThrowsAsync<PostgresException>(() => connection.InsertAsync(schema, "Orders", [new("qty", "many")]));
            Assert.Contains("integer", problem.MessageText);

            Assert.Equal(1, await connection.UpdateAsync(schema, "Orders", rows[1].Id, [new("say \"what\"", "changed"), new("qty", "7")]));
            Assert.Equal(0, await connection.UpdateAsync(schema, "Orders", rows[1].Id, [new("qty", "8")]));
            Assert.Equal(0, await connection.UpdateAsync(schema, "Orders", rows[1].Id, []));
            Assert.Equal(["2", "changed", "7", null], (await connection.RowsAsync(schema, "Orders", names, [], 200, 0))[1].Values);

            Assert.Equal(1, await connection.DeleteAsync(schema, "Orders", rows[0].Id));
            Assert.Equal(0, await connection.DeleteAsync(schema, "Orders", rows[0].Id));
            Assert.Single(await connection.RowsAsync(schema, "Orders", names, [], 200, 0));

            await connection.DropColumnAsync(schema, "Orders", "at");
            Assert.Equal(["id", "say \"what\"", "qty"], (await connection.ColumnsAsync(schema, "Orders")).Select(c => c.Name));
            await connection.DropTableAsync(schema, "empty");
            Assert.Equal(["Orders"], await connection.TablesAsync(schema));
            await connection.DropSchemaAsync(schema);
            Assert.DoesNotContain(schema, await connection.SchemasAsync());
        }
        finally
        {
            await using var cleanup = NpgsqlDataSource.Create($"Host=localhost;Port={port};Username=postgres;Password=secret;Database=test");
            await using var drop = cleanup.CreateCommand($"DROP SCHEMA IF EXISTS {PostgresSql.Quote(schema)} CASCADE");
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task A_server_that_is_not_there_says_so()
    {
        await using var connection = new PostgresConnection("localhost", 1, "postgres", "secret", "test");
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => connection.SchemasAsync());
        Assert.True(failure is NpgsqlException or System.Net.Sockets.SocketException, failure.GetType().Name);
        IReadOnlyList<KeyValuePair<string, string?>> none = [];
        await Assert.ThrowsAnyAsync<Exception>(() => connection.InsertAsync("s", "t", none));
    }
}
