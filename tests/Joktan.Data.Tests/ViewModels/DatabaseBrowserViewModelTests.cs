using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Containers;
using Joktan.Containers.Tests.ViewModels;
using Joktan.Containers.ViewModels;
using Joktan.Data.Postgres;
using Joktan.Data.ViewModels;
using Joktan.Tests.TestSupport;
using Npgsql;
using Xunit;

namespace Joktan.Data.Tests.ViewModels;

/// <summary>A database kept in memory, which does what the browser asks and remembers it.</summary>
internal sealed class FakeConnection : IDatabaseConnection
{
    private int _next;

    public Dictionary<string, Dictionary<string, (List<TableColumn> Columns, List<TableRow> Rows)>> Schemas { get; } = new() { ["public"] = [] };

    public List<string> Done { get; } = [];

    /// <summary>Thrown by the next call, as the server would.</summary>
    public Exception? Fails { get; set; }

    public bool Disposed { get; private set; }

    public void Table(string schema, string name, params TableColumn[] columns)
    {
        if (!Schemas.TryGetValue(schema, out var tables))
            Schemas[schema] = tables = [];
        tables[name] = ([.. columns], []);
    }

    private void Check()
    {
        if (Fails is { } failure)
        {
            Fails = null;
            throw failure;
        }
    }

    public Task<IReadOnlyList<string>> SchemasAsync(CancellationToken cancel = default)
    {
        Check();
        return Task.FromResult<IReadOnlyList<string>>(Schemas.Keys.Order().ToList());
    }

    public Task<IReadOnlyList<string>> TablesAsync(string schema, CancellationToken cancel = default)
    {
        Check();
        return Task.FromResult<IReadOnlyList<string>>(Schemas[schema].Keys.Order().ToList());
    }

    public Task<IReadOnlyList<TableColumn>> ColumnsAsync(string schema, string table, CancellationToken cancel = default)
    {
        Check();
        return Task.FromResult<IReadOnlyList<TableColumn>>(Schemas[schema][table].Columns.ToList());
    }

    public Task<IReadOnlyList<TableRow>> RowsAsync(string schema, string table, IReadOnlyList<string> columns, IReadOnlyList<string> orderBy, int limit, int offset,
        CancellationToken cancel = default)
    {
        Check();
        Reads.Add($"rows {offset}+{limit} by {(orderBy.Count > 0 ? string.Join(",", orderBy) : "ctid")}");
        return Task.FromResult<IReadOnlyList<TableRow>>(Schemas[schema][table].Rows.Skip(offset).Take(limit).ToList());
    }

    public Task<long> CountAsync(string schema, string table, CancellationToken cancel = default)
    {
        Check();
        return Task.FromResult<long>(Schemas[schema][table].Rows.Count);
    }

    /// <summary>Which pages were read.</summary>
    public List<string> Reads { get; } = [];

    public Task CreateSchemaAsync(string name, CancellationToken cancel = default)
    {
        Check();
        Schemas[name] = [];
        Done.Add($"schema {name}");
        return Task.CompletedTask;
    }

    public Task CreateTableAsync(string schema, string name, bool withId, CancellationToken cancel = default)
    {
        Check();
        Table(schema, name, withId ? [new TableColumn("id", "bigint", false, null, IsPrimaryKey: true, IsIdentity: true)] : []);
        Done.Add($"table {schema}.{name} {(withId ? "with id" : "bare")}");
        return Task.CompletedTask;
    }

    public Task AddColumnAsync(string schema, string table, ColumnSpec column, CancellationToken cancel = default)
    {
        Check();
        Schemas[schema][table].Columns.Add(new TableColumn(column.Name, column.Type, !column.NotNull, column.Default.Length > 0 ? column.Default : null));
        Done.Add($"column {column}");
        return Task.CompletedTask;
    }

