using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Containers;
using Joktan.Containers.ViewModels;
using Joktan.Core;
using Joktan.Data.Postgres;
using Joktan.Data.Resources;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Joktan.Data.ViewModels;

/// <summary>How wide a column of the rows' table is, shared by its heading and its cells.</summary>
public partial class ColumnSize(string name, double width) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial double Width { get; set; } = width;
}

/// <summary>A value or heading in the rows' table, as wide as its column.</summary>
public sealed record BrowserCell(string Text, ColumnSize Size)
{
    public double Width => Size.Width;
}

/// <summary>A row of the table shown, with its values as shown: NULL for null.</summary>
public partial class BrowserRow(TableRow row, DatabaseBrowserViewModel browser)
{
    public TableRow Row { get; } = row;

    public IReadOnlyList<string> Cells { get; } = row.Values.Select(v => v ?? "NULL").ToList();

    /// <summary>The cells, each as wide as its column. Set once the page is read.</summary>
    public IReadOnlyList<BrowserCell> Shown { get; internal set; } = [];

    [RelayCommand]
    private void Edit() => browser.EditRow(this);

    [RelayCommand]
    private Task DeleteAsync() => browser.DeleteRowAsync(this);
}

/// <summary>A column of the table shown, as a row of the columns' table.</summary>
public partial class BrowserColumn(TableColumn column, DatabaseBrowserViewModel browser)
{
    public TableColumn Column { get; } = column;

    public string Name => Column.Name;

    public string Type => Column.Type;

    public string NullText => Column.NullText;

    public string DefaultText => Column.DefaultText;

    public string KeyText => Column.KeyText;

    [RelayCommand]
    private void Drop() => browser.AskDropColumn(this);
}

/// <summary>A schema, table or column to drop once confirmed, and what the confirmation says.</summary>
public sealed record PendingDrop(string Text, Func<IDatabaseConnection, Task> Drop, Func<Task> Reload, string Done);

/// <summary>
/// A value to insert into one column, or to change it to in the row being edited. Left empty when
/// inserting, the column gets its default.
/// </summary>
public partial class InsertField(TableColumn column) : ObservableObject
{
    public TableColumn Column { get; } = column;

    /// <summary>The value the row being edited had, null for NULL; not used when inserting.</summary>
    public string? Original { get; init; }

    /// <summary>The value to save: null for NULL.</summary>
    public string? Value => IsNull ? null : Text;

    public string Name => Column.Name;

    /// <summary>What it holds, shown in the empty box, such as "integer · default".</summary>
    public string Hint => Column.Default is not null || Column.IsIdentity ? string.Format(Strings.InsertField_Default, Column.Type) : Column.Type;

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>Whether to insert NULL, whatever is typed.</summary>
    [ObservableProperty]
    public partial bool IsNull { get; set; }
}

/// <summary>
/// The database browser on Data / Database: pick one of the databases, then its schemas, tables,
/// columns and first rows are shown. Schemas, tables and columns can be added and dropped, and rows
/// inserted, edited and deleted. Each change is sent at once; nothing is kept here but what was read.
/// </summary>
public partial class DatabaseBrowserViewModel : ObservableObject
{
    /// <summary>How many rows a page has, to choose from.</summary>
    public IReadOnlyList<int> PageSizes { get; } = [25, 50, 100, 200, 500];

    // A column is as wide as its longest value on the page, or its heading, within these bounds.
    private const double MinColumnWidth = 60, MaxColumnWidth = 420, CharacterWidth = 7.8, HeadingCharacterWidth = 7.4, CellPadding = 16;

    /// <summary>The narrowest and widest a column can be dragged to.</summary>
    public const double MinDraggedWidth = 40, MaxDraggedWidth = 1600;

    // Widths dragged to, by column, kept while the same table is shown.
    private readonly Dictionary<string, double> _draggedWidths = [];
    private (string Schema, string Table)? _widthsOf;

    private readonly PostgresDatabasesViewModel _databases;
    private readonly Func<PostgresDatabase, string, IDatabaseConnection> _connect;
    private IDatabaseConnection? _connection;

    // Set while the lists are filled in, so that choosing in them does not read again.
    private bool _filling;

