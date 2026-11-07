using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Data.Postgres;
using Joktan.Workflows.Engine;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

/// <summary>
/// The tables of the databases workflows use: those on Data / Database and the connections on Settings / Workflow
/// Settings. Like the browser on Data / Database, names are quoted and values sent as parameters; there is no SQL to type.
/// </summary>
public static class DatabaseTools
{
    private static readonly ToolParameter DatabaseName = P.Text("database", "The database's id or name, from database_list.");
    private static readonly ToolParameter Schema = P.OptText("schema", "The schema; public unless given.");
    private static readonly ToolParameter Table = P.Text("table", "The table.");

    public static IEnumerable<McpTool> Create(IWorkflowServices services) =>
    [
        Read("database_list", "List the databases on Data / Database and the connections on Settings / Workflow Settings, with the ids database steps name. Make a database with containers_database_save.", [], _ =>
            services.Databases().Select(d => new { d.Id, d.Name, Where = d.Detail }).ToList()),
        ReadAsync("database_tables", "List a database's schemas, their tables and each table's columns.", [DatabaseName, P.OptText("schema", "Only this schema.")], args =>
            WithDatabase(services, args, async connection =>
            {
                var schemas = args.OptText("schema") is { } only ? [only] : await connection.SchemasAsync();
                var result = new List<object>();
                foreach (var schema in schemas)
                {
                    var tables = new List<object>();
                    foreach (var table in await connection.TablesAsync(schema))
                    {
                        var columns = await connection.ColumnsAsync(schema, table);
                        tables.Add(new { Table = table, Columns = columns.Select(c => new { c.Name, c.Type, Nullable = c.IsNullable, c.Default, PrimaryKey = c.IsPrimaryKey, Numbered = c.IsIdentity }) });
                    }

                    result.Add(new { Schema = schema, Tables = tables });
                }

                return result;
            })),
        // Risky: a column's type and default are SQL, as on Data / Database.
        RiskyAsync("database_table_create",
            "Make a table, and its schema if there is none, as Data / Database does. With with_id it gets an id column numbered for each row, as its primary key.",
            [
                DatabaseName, Schema, Table, P.OptFlag("with_id", "Whether it gets a numbered id column as its primary key. On unless given."),
                P.List("columns", "Its columns: [{\"name\": \"status\", \"type\": \"text\", \"not_null\": false, \"unique\": false, \"default\": \"'new'\"}]. Types are PostgreSQL's, such as text, integer, numeric, boolean, timestamptz, uuid or jsonb; a default is SQL. " +
                    "A column an upsert step matches on must be unique."),
            ], args => WithDatabase(services, args, async connection =>
            {
                var schema = args.OptText("schema") ?? "public";
                var table = args.Text("table");
                var columns = Columns(args.Json("columns"));
                if ((await connection.TablesAsync(schema)).Contains(table))
                    throw new ToolFailure($"{schema}.{table} is there already; database_tables shows its columns.");
                if (!(await connection.SchemasAsync()).Contains(schema))
                    await connection.CreateSchemaAsync(schema);
                await connection.CreateTableAsync(schema, table, args.Flag("with_id") ?? true);
                foreach (var column in columns)
                    await connection.AddColumnAsync(schema, table, column);
                return new { Schema = schema, Table = table, Columns = (await connection.ColumnsAsync(schema, table)).Select(c => $"{c.Name} {c.Type}") };
            })),
        ReadAsync("database_rows", "Read the rows of a table, first rows first.", [DatabaseName, Schema, Table, P.OptNumber("limit", "How many; 50 unless given.")], args =>
            WithDatabase(services, args, async connection =>
            {
                var schema = args.OptText("schema") ?? "public";
                var table = args.Text("table");
                var columns = (await connection.ColumnsAsync(schema, table)).Select(c => c.Name).ToList();
                if (columns.Count == 0)
                    throw new ToolFailure($"There is no table {schema}.{table}; database_tables lists them.");
                var rows = await connection.RowsAsync(schema, table, columns, [], Math.Clamp(args.Whole("limit") ?? 50, 1, 1000), 0);
                return new { Count = await connection.CountAsync(schema, table), Rows = rows.Select(r => columns.Zip(r.Values).ToDictionary(p => p.First, p => p.Second)) };
            })),
        ChangeAsync("database_row_insert", "Insert a row into a table. Columns not given get their defaults.",
            [DatabaseName, Schema, Table, P.Map("values", "The row: column names and values, as text PostgreSQL reads as each column's type.")], args =>
                WithDatabase(services, args, async connection =>
                {
                    var values = args.Map("values").Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)).ToList();
                    if (values.Count == 0)
                        throw new ToolFailure("Give the row's values.");
                    await connection.InsertAsync(args.OptText("schema") ?? "public", args.Text("table"), values);
                    return "Inserted.";
                })),
    ];

    private static List<ColumnSpec> Columns(JsonNode? node)
    {
        if (node is not JsonArray list)
            throw new ToolFailure("columns must be a list of {name, type}.");
        var columns = new List<ColumnSpec>();
        foreach (var item in list)
        {
            var name = item?["name"]?.GetValue<string>().Trim() ?? "";
            var type = item?["type"]?.GetValue<string>().Trim() ?? "";
            if (name.Length == 0 || type.Length == 0)
                throw new ToolFailure("Each column needs a name and a type.");
            // UNIQUE goes after the type, as the type is SQL already.
            if (item?["unique"]?.GetValue<bool>() == true)
                type += " UNIQUE";
            columns.Add(new ColumnSpec(name, type, item?["not_null"]?.GetValue<bool>() ?? false, item?["default"]?.ToString() ?? ""));
        }

        return columns;
    }

    // Opens the database for one call; what PostgreSQL says when it refuses is given back to the agent.
    private static async Task<object?> WithDatabase(IWorkflowServices services, ToolArgs args, Func<IDatabaseConnection, Task<object?>> use)
    {
        var name = args.Text("database");
        var choice = services.Databases().FirstOrDefault(d => d.Id == name)
                     ?? services.Databases().FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                     ?? throw new ToolFailure($"There is no database {name}. database_list lists them.");
        var (connection, error) = services.OpenDatabase(choice.Id);
        Check(error);
        await using (connection!)
        {
            try
            {
                return await use(connection!);
            }
            catch (Exception e) when (e is Npgsql.NpgsqlException or InvalidOperationException)
            {
                throw new ToolFailure(e.Message);
            }
        }
    }
}
