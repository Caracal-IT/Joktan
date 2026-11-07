using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Agent.Tools;
using Joktan.Data.Postgres;
using Joktan.Workflows.Tests;
using Xunit;

namespace Joktan.Agent.Tests.Tools;

public sealed class DatabaseToolsTests
{
    private readonly FakeWorkflowServices _services = new();
    private readonly FakeDatabase _database;

    public DatabaseToolsTests() => _database = _services.AddDatabase("db1", "Demo");

    private async Task<JsonNode?> Call(string name, string arguments)
    {
        var tool = DatabaseTools.Create(_services).Single(t => t.Name == name);
        using var json = JsonDocument.Parse(arguments);
        return JsonSerializer.SerializeToNode(await tool.RunAsync(new ToolArgs(json.RootElement.Clone())));
    }

    [Fact]
    public async Task Makes_a_table_with_its_schema_and_columns()
    {
        await Call("database_table_create", """
            {"database": "Demo", "schema": "demo", "table": "orders",
             "columns": [{"name": "item", "type": "text", "not_null": true}, {"name": "qty", "type": "integer", "default": "1"}]}
            """);

        var columns = _database.Schemas["demo"]["orders"];
        Assert.Equal(["id", "item", "qty"], columns.Select(c => c.Name));
        Assert.False(columns[1].IsNullable);
        Assert.Equal("1", columns[2].Default);

        // The connection is closed after each call.
        Assert.Equal(1, _database.Disposed);
    }

    [Fact]
    public async Task Makes_a_column_unique_for_an_upsert_to_match_on()
    {
        await Call("database_table_create", """{"database": "Demo", "table": "orders", "columns": [{"name": "order_ref", "type": "text", "unique": true}]}""");

        Assert.Equal("text UNIQUE", _database.Schemas["public"]["orders"][1].Type);
    }

    [Fact]
    public async Task Lists_tables_by_schema()
    {
        _database.Table("orders", new TableColumn("id", "bigint", false, null, IsPrimaryKey: true));

        var listed = await Call("database_tables", """{"database": "db1"}""");

        Assert.Equal("orders", listed![0]!["Tables"]![0]!["Table"]!.GetValue<string>());
    }

    [Fact]
    public async Task Refuses_a_table_that_is_there_already()
    {
        _database.Table("public", "orders");

        await Assert.ThrowsAsync<ToolFailure>(() => Call("database_table_create", """{"database": "Demo", "table": "orders", "columns": []}"""));
    }

    [Fact]
    public async Task Making_a_table_asks_as_its_types_are_sql()
    {
        Assert.Equal(ToolRisk.Risky, DatabaseTools.Create(_services).Single(t => t.Name == "database_table_create").Risk);
        await Assert.ThrowsAsync<ToolFailure>(() => Call("database_tables", """{"database": "nowhere"}"""));
    }
}