    /// <param name="connect">Opens a database with its password; over the network otherwise.</param>
    public DatabaseBrowserViewModel(PostgresDatabasesViewModel databases, Func<PostgresDatabase, string, IDatabaseConnection>? connect = null)
    {
        _databases = databases;
        _connect = connect ?? ((d, password) => new PostgresConnection("localhost", d.Port, d.Username, password, d.Database));
        DatabasesChanged();
        databases.Items.CollectionChanged += (_, _) => DatabasesChanged();
        Tables.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoTables));
        Columns.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoColumns));
    }

    /// <summary>The databases on the tab, to choose from.</summary>
    public ObservableCollection<PostgresDatabase> Choices { get; } = [];

    public bool HasChoices => Choices.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial PostgresDatabase? SelectedDatabase { get; set; }

    public bool IsConnected => SelectedDatabase is not null && _connection is not null;

    public ObservableCollection<string> Schemas { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSchema))]
    [NotifyCanExecuteChangedFor(nameof(CreateTableCommand), nameof(DropSchemaCommand))]
    public partial string? SelectedSchema { get; set; }

    public bool HasSchema => SelectedSchema is not null;

    public ObservableCollection<string> Tables { get; } = [];

    public bool HasNoTables => Tables.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTable))]
    [NotifyPropertyChangedFor(nameof(TableTitle))]
    [NotifyCanExecuteChangedFor(nameof(AddColumnCommand), nameof(InsertCommand), nameof(DropTableCommand))]
    public partial string? SelectedTable { get; set; }

    public bool HasTable => SelectedTable is not null;

    public string TableTitle => SelectedTable is null ? "" : $"{SelectedSchema}.{SelectedTable}";

    public ObservableCollection<BrowserColumn> Columns { get; } = [];

    public bool HasNoColumns => Columns.Count == 0;

    /// <summary>What Drop was pressed for, waiting to be confirmed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingDrop))]
    public partial PendingDrop? Dropping { get; set; }

    public bool IsConfirmingDrop => Dropping is not null;

    /// <summary>The columns' names, as the rows' heading.</summary>
    public ObservableCollection<string> Heading { get; } = [];

    /// <summary>The heading, each name as wide as its column.</summary>
    public ObservableCollection<BrowserCell> HeadingCells { get; } = [];

    [ObservableProperty]
    public partial int PageSize { get; set; } = 100;

    /// <summary>The page shown, from 0.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageNumberText))]
    [NotifyCanExecuteChangedFor(nameof(FirstPageCommand), nameof(PreviousPageCommand), nameof(NextPageCommand), nameof(LastPageCommand))]
    public partial int Page { get; set; }

    /// <summary>How many rows the table has.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageCount), nameof(PageNumberText), nameof(HasPages))]
    [NotifyCanExecuteChangedFor(nameof(FirstPageCommand), nameof(PreviousPageCommand), nameof(NextPageCommand), nameof(LastPageCommand))]
    public partial long TotalRows { get; set; }

    public int PageCount => (int)Math.Max(1, (TotalRows + PageSize - 1) / PageSize);

    public bool HasPages => PageCount > 1;

    public string PageNumberText => string.Format(Strings.DatabaseBrowser_PageOf, Page + 1, PageCount);

    public ObservableCollection<BrowserRow> Rows { get; } = [];

    public ObservableCollection<InsertField> InsertFields { get; } = [];

    /// <summary>The row whose values are in the form, to be saved in place of inserting a new one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingRow), nameof(FormTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveRowCommand))]
    public partial BrowserRow? EditingRow { get; set; }

    public bool IsEditingRow => EditingRow is not null;

    public string FormTitle => IsEditingRow ? Strings.DataView_Browse_EditARow : Strings.DataView_Browse_InsertARow;

    /// <summary>How many rows are shown, and that there may be more.</summary>
    [ObservableProperty]
    public partial string RowsText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(CreateSchemaCommand), nameof(CreateTableCommand), nameof(AddColumnCommand), nameof(InsertCommand), nameof(SaveRowCommand))]
    public partial bool IsWorking { get; set; }

    /// <summary>What went wrong last, as PostgreSQL or the network said.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>What was done last, such as "Inserted a row".</summary>
    [ObservableProperty]
    public partial string? Status { get; set; }

    /// <summary>What is being read or changed now, for tests to wait on.</summary>
    public Task Work { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateSchemaCommand))]
    public partial string NewSchemaName { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateTableCommand))]
    public partial string NewTableName { get; set; } = "";

    /// <summary>Whether a new table starts with an id column, numbered for each row, as its primary key.</summary>
    [ObservableProperty]
    public partial bool NewTableWithId { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddColumnCommand))]
    public partial string NewColumnName { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddColumnCommand))]
    public partial string NewColumnType { get; set; } = "text";

    [ObservableProperty]
    public partial bool NewColumnNotNull { get; set; }

    /// <summary>An SQL expression, such as <c>now()</c> or <c>'none'</c>; empty for no default.</summary>
    [ObservableProperty]
    public partial string NewColumnDefault { get; set; } = "";

    public IReadOnlyList<string> ColumnTypes => PostgresSql.CommonTypes;

    // Keeps the choices in step with the tab's list, and lets go of a database that was removed.
    private void DatabasesChanged()
    {
        var selected = SelectedDatabase?.Id;
        _filling = true;
        try
        {
            Choices.Clear();
            foreach (var database in _databases.Databases)
                Choices.Add(database);
        }
        finally
        {
            _filling = false;
        }

        OnPropertyChanged(nameof(HasChoices));
        var again = Choices.FirstOrDefault(d => d.Id == selected);
        if (again is null)
        {
            SelectedDatabase = null;
            return;
        }

        // A database saved again is a new object: keep it chosen, and connect again if how changed.
        var changed = SelectedDatabase is { } old && (old.Port != again.Port || old.Username != again.Username || old.Database != again.Database);
        _filling = !changed;
        try
        {
            SelectedDatabase = again;
        }
        finally
        {
            _filling = false;
        }
    }

    partial void OnSelectedDatabaseChanged(PostgresDatabase? value)
    {
        if (_filling)
            return;
        Work = ConnectAsync(value);
    }

    partial void OnSelectedSchemaChanged(string? value)
    {
        if (!_filling)
            Work = Run(_ => LoadTablesAsync(null));
    }

    partial void OnSelectedTableChanged(string? value)
    {
        if (!_filling)
            Work = Run(_ => LoadTableAsync());
    }

    private async Task ConnectAsync(PostgresDatabase? database)
    {
        if (_connection is { } old)
        {
            _connection = null;
            await old.DisposeAsync();
        }

        Fill(Schemas, [], () => SelectedSchema = null);
        Fill(Tables, [], () => SelectedTable = null);
        ClearTable();
        Dropping = null;
        Error = null;
        Status = null;
        OnPropertyChanged(nameof(IsConnected));
        if (database is null)
            return;

        if (_databases.PasswordOf(database) is not { Length: > 0 } password)
        {
            Error = string.Format(Strings.DatabaseBrowser_NoPasswordIsSaved, database.Name);
            return;
        }

        _connection = _connect(database, password);
        OnPropertyChanged(nameof(IsConnected));
        await Run(async _ =>
        {
            await LoadSchemasAsync("public");
            await LoadTablesAsync(null);
        });
    }

    private bool CanRefresh => !IsWorking && SelectedDatabase is not null;

    /// <summary>Reads the schemas, tables and the table shown again, keeping what was chosen.</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync()
    {
        if (_connection is null)
            return Work = ConnectAsync(SelectedDatabase);
        var table = SelectedTable;
        return Work = Run(async _ =>
        {
            await LoadSchemasAsync(SelectedSchema);
            await LoadTablesAsync(table);
        });
    }

    private bool CanCreateSchema => !IsWorking && _connection is not null && NewSchemaName.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanCreateSchema))]
    private Task CreateSchemaAsync()
    {
        var name = NewSchemaName.Trim();
        return Work = Run(async connection =>
        {
            await connection.CreateSchemaAsync(name);
            Log("Created the schema {Name}", name);
            NewSchemaName = "";
            Status = string.Format(Strings.DatabaseBrowser_CreatedTheSchema, name);
            await LoadSchemasAsync(name);
            await LoadTablesAsync(null);
        });
    }

    private bool CanCreateTable => !IsWorking && _connection is not null && SelectedSchema is not null && NewTableName.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanCreateTable))]
    private Task CreateTableAsync()
    {
        var (schema, name) = (SelectedSchema!, NewTableName.Trim());
        return Work = Run(async connection =>
        {
            await connection.CreateTableAsync(schema, name, NewTableWithId);
            Log("Created the table {Schema}.{Name}", schema, name);
            NewTableName = "";
            Status = string.Format(Strings.DatabaseBrowser_CreatedTheTable, schema, name);
            await LoadTablesAsync(name);
        });
    }

    private bool CanAddColumn => !IsWorking && _connection is not null && SelectedTable is not null
                                 && NewColumnName.Trim().Length > 0 && NewColumnType.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanAddColumn))]
    private Task AddColumnAsync()
    {
        var (schema, table) = (SelectedSchema!, SelectedTable!);
        var column = new ColumnSpec(NewColumnName.Trim(), NewColumnType.Trim(), NewColumnNotNull, NewColumnDefault.Trim());
        return Work = Run(async connection =>
        {
            await connection.AddColumnAsync(schema, table, column);
            Log("Added the column {Column} to {Schema}.{Table}", column.Name, schema, table);
            NewColumnName = "";
            NewColumnDefault = "";
            NewColumnNotNull = false;
            Status = string.Format(Strings.DatabaseBrowser_AddedTheColumn, column.Name);
            await LoadTableAsync();
        });
    }

    private bool CanInsert => !IsWorking && _connection is not null && SelectedTable is not null;

    /// <summary>Inserts a row of the values typed. Empty boxes give their columns' defaults.</summary>
    [RelayCommand(CanExecute = nameof(CanInsert))]
    private Task InsertAsync()
    {
        var (schema, table) = (SelectedSchema!, SelectedTable!);
        var values = InsertFields.Where(f => f.IsNull || f.Text.Length > 0)
            .Select(f => new KeyValuePair<string, string?>(f.Name, f.IsNull ? null : f.Text)).ToList();
        return Work = Run(async connection =>
        {
            await connection.InsertAsync(schema, table, values);
            Log("Inserted a row into {Schema}.{Table}", schema, table);
            foreach (var field in InsertFields)
                (field.Text, field.IsNull) = ("", false);
            Status = Strings.DatabaseBrowser_InsertedARow;
            await LoadRowsAsync();
        });
    }

    /// <summary>Puts a row's values in the form, to change and save.</summary>
    internal void EditRow(BrowserRow row)
    {
        if (IsWorking)
            return;
        Status = null;
        Fill(InsertFields, Columns.Select((c, i) => new InsertField(c.Column)
        {
            Original = row.Row.Values[i], Text = row.Row.Values[i] ?? "", IsNull = row.Row.Values[i] is null,
        }).ToList(), () => EditingRow = row);
    }

    /// <summary>Leaves the row as it is, and empties the form for a new row.</summary>
    [RelayCommand]
    private void CancelEdit()
    {
        EditingRow = null;
        Fill(InsertFields, Columns.Select(c => new InsertField(c.Column)).ToList(), () => { });
    }

    private bool CanSaveRow => CanInsert && EditingRow is not null;

    /// <summary>Saves the columns changed in the form to the row being edited.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveRow))]
    private Task SaveRowAsync()
    {
        var (schema, table, row) = (SelectedSchema!, SelectedTable!, EditingRow!);
        var changed = InsertFields.Where(f => f.Value != f.Original)
            .Select(f => new KeyValuePair<string, string?>(f.Name, f.Value)).ToList();
        if (changed.Count == 0)
        {
            CancelEdit();
            Status = Strings.DatabaseBrowser_NothingWasChanged;
            return Task.CompletedTask;
        }

        return Work = Run(async connection =>
        {
            var updated = await connection.UpdateAsync(schema, table, row.Row.Id, changed);
            Log("Changed a row of {Schema}.{Table}", schema, table);
            CancelEdit();
            Status = updated > 0 ? Strings.DatabaseBrowser_SavedTheRow : Strings.DatabaseBrowser_TheRowHadAlreadyChangedSoNotSaved;
            await LoadRowsAsync();
        });
    }

    private bool CanDropSchema => !IsWorking && _connection is not null && SelectedSchema is not null;

    [RelayCommand(CanExecute = nameof(CanDropSchema))]
    private void DropSchema()
    {
        var schema = SelectedSchema!;
        Dropping = new PendingDrop(string.Format(Strings.DatabaseBrowser_DropTheSchema, schema), c => c.DropSchemaAsync(schema),
            async () =>
            {
                await LoadSchemasAsync(null);
                await LoadTablesAsync(null);
            }, string.Format(Strings.DatabaseBrowser_DroppedTheSchema, schema));
    }

    private bool CanDropTable => !IsWorking && _connection is not null && SelectedTable is not null;

    [RelayCommand(CanExecute = nameof(CanDropTable))]
    private void DropTable()
    {
        var (schema, table) = (SelectedSchema!, SelectedTable!);
        Dropping = new PendingDrop(string.Format(Strings.DatabaseBrowser_DropTheTable, schema, table), c => c.DropTableAsync(schema, table),
            () => LoadTablesAsync(null), string.Format(Strings.DatabaseBrowser_DroppedTheTable, schema, table));
    }

    internal void AskDropColumn(BrowserColumn column)
    {
        if (IsWorking || SelectedTable is null)
            return;
        var (schema, table, name) = (SelectedSchema!, SelectedTable, column.Name);
        Dropping = new PendingDrop(string.Format(Strings.DatabaseBrowser_DropTheColumn, name, schema, table), c => c.DropColumnAsync(schema, table, name),
            LoadTableAsync, string.Format(Strings.DatabaseBrowser_DroppedTheColumn, name));
    }

    [RelayCommand]
    private void CancelDrop() => Dropping = null;

    /// <summary>Drops what was asked for, then reads what is left.</summary>
    [RelayCommand]
    private Task ConfirmDropAsync()
    {
        if (Dropping is not { } drop || IsWorking)
            return Task.CompletedTask;
        Dropping = null;
        return Work = Run(async connection =>
        {
            await drop.Drop(connection);
            Log("Dropped: {What}", drop.Done);
            Status = drop.Done;
            await drop.Reload();
        });
    }

    partial void OnPageSizeChanged(int value)
    {
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(PageNumberText));
        if (!_filling)
            Work = GoTo(0);
    }

    private bool CanGoBack => !IsWorking && Page > 0;

    private bool CanGoOn => !IsWorking && Page < PageCount - 1;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task FirstPageAsync() => GoTo(0);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task PreviousPageAsync() => GoTo(Page - 1);

    [RelayCommand(CanExecute = nameof(CanGoOn))]
    private Task NextPageAsync() => GoTo(Page + 1);

    [RelayCommand(CanExecute = nameof(CanGoOn))]
    private Task LastPageAsync() => GoTo(PageCount - 1);

    private Task GoTo(int page)
    {
        Page = page;
        if (SelectedTable is null)
            return Task.CompletedTask;

        // A row being edited may not be on the page shown next, so the form goes back to inserting.
        if (EditingRow is not null)
            CancelEdit();
        return Work = Run(_ => LoadRowsAsync());
    }

    internal Task DeleteRowAsync(BrowserRow row)
    {
        if (IsWorking || SelectedTable is null)
            return Task.CompletedTask;
        var (schema, table) = (SelectedSchema!, SelectedTable);
        return Work = Run(async connection =>
        {
            var deleted = await connection.DeleteAsync(schema, table, row.Row.Id);
            Log("Deleted a row from {Schema}.{Table}", schema, table);
            Status = deleted > 0 ? Strings.DatabaseBrowser_DeletedARow : Strings.DatabaseBrowser_TheRowHadAlreadyChanged;
            if (EditingRow == row)
                CancelEdit();
            await LoadRowsAsync();
        });
    }

    private async Task LoadSchemasAsync(string? choose)
    {
        var schemas = await _connection!.SchemasAsync();
        Fill(Schemas, schemas, () => SelectedSchema = schemas.Contains(choose) ? choose : schemas.FirstOrDefault());
    }

    private async Task LoadTablesAsync(string? choose)
    {
        if (SelectedSchema is not { } schema)
        {
            Fill(Tables, [], () => SelectedTable = null);
            ClearTable();
            return;
        }

        var tables = await _connection!.TablesAsync(schema);
        Fill(Tables, tables, () => SelectedTable = choose is not null && tables.Contains(choose) ? choose : tables.FirstOrDefault());
        await LoadTableAsync();
    }

    private async Task LoadTableAsync()
    {
        if (SelectedSchema is not { } schema || SelectedTable is not { } table)
        {
            ClearTable();
            return;
        }

        var columns = await _connection!.ColumnsAsync(schema, table);
        EditingRow = null;
        Fill(Columns, columns.Select(c => new BrowserColumn(c, this)).ToList(), () => { });
        Fill(Heading, columns.Select(c => c.Name).ToList(), () => { });
        if ((schema, table) != _widthsOf)
        {
            Page = 0;
            _draggedWidths.Clear();
            _widthsOf = (schema, table);
        }
        Fill(InsertFields, columns.Select(c => new InsertField(c)).ToList(), () => { });
        await LoadRowsAsync();
    }

    private async Task LoadRowsAsync()
    {
        if (SelectedSchema is not { } schema || SelectedTable is not { } table)
            return;
        var total = await _connection!.CountAsync(schema, table);
        TotalRows = total;
        if (Page > PageCount - 1)
            Page = PageCount - 1;
        var names = Columns.Select(c => c.Name).ToList();
        var key = Columns.Where(c => c.Column.IsPrimaryKey).Select(c => c.Name).ToList();
        var rows = (await _connection.RowsAsync(schema, table, names, key, PageSize, Page * PageSize)).Select(r => new BrowserRow(r, this)).ToList();

        var sizes = names.Select((name, i) => new ColumnSize(name, _draggedWidths.TryGetValue(name, out var dragged) ? dragged : FittingWidth(name, i, rows))).ToList();
        foreach (var row in rows)
            row.Shown = row.Cells.Select((text, i) => new BrowserCell(text, sizes[i])).ToList();
        Fill(HeadingCells, names.Select((name, i) => new BrowserCell(name, sizes[i])).ToList(), () => { });
        Fill(Rows, rows, () => { });

        var first = (long)Page * PageSize + 1;
        RowsText = total == 0 ? Strings.DatabaseBrowser_NoRows
            : total == 1 ? Strings.DatabaseBrowser_OneRow
            : string.Format(Strings.DatabaseBrowser_RowsOf, first, first + rows.Count - 1, total);
    }

    // As wide as the column's longest value on the page, or its heading, within bounds.
    private static double FittingWidth(string name, int column, IEnumerable<BrowserRow> rows)
        => Math.Clamp(
            Math.Max(name.Length * HeadingCharacterWidth, rows.Select(r => Math.Min(r.Cells[column].Length, 100) * CharacterWidth).DefaultIfEmpty(0).Max()) + CellPadding,
            MinColumnWidth, MaxColumnWidth);

    /// <summary>Sets a column's width, as its heading's edge is dragged. It stays while the table is shown.</summary>
    public void SetColumnWidth(ColumnSize size, double width)
    {
        size.Width = Math.Clamp(width, MinDraggedWidth, MaxDraggedWidth);
        _draggedWidths[size.Name] = size.Width;
    }

    /// <summary>Fits a column to its values on the page again, forgetting a width it was dragged to.</summary>
    public void FitColumn(ColumnSize size)
    {
        _draggedWidths.Remove(size.Name);
        var column = Heading.IndexOf(size.Name);
        if (column >= 0)
            size.Width = FittingWidth(size.Name, column, Rows);
    }

    private void ClearTable()
    {
        EditingRow = null;
        Columns.Clear();
        Heading.Clear();
        HeadingCells.Clear();
        Rows.Clear();
        TotalRows = 0;
        Page = 0;
        InsertFields.Clear();
        RowsText = "";
    }

    private void Fill<T>(ObservableCollection<T> list, IReadOnlyList<T> items, Action choose)
    {
        _filling = true;
        try
        {
            list.Clear();
            foreach (var item in items)
                list.Add(item);
            choose();
        }
        finally
        {
            _filling = false;
        }
    }

    // One thing at a time; what PostgreSQL or the network says is shown rather than thrown.
    private async Task Run(Func<IDatabaseConnection, Task> work)
    {
        if (_connection is not { } connection)
            return;
        IsWorking = true;
        Error = null;
        try
        {
            await work(connection);
        }
        catch (Exception e) when (e is DbException or SocketException or TimeoutException or InvalidOperationException)
        {
            Error = e is PostgresException postgres ? postgres.MessageText : e.Message;
            Status = null;
            AppLog.For("Data").LogWarning("The database {Database} said: {Problem}", SelectedDatabase?.Name, Error);
        }
        finally
        {
            IsWorking = false;
            OnPropertyChanged(nameof(IsConnected));
            RefreshCommand.NotifyCanExecuteChanged();
            CreateSchemaCommand.NotifyCanExecuteChanged();
            CreateTableCommand.NotifyCanExecuteChanged();
            AddColumnCommand.NotifyCanExecuteChanged();
            InsertCommand.NotifyCanExecuteChanged();
            SaveRowCommand.NotifyCanExecuteChanged();
            DropSchemaCommand.NotifyCanExecuteChanged();
            FirstPageCommand.NotifyCanExecuteChanged();
            PreviousPageCommand.NotifyCanExecuteChanged();
            NextPageCommand.NotifyCanExecuteChanged();
            LastPageCommand.NotifyCanExecuteChanged();
            DropTableCommand.NotifyCanExecuteChanged();
        }
    }

    private static void Log(string message, params object[] args)
#pragma warning disable CA2254 // The messages are the constant ones above.
        => AppLog.For("Data").LogInformation(message, args);
#pragma warning restore CA2254
}