    public Task InsertAsync(string schema, string table, IReadOnlyList<KeyValuePair<string, string?>> values, CancellationToken cancel = default)
    {
        Check();
        var (columns, rows) = Schemas[schema][table];
        var given = values.ToDictionary(v => v.Key, v => v.Value);
        rows.Add(new TableRow($"(0,{++_next})", columns.Select(c => given.TryGetValue(c.Name, out var v) ? v : c.IsIdentity ? _next.ToString() : c.Default).ToList()));
        Done.Add($"insert {string.Join(", ", values.Select(v => $"{v.Key}={v.Value ?? "NULL"}"))}");
        return Task.CompletedTask;
    }

    public Task<int> UpdateAsync(string schema, string table, string rowId, IReadOnlyList<KeyValuePair<string, string?>> values, CancellationToken cancel = default)
    {
        Check();
        var (columns, rows) = Schemas[schema][table];
        Done.Add($"update {rowId} {string.Join(", ", values.Select(v => $"{v.Key}={v.Value ?? "NULL"}"))}");
        var at = rows.FindIndex(r => r.Id == rowId);
        if (at < 0)
            return Task.FromResult(0);
        var given = values.ToDictionary(v => v.Key, v => v.Value);
        rows[at] = rows[at] with { Values = columns.Select((c, i) => given.TryGetValue(c.Name, out var v) ? v : rows[at].Values[i]).ToList() };
        return Task.FromResult(1);
    }

    public Task DropSchemaAsync(string name, CancellationToken cancel = default)
    {
        Check();
        Schemas.Remove(name);
        Done.Add($"drop schema {name}");
        return Task.CompletedTask;
    }

    public Task DropTableAsync(string schema, string table, CancellationToken cancel = default)
    {
        Check();
        Schemas[schema].Remove(table);
        Done.Add($"drop table {schema}.{table}");
        return Task.CompletedTask;
    }

    public Task DropColumnAsync(string schema, string table, string column, CancellationToken cancel = default)
    {
        Check();
        var (columns, rows) = Schemas[schema][table];
        var at = columns.FindIndex(c => c.Name == column);
        columns.RemoveAt(at);
        for (var i = 0; i < rows.Count; i++)
            rows[i] = rows[i] with { Values = rows[i].Values.Where((_, n) => n != at).ToList() };
        Done.Add($"drop column {column}");
        return Task.CompletedTask;
    }

