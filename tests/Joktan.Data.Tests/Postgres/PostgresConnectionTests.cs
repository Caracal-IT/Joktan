using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
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
    public async Task A_workflows_reads_inserts_and_updates_go_to_a_real_server()
    {
        Assert.SkipUnless(int.TryParse(Environment.GetEnvironmentVariable(Variable), out var port), $"Set {Variable} to test against a real server.");
        var schema = "joktan rows " + Guid.NewGuid().ToString("N")[..6];
        await using var connection = new PostgresConnection("localhost", port, "postgres", "secret", "test");
        try
        {
            await connection.CreateSchemaAsync(schema);
            await connection.CreateTableAsync(schema, "orders", withId: true);
            await connection.AddColumnAsync(schema, "orders", new ColumnSpec("name", "text"));
            await connection.AddColumnAsync(schema, "orders", new ColumnSpec("qty", "integer", Default: "1"));
            await connection.AddColumnAsync(schema, "orders", new ColumnSpec("paid", "boolean"));
            await connection.AddColumnAsync(schema, "orders", new ColumnSpec("extra", "jsonb"));

            // Values typed as text become each column's type, and come back as JSON of that type.
            var inserted = await connection.RunAsync(RowQueries.Insert(schema, "orders", ["name", "qty", "paid", "extra"],
                [["tea", "2", "true", """{"cup": "big"}"""], ["cake", "5", "false", null], ["it's", "9", null, "[1,2]"]]));
            SameJson("""[{"id":1,"name":"tea","qty":2,"paid":true,"extra":{"cup":"big"}}, {"id":2,"name":"cake","qty":5,"paid":false,"extra":null}, {"id":3,"name":"it's","qty":9,"paid":null,"extra":[1,2]}]""", Assert.Single(inserted));

            var read = await connection.RunAsync([RowQueries.Select(schema, "orders", ["name", "qty"], [new("qty", FilterKind.GreaterOrEqual, "5")], "qty", descending: true)]);
            SameJson("""[{"name":"it's","qty":9}, {"name":"cake","qty":5}]""", read[0]);
            Assert.Equal("[]", (await connection.RunAsync([RowQueries.Select(schema, "orders", [], [new("name", FilterKind.Equal, "none")])]))[0]);
            Assert.Contains("\"cake\"", (await connection.RunAsync([RowQueries.Select(schema, "orders", ["name"], [new("paid", FilterKind.NotEqual, "true"), new("extra", FilterKind.IsEmpty)])]))[0]);
            Assert.Contains("\"tea\"", (await connection.RunAsync([RowQueries.Select(schema, "orders", ["name"], [new("extra", FilterKind.Contains, "big"), new("name", FilterKind.StartsWith, "t")])]))[0]);

            var updated = await connection.RunAsync([RowQueries.Update(schema, "orders", [new("qty", "6"), new("paid", null)], [new("name", FilterKind.Equal, "cake")])]);
            SameJson("""[{"id":2,"name":"cake","qty":6,"paid":null,"extra":null}]""", updated[0]);

            var upserted = await connection.RunAsync(RowQueries.Upsert(schema, "orders", ["id", "name", "qty"], ["id"], [["2", "cake", "8"], ["10", "pie", "1"]]));
            SameJson("""[{"id":2,"name":"cake","qty":8,"paid":null,"extra":null}, {"id":10,"name":"pie","qty":1,"paid":null,"extra":null}]""", upserted[0]);
            Assert.Equal(4, await connection.CountAsync(schema, "orders"));
            var deleted = await connection.RunAsync([RowQueries.Delete(schema, "orders", [new("id", FilterKind.Equal, "10")])]);
            SameJson("""[{"id":10,"name":"pie","qty":1,"paid":null,"extra":null}]""", deleted[0]);

            // A failure part of the way through leaves nothing done.
            await Assert.ThrowsAsync<PostgresException>(() => connection.RunAsync([.. RowQueries.Insert(schema, "orders", ["name"], [["more"]]),
                RowQueries.Update(schema, "orders", [new("qty", "lots")], [new("id", FilterKind.Greater, "0")])]));
            Assert.Equal(3, await connection.CountAsync(schema, "orders"));
        }
        finally
        {
            await using var cleanup = NpgsqlDataSource.Create($"Host=localhost;Port={port};Username=postgres;Password=secret;Database=test");
            await using var drop = cleanup.CreateCommand($"DROP SCHEMA IF EXISTS {PostgresSql.Quote(schema)} CASCADE");
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static void SameJson(string expected, string actual)
        => Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), actual);

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