    public Task<int> DeleteAsync(string schema, string table, string rowId, CancellationToken cancel = default)
    {
        Check();
        Done.Add($"delete {rowId}");
        return Task.FromResult(Schemas[schema][table].Rows.RemoveAll(r => r.Id == rowId));
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

public class DatabaseBrowserViewModelTests : FakeEngineTest
{
    private readonly FakeCredentialStore _credentials = new();
    private readonly List<(PostgresDatabase Database, string Password, FakeConnection Connection)> _opened = [];

    private FakeConnection Server { get; } = new();

    private (ContainersViewModel Containers, DatabaseBrowserViewModel Browser) Browser(params string[] names)
    {
        var databases = names.Select((n, i) => new PostgresDatabase { Name = n, Port = 5432 + i, Database = n, DataFolder = Folder.At("pg", n) }).ToList();
        foreach (var database in databases)
            _credentials.Saved[database.Account] = "secret";
        new ComposeSettings
        {
            ComposeFiles = [], Engine = ContainerEngine.Docker, DatabasesFolder = Folder.At("databases"), DatabasesDataRoot = Folder.At("pg"),
            Databases = databases,
        }.Save(DataFolder);
        var containers = new ContainersViewModel(DataFolder, credentials: _credentials);
        return (containers, new DatabaseBrowserViewModel(containers.Databases, (d, p) =>
        {
            _opened.Add((d, p, Server));
            return Server;
        }));
    }

    [Fact]
    public Task Choosing_a_database_shows_its_schemas_and_first_table() => Headless.Run(async () =>
    {
        Server.Table("public", "orders", new TableColumn("id", "bigint", false, null, true, true), new TableColumn("name", "text", true, null));
        Server.Table("public", "audit");
        Server.Table("shop", "items");
        var (_, browser) = Browser("app", "other");
        Assert.Equal(["app", "other"], browser.Choices.Select(d => d.Name));
        Assert.Null(browser.SelectedDatabase);
        Assert.False(browser.IsConnected);
        Assert.False(browser.RefreshCommand.CanExecute(null));

        browser.SelectedDatabase = browser.Choices[0];
        await browser.Work;

        var (database, password, _) = Assert.Single(_opened);
        Assert.Equal(("app", "secret"), (database.Name, password));
        Assert.True(browser.IsConnected);
        Assert.Equal(["public", "shop"], browser.Schemas);
        Assert.Equal("public", browser.SelectedSchema);
        Assert.Equal(["audit", "orders"], browser.Tables);
        Assert.Equal("audit", browser.SelectedTable);
        Assert.Equal("public.audit", browser.TableTitle);
        Assert.Equal("No rows.", browser.RowsText);

        browser.SelectedTable = "orders";
        await browser.Work;
        Assert.Equal(["id", "name"], browser.Heading);
        Assert.Equal(["id", "name"], browser.InsertFields.Select(f => f.Name));
        Assert.Equal(["bigint · default", "text"], browser.InsertFields.Select(f => f.Hint));

        browser.SelectedSchema = "shop";
        await browser.Work;
        Assert.Equal(["items"], browser.Tables);
        Assert.Equal("items", browser.SelectedTable);
    });

    [Fact]
    public Task Schemas_tables_and_columns_are_created_and_then_chosen() => Headless.Run(async () =>
    {
        var (_, browser) = Browser("app");
        browser.SelectedDatabase = browser.Choices[0];
        await browser.Work;
        Assert.True(browser.HasNoTables);
        Assert.False(browser.CreateSchemaCommand.CanExecute(null));
        Assert.False(browser.AddColumnCommand.CanExecute(null));

        browser.NewSchemaName = " shop ";
        await browser.CreateSchemaCommand.ExecuteAsync(null);
        Assert.Equal("shop", browser.SelectedSchema);
        Assert.Equal("", browser.NewSchemaName);
        Assert.Equal("Created the schema shop.", browser.Status);

        browser.NewTableName = "orders";
        await browser.CreateTableCommand.ExecuteAsync(null);
        Assert.Equal("orders", browser.SelectedTable);
        Assert.Equal("id", Assert.Single(browser.Columns).Name);

        browser.NewTableWithId = false;
        browser.NewTableName = "bare";
        await browser.CreateTableCommand.ExecuteAsync(null);
        Assert.Empty(browser.Columns);

        browser.NewColumnName = "placed";
        browser.NewColumnType = "timestamptz";
        browser.NewColumnNotNull = true;
        browser.NewColumnDefault = "now()";
        await browser.AddColumnCommand.ExecuteAsync(null);
        Assert.Equal("placed", Assert.Single(browser.Columns).Name);
        Assert.Equal(("", "", false), (browser.NewColumnName, browser.NewColumnDefault, browser.NewColumnNotNull));
        Assert.Equal("Added the column placed.", browser.Status);

        Assert.Equal([
            "schema shop", "table shop.orders with id", "table shop.bare bare",
            "column ColumnSpec { Name = placed, Type = timestamptz, NotNull = True, Default = now() }",
        ], Server.Done);
    });

    [Fact]
    public Task Rows_are_inserted_from_what_is_typed_and_deleted() => Headless.Run(async () =>
    {
        Server.Table("public", "orders", new TableColumn("id", "bigint", false, null, true, true), new TableColumn("name", "text", true, null),
            new TableColumn("qty", "integer", false, "1"));
        var (_, browser) = Browser("app");
        browser.SelectedDatabase = browser.Choices[0];
        await browser.Work;

        browser.InsertFields[1].Text = "pen";
        await browser.InsertCommand.ExecuteAsync(null);
        browser.InsertFields[1].IsNull = true;
        browser.InsertFields[1].Text = "ignored";
        browser.InsertFields[2].Text = "3";
        await browser.InsertCommand.ExecuteAsync(null);

        Assert.Equal(["insert name=pen", "insert name=NULL, qty=3"], Server.Done);
        Assert.Equal(2, browser.Rows.Count);
        Assert.Equal(["1", "pen", "1"], browser.Rows[0].Cells);
        Assert.Equal(["2", "NULL", "3"], browser.Rows[1].Cells);
        Assert.Equal("Rows 1–2 of 2.", browser.RowsText);
        Assert.All(browser.InsertFields, f => Assert.Equal(("", false), (f.Text, f.IsNull)));
        Assert.Equal("Inserted a row.", browser.Status);

        await browser.Rows[0].DeleteCommand.ExecuteAsync(null);
        Assert.Equal("delete (0,1)", Server.Done[^1]);
        Assert.Equal("1 row.", browser.RowsText);
        Assert.Equal("Deleted the row.", browser.Status);

        // A row that has gone already deletes nothing, and the rows are read again.
        var stale = browser.Rows[0];
        Server.Schemas["public"]["orders"].Rows.Clear();
        await stale.DeleteCommand.ExecuteAsync(null);
        Assert.StartsWith("The row had already changed", browser.Status);
        Assert.Empty(browser.Rows);
    });

    [Fact]
    public Task Rows_come_a_page_at_a_time_in_a_stable_order() => Headless.Run(async () =>
    {
        Server.Table("public", "big", new TableColumn("n", "integer", true, null));
        Server.Table("public", "keyed", new TableColumn("id", "bigint", false, null, IsPrimaryKey: true));
        Server.Schemas["public"]["big"].Rows.AddRange(Enumerable.Range(1, 250).Select(i => new TableRow($"(0,{i})", [i.ToString()])));
        var (_, browser) = Browser("app");
        browser.SelectedDatabase = browser.Choices[0];
        await browser.Work;

        Assert.Equal("big", browser.SelectedTable);
        Assert.Equal((100, 250L, 3), (browser.Rows.Count, browser.TotalRows, browser.PageCount));
        Assert.Equal("Rows 1–100 of 250.", browser.RowsText);
        Assert.Equal("Page 1 of 3", browser.PageNumberText);
        Assert.True(browser.HasPages);
        Assert.False(browser.PreviousPageCommand.CanExecute(null));
        Assert.Equal("rows 0+100 by ctid", Server.Reads[^1]);

        await browser.NextPageCommand.ExecuteAsync(null);
        Assert.Equal("101", browser.Rows[0].Cells[0]);
        Assert.Equal("Rows 101–200 of 250.", browser.RowsText);
        await browser.LastPageCommand.ExecuteAsync(null);
        Assert.Equal(("Rows 201–250 of 250.", "Page 3 of 3"), (browser.RowsText, browser.PageNumberText));
        Assert.False(browser.NextPageCommand.CanExecute(null));
        await browser.PreviousPageCommand.ExecuteAsync(null);
        Assert.Equal(1, browser.Page);
        await browser.FirstPageCommand.ExecuteAsync(null);
        Assert.Equal("1", browser.Rows[0].Cells[0]);

        // Another page size starts again from the first page.
        await browser.LastPageCommand.ExecuteAsync(null);
        browser.PageSize = 200;
        await browser.Work;
        Assert.Equal((0, 2, 200), (browser.Page, browser.PageCount, browser.Rows.Count));

        // The last page emptied goes back a page.
        await browser.LastPageCommand.ExecuteAsync(null);
        Server.Schemas["public"]["big"].Rows.RemoveRange(201, 49);
        await browser.Rows[0].DeleteCommand.ExecuteAsync(null);
        Assert.Equal(0, browser.Page);
        Assert.False(browser.HasPages);
        Assert.Equal("Rows 1–200 of 200.", browser.RowsText);

        // An edit ends when the page changes; a table with a primary key is read in its order.
        browser.PageSize = 25;
        await browser.Work;
        browser.Rows[0].EditCommand.Execute(null);
        await browser.NextPageCommand.ExecuteAsync(null);
        Assert.False(browser.IsEditingRow);
        browser.SelectedTable = "keyed";
        await browser.Work;
        Assert.Equal("rows 0+25 by id", Server.Reads[^1]);
        Assert.Equal(0, browser.Page);
    });

    [Fact]
    public Task Columns_are_as_wide_as_their_longest_value_or_heading() => Headless.Run(async () =>
    {
        Server.Table("public", "t", new TableColumn("id", "integer", true, null), new TableColumn("a_rather_long_column_name", "text", true, null),
            new TableColumn("note", "text", true, null));
        Server.Schemas["public"]["t"].Rows.Add(new TableRow("(0,1)", ["1", "x", new string('w', 30)]));
        Server.Schemas["public"]["t"].Rows.Add(new TableRow("(0,2)", ["2", null, new string('w', 500)]));
        var (_, browser) = Browser("app");
        browser.SelectedDatabase = browser.Choices[0];
        await browser.Work;

        var widths = browser.HeadingCells.Select(c => c.Width).ToList();
        Assert.Equal(60, widths[0]);
        Assert.InRange(widths[1], 180, 220);
        Assert.Equal(420, widths[2]);
        Assert.All(browser.Rows, r => Assert.Equal(widths, r.Shown.Select(c => c.Width)));
        Assert.Equal(["2", "NULL", new string('w', 500)], browser.Rows[1].Shown.Select(c => c.Text));

        // A column dragged to a width keeps it for every row, and on other pages, until fitted again.
        var note = browser.HeadingCells[2].Size;
        browser.SetColumnWidth(note, 90);
        Assert.All(browser.Rows, r => Assert.Equal(90, r.Shown[2].Width));
        browser.SetColumnWidth(note, 5);
        Assert.Equal(DatabaseBrowserViewModel.MinDraggedWidth, note.Width);
        browser.SetColumnWidth(note, 90);
        await browser.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(90, browser.HeadingCells[2].Width);

        browser.FitColumn(browser.HeadingCells[2].Size);
        Assert.Equal(420, browser.HeadingCells[2].Width);
        await browser.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(420, browser.HeadingCells[2].Width);

        // Another table starts fitted.
        browser.SetColumnWidth(browser.HeadingCells[0].Size, 300);
        Server.Table("public", "u", new TableColumn("id", "integer", true, null));
        await browser.RefreshCommand.ExecuteAsync(null);
        browser.SelectedTable = "u";
        await browser.Work;
        Assert.Equal(60, browser.HeadingCells[0].Width);
    });

    [Fact]
    public Task What_the_server_says_is_shown() => Headless.Run(async () =>
    {
        Server.Table("public", "orders", new TableColumn("qty", "integer", true, null));
        var (_, browser) = Browser("app");
        browser.SelectedDatabase = browser.Choices[0];
        await browser.Work;

        Server.Fails = new PostgresException("invalid input syntax for type integer: \"many\"", "ERROR", "ERROR", "22P02");
        browser.InsertFields[0].Text = "many";
        await browser.InsertCommand.ExecuteAsync(null);
        Assert.Equal("invalid input syntax for type integer: \"many\"", browser.Error);
        Assert.Null(browser.Status);
        Assert.Equal("many", browser.InsertFields[0].Text);
        Assert.False(browser.IsWorking);

        Server.Fails = new NpgsqlException("Failed to connect to 127.0.0.1:5432");
        await browser.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Failed to connect to 127.0.0.1:5432", browser.Error);

        await browser.RefreshCommand.ExecuteAsync(null);
        Assert.Null(browser.Error);
        Assert.Equal("orders", browser.SelectedTable);
    });

    [Fact]
    public Task A_database_without_a_password_cannot_be_opened() => Headless.Run(async () =>
    {
        var (_, browser) = Browser("app");
        _credentials.Saved.Clear();

        browser.SelectedDatabase = browser.Choices[0];
        await browser.Work;

        Assert.Equal("No password is saved for app. Edit it and give one.", browser.Error);
        Assert.False(browser.IsConnected);
        Assert.Empty(_opened);

        // Refresh tries again, once one is saved.
        _credentials.Saved[browser.Choices[0].Account] = "secret";
        await browser.RefreshCommand.ExecuteAsync(null);
        Assert.True(browser.IsConnected);
    });

    [Fact]
    public Task Changing_or_removing_the_database_chosen_lets_go_of_it() => Headless.Run(async () =>
    {
        var (containers, browser) = Browser("app", "other");
        browser.SelectedDatabase = browser.Choices[0];
        await browser.Work;
        Assert.Single(_opened);

        // Saved with the same way in, it stays open.
        containers.Databases.Items[0].EditCommand.Execute(null);
        await containers.Databases.Editor!.SaveDatabaseAsync();
        await browser.Work;
        Assert.Equal("app", browser.SelectedDatabase!.Name);
        Assert.Single(_opened);

        // Another port opens it again.
        containers.Databases.Items[0].EditCommand.Execute(null);
        containers.Databases.Editor!.PortText = "6000";
        await containers.Databases.Editor.SaveDatabaseAsync();
        await browser.Work;
        Assert.Equal(2, _opened.Count);
        Assert.Equal(6000, _opened[1].Database.Port);

        await containers.Databases.Items[0].ConfirmRemoveCommand.ExecuteAsync(null);
        await browser.Work;
        Assert.Null(browser.SelectedDatabase);
        Assert.False(browser.IsConnected);
        Assert.Empty(browser.Schemas);
        Assert.True(Server.Disposed);
        Assert.Equal(["other"], browser.Choices.Select(d => d.Name));
    });

    [Fact]
    public Task A_row_is_edited_in_the_form_and_only_what_changed_is_saved() => Headless.Run(async () =>
    {
        Server.Table("public", "orders", new TableColumn("id", "bigint", false, null, true, true), new TableColumn("name", "text", true, null),
            new TableColumn("qty", "integer", false, "1"));
        var (_, browser) = Browser("app");
        browser.SelectedDatabase = browser.Choices[0];
        await browser.Work;
        browser.InsertFields[1].Text = "pen";
        await browser.InsertCommand.ExecuteAsync(null);
        Assert.Equal("Insert a row", browser.FormTitle);
        Assert.False(browser.SaveRowCommand.CanExecute(null));

        browser.Rows[0].EditCommand.Execute(null);
        Assert.True(browser.IsEditingRow);
        Assert.Equal("Edit the row", browser.FormTitle);
        Assert.Equal(["1", "pen", "1"], browser.InsertFields.Select(f => f.Text));
        browser.InsertFields[1].IsNull = true;
        browser.InsertFields[2].Text = "4";
        await browser.SaveRowCommand.ExecuteAsync(null);

        Assert.Equal("update (0,1) name=NULL, qty=4", Server.Done[^1]);
        Assert.Equal(["1", "NULL", "4"], browser.Rows[0].Cells);
        Assert.False(browser.IsEditingRow);
        Assert.Equal("Saved the row.", browser.Status);
        Assert.All(browser.InsertFields, f => Assert.Equal("", f.Text));

        // Nothing changed sends nothing; a NULL left NULL is not a change.
        browser.Rows[0].EditCommand.Execute(null);
        Assert.True(browser.InsertFields[1].IsNull);
        await browser.SaveRowCommand.ExecuteAsync(null);
        Assert.Equal("Nothing was changed.", browser.Status);
        Assert.Equal(2, Server.Done.Count);

        // Cancel leaves the row; a row gone in the meantime saves nothing.
        browser.Rows[0].EditCommand.Execute(null);
        browser.InsertFields[2].Text = "9";
        browser.CancelEditCommand.Execute(null);
        Assert.False(browser.IsEditingRow);
        Assert.Equal("", browser.InsertFields[2].Text);
        browser.Rows[0].EditCommand.Execute(null);
        browser.InsertFields[2].Text = "9";
        Server.Schemas["public"]["orders"].Rows.Clear();
        await browser.SaveRowCommand.ExecuteAsync(null);
        Assert.StartsWith("The row had already changed or gone, so nothing was saved", browser.Status);
        Assert.Empty(browser.Rows);

        // Deleting the row being edited ends the edit.
        browser.InsertFields[1].Text = "cup";
        await browser.InsertCommand.ExecuteAsync(null);
        browser.Rows[0].EditCommand.Execute(null);
        await browser.Rows[0].DeleteCommand.ExecuteAsync(null);
        Assert.False(browser.IsEditingRow);
    });

    [Fact]
    public Task Schemas_tables_and_columns_are_dropped_once_confirmed() => Headless.Run(async () =>
    {
        Server.Table("public", "orders", new TableColumn("id", "bigint", false, null, true, true), new TableColumn("note", "text", true, null));
        Server.Table("public", "audit");
        Server.Table("shop", "items");
        var (_, browser) = Browser("app");
        Assert.False(browser.DropSchemaCommand.CanExecute(null));
        browser.SelectedDatabase = browser.Choices[0];
        await browser.Work;
        browser.SelectedTable = "orders";
        await browser.Work;

        browser.Columns[1].DropCommand.Execute(null);
        Assert.True(browser.IsConfirmingDrop);
        Assert.StartsWith("Drop the column note of public.orders", browser.Dropping!.Text);
        browser.CancelDropCommand.Execute(null);
        Assert.False(browser.IsConfirmingDrop);
        Assert.Empty(Server.Done);

        browser.Columns[1].DropCommand.Execute(null);
        await browser.ConfirmDropCommand.ExecuteAsync(null);
        Assert.Equal("drop column note", Server.Done[^1]);
        Assert.Equal(["id"], browser.Columns.Select(c => c.Name));
        Assert.Equal("Dropped the column note.", browser.Status);

        browser.DropTableCommand.Execute(null);
        Assert.StartsWith("Drop the table public.orders", browser.Dropping!.Text);
        await browser.ConfirmDropCommand.ExecuteAsync(null);
        Assert.Equal(["audit"], browser.Tables);
        Assert.Equal("audit", browser.SelectedTable);

        browser.SelectedSchema = "shop";
        await browser.Work;
        browser.DropSchemaCommand.Execute(null);
        Assert.StartsWith("Drop the schema shop, with every table", browser.Dropping!.Text);
        await browser.ConfirmDropCommand.ExecuteAsync(null);
        Assert.Equal(["public"], browser.Schemas);
        Assert.Equal("public", browser.SelectedSchema);
        Assert.Equal(["drop column note", "drop table public.orders", "drop schema shop"], Server.Done);

        // What the server refuses is shown, and nothing more is asked.
        browser.DropSchemaCommand.Execute(null);
        Server.Fails = new PostgresException("must be owner of schema public", "ERROR", "ERROR", "42501");
        await browser.ConfirmDropCommand.ExecuteAsync(null);
        Assert.Equal("must be owner of schema public", browser.Error);
        Assert.False(browser.IsConfirmingDrop);

        // Choosing another database forgets what was to be dropped.
        browser.DropSchemaCommand.Execute(null);
        browser.SelectedDatabase = null;
        await browser.Work;
        Assert.False(browser.IsConfirmingDrop);
        await browser.ConfirmDropCommand.ExecuteAsync(null);
    });
}
