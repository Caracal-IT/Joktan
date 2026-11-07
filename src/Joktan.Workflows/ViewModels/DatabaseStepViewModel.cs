using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Data.Postgres;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Resources;

namespace Joktan.Workflows.ViewModels;

/// <summary>
/// The window a database step is set up in: its database, schema and table, read from the database itself, then
/// what it does with each column, and the filters the rows must pass. Every change is written to the step at once,
/// so the designer's panel and the canvas follow it.
/// </summary>
public sealed partial class DatabaseStepViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly StepViewModel _owner;
    private IDatabaseConnection? _connection;

    // Set while the lists are filled in, so that choosing in them does not read again or change the step.
    private bool _filling;

    public DatabaseStepViewModel(StepViewModel owner)
    {
        _owner = owner;
        foreach (var choice in owner.Choices.Databases())
            DatabaseChoices.Add(choice);
        if (Step.Schema.Trim().Length > 0)
            Schemas.Add(Step.Schema);
        if (Step.Table.Trim().Length > 0)
            Tables.Add(Step.Table);

        // Until the table is read, the columns the step names are all that is known of it.
        foreach (var column in Step.Columns)
            Columns.Add(new DatabaseColumnViewModel(this, column.Column, null, column));
        foreach (var filter in Step.Filters)
            Filters.Add(new DatabaseFilterViewModel(this, filter));
        Loading = ConnectAsync();
    }

    public WorkflowStep Step => _owner.Step;

    public string Name => _owner.Name;

    /// <summary>Reading the schemas, tables and columns; tests wait on it.</summary>
    public Task Loading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    // ---- Where ----

    public ObservableCollection<DatabaseChoice> DatabaseChoices { get; } = [];

    public bool HasNoDatabases => DatabaseChoices.Count == 0;

    public DatabaseChoice? SelectedDatabase
    {
        get => DatabaseChoices.FirstOrDefault(d => d.Id == Step.Database);
        set
        {
            if (_filling || value is null || value.Id == Step.Database)
                return;
            Step.Database = value.Id;
            OnPropertyChanged();
            Changed();
            Loading = ConnectAsync();
        }
    }

    public ObservableCollection<string> Schemas { get; } = [];

    public string? Schema
    {
        get => Step.Schema;
        set
        {
            if (_filling || value is null || value == Step.Schema)
                return;
            Step.Schema = value;
            ForgetTable();
            Changed();
            Loading = LoadTablesAsync();
        }
    }

    public ObservableCollection<string> Tables { get; } = [];

    public string? Table
    {
        get => Step.Table.Length > 0 ? Step.Table : null;
        set
        {
            if (_filling || value is null || value == Step.Table)
                return;
            ForgetTable();
            Step.Table = value;
            OnPropertyChanged();
            Changed();
            Loading = LoadColumnsAsync();
        }
    }

    // What was said about one table means nothing for another.
    private void ForgetTable()
    {
        Step.Table = "";
        Step.Columns.Clear();
        Step.Filters.Clear();
        Step.OrderBy = "";
        Columns.Clear();
        Filters.Clear();
        OnPropertyChanged(nameof(Table));
        ColumnsChanged();
    }

    [RelayCommand]
    private Task Refresh() => Loading = ConnectAsync();

    // ---- What ----

    /// <summary>What the step does, which its type says.</summary>
    public DatabaseAction Action => StepTypes.DatabaseActionOf(Step.Type) ?? DatabaseAction.Read;

    public string TypeText => Step.Type.Text();

    public bool IsRead => Action == DatabaseAction.Read;

    public bool IsInsert => Action == DatabaseAction.Insert;

    public bool IsUpdate => Action == DatabaseAction.Update;

    public bool IsDelete => Action == DatabaseAction.Delete;

    public bool IsUpsert => Action == DatabaseAction.Upsert;

    /// <summary>Whether the rows are chosen by filters: for a read, an update and a delete.</summary>
    public bool HasFilters => IsRead || IsUpdate || IsDelete;

    /// <summary>Whether the filters must say which rows, with values: an update or delete never changes every row.</summary>
    public bool NeedsFilters => IsUpdate || IsDelete;

    /// <summary>Whether columns are chosen: for all but a delete.</summary>
    public bool UsesColumns => !IsDelete;

    /// <summary>Whether columns are given values: for an insert, an update and an upsert.</summary>
    public bool GivesValues => IsInsert || IsUpdate || IsUpsert;

    /// <summary>Whether it can make a row for each item of a list: an insert or an upsert.</summary>
    public bool CanMakeManyRows => IsInsert || IsUpsert;

    public string ColumnsTip => Action switch
    {
        DatabaseAction.Insert => Strings.DatabaseStep_InsertColumnsTip,
        DatabaseAction.Update => Strings.DatabaseStep_UpdateColumnsTip,
        DatabaseAction.Upsert => Strings.DatabaseStep_UpsertColumnsTip,
        _ => Strings.DatabaseStep_ReadColumnsTip,
    };

    public string FiltersTip => NeedsFilters ? string.Format(Strings.DatabaseStep_FiltersRequiredTip, IsDelete ? Strings.DatabaseStep_Deleted : Strings.DatabaseStep_Changed)
        : Strings.DatabaseStep_FiltersTip;

    public string ResultTip => Action switch
    {
        DatabaseAction.Read => Strings.DatabaseStep_ReadResultTip,
        DatabaseAction.Delete => Strings.DatabaseStep_DeleteResultTip,
        _ => Strings.DatabaseStep_ChangeResultTip,
    };

    /// <summary>What is wrong with the step as it is, shown in the window so it can be put right there.</summary>
    public string? Problem => WorkflowCheck.StepProblem(Step);

    // ---- Columns ----

    public ObservableCollection<DatabaseColumnViewModel> Columns { get; } = [];

    public bool HasColumns => Columns.Count > 0;

    /// <summary>The columns' names, for the filters and the order.</summary>
    public IReadOnlyList<string> ColumnNames => Columns.Where(c => !c.IsMissing).Select(c => c.Name).ToList();

    /// <summary>The order a read can be in: the table's own, then by each column.</summary>
    public IReadOnlyList<string> OrderChoices => [Strings.DatabaseStep_TableOrder, .. ColumnNames];

    public string OrderChoice
    {
        get => Step.OrderBy.Length == 0 ? Strings.DatabaseStep_TableOrder : Step.OrderBy;
        set
        {
            if (_filling || value is null)
                return;
            Step.OrderBy = value == Strings.DatabaseStep_TableOrder ? "" : value;
            OnPropertyChanged();
            Changed();
        }
    }

    [RelayCommand]
    private void UseAll()
    {
        foreach (var column in Columns.Where(c => !c.IsMissing && !(c.Column?.IsIdentity == true && !IsRead)))
            column.IsUsed = true;
    }

    [RelayCommand]
    private void UseNone()
    {
        foreach (var column in Columns.ToList())
            column.IsUsed = false;
    }

    /// <summary>Writes the columns in use to the step, in the table's order.</summary>
    internal void ColumnUseChanged()
    {
        Step.Columns = Columns.Where(c => c.IsKept).Select(c => c.Entry).ToList();
        Changed();
    }

    internal void RemoveMissing(DatabaseColumnViewModel column)
    {
        Columns.Remove(column);
        ColumnUseChanged();
        ColumnsChanged();
    }

    private void ColumnsChanged()
    {
        OnPropertyChanged(nameof(HasColumns));
        OnPropertyChanged(nameof(ColumnNames));
        OnPropertyChanged(nameof(OrderChoices));
        OnPropertyChanged(nameof(OrderChoice));
        foreach (var filter in Filters)
            filter.ColumnsChanged();
    }

    /// <summary>The heading over the variables: from the rows read, or from the rows as changed.</summary>
    public string VariableHeading => IsRead ? Strings.DatabaseStep_PutInVariable : Strings.DatabaseStep_PutChangedInVariable;

    /// <summary>The workflow's global variables and the other steps' output variables, which a column's value or the result can be put in.</summary>
    public IReadOnlyList<string> VariableChoices => _owner.ResultVariableChoices;

    /// <summary>Paths a value can be, to choose rather than type.</summary>
    public IReadOnlyList<string> ValueChoices
    {
        get
        {
            var paths = VariableChoices.Select(v => $"{{{{vars.{v}}}}}").ToList();
            if (CanMakeManyRows && Step.ManyRows)
                paths.Insert(0, $"{{{{vars.{ItemVariableOrDefault}.}}}}");
            paths.Add("{{input}}");
            paths.Add("{{last}}");
            return paths;
        }
    }

    // ---- Filters ----

    public ObservableCollection<DatabaseFilterViewModel> Filters { get; } = [];

    /// <summary>The comparisons a filter can make; C# cannot be sent to a database.</summary>
    public IReadOnlyList<CompareOperator> Operators { get; } = Enum.GetValues<CompareOperator>().Where(o => o != CompareOperator.CSharp).ToList();

    [RelayCommand]
    private void AddFilter()
    {
        var filter = new DatabaseFilter { Column = ColumnNames.FirstOrDefault() ?? "" };
        Step.Filters.Add(filter);
        Filters.Add(new DatabaseFilterViewModel(this, filter));
        Changed();
    }

    internal void RemoveFilter(DatabaseFilterViewModel filter)
    {
        Step.Filters.Remove(filter.Filter);
        Filters.Remove(filter);
        Changed();
    }

    // ---- A read's order, limit and result ----

    public bool Descending
    {
        get => Step.Descending;
        set => Set(value, v => Step.Descending = v);
    }

    public decimal Limit
    {
        get => Step.Limit;
        set => Set((int)Math.Clamp(value, 0, int.MaxValue), v => Step.Limit = v);
    }

    public bool FirstRowOnly
    {
        get => Step.FirstRowOnly;
        set => Set(value, v => Step.FirstRowOnly = v);
    }

    public string? ResultVariable
    {
        get => _owner.ResultVariable;
        set
        {
            _owner.ResultVariable = value;
            OnPropertyChanged();
        }
    }

    // ---- An insert's rows ----

    public bool ManyRows
    {
        get => Step.ManyRows;
        set => Set(value, v => Step.ManyRows = v, [nameof(ValueChoices)]);
    }

    public string Items
    {
        get => Step.Items;
        set => Set(value, v => Step.Items = v);
    }

    public string ItemVariable
    {
        get => Step.ItemVariable;
        set => Set(value.Trim(), v => Step.ItemVariable = v, [nameof(ValueChoices), nameof(ManyRowsTip)]);
    }

    private string ItemVariableOrDefault => Step.ItemVariable.Trim() is { Length: > 0 } name ? name : "item";

    public string ManyRowsTip => string.Format(Strings.DatabaseStep_ManyRowsTip, ItemVariableOrDefault);

    // ---- A new connection ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddingConnection))]
    public partial ConnectionEditorViewModel? NewConnection { get; set; }

    public bool IsAddingConnection => NewConnection is not null;

    public bool CanAddConnection => _owner.Choices.Connections is not null;

    [RelayCommand]
    private void AddConnection()
    {
        if (_owner.Choices.Connections is not { } connections)
            return;
        NewConnection = new ConnectionEditorViewModel(connections, null, _owner.Choices.OpenConnection, saved =>
        {
            NewConnection = null;
            var choice = new DatabaseChoice(saved.Id, saved.Name, saved.ConnectionText);
            if (DatabaseChoices.FirstOrDefault(d => d.Id == saved.Id) is { } old)
                DatabaseChoices.Remove(old);
            DatabaseChoices.Add(choice);
            OnPropertyChanged(nameof(HasNoDatabases));
            SelectedDatabase = choice;
            OnPropertyChanged(nameof(SelectedDatabase));
        }, () => NewConnection = null);
    }

    // ---- Reading the database ----

    private async Task ConnectAsync()
    {
        if (_connection is { } old)
        {
            _connection = null;
            await old.DisposeAsync();
        }

        Error = null;
        if (Step.Database.Length == 0)
            return;
        if (_owner.Choices.OpenDatabase is not { } open)
            return;
        var (connection, error) = open(Step.Database);
        if (connection is null)
        {
            Error = error;
            return;
        }

        _connection = connection;
        if (await ReadAsync(c => c.SchemasAsync()) is not { } schemas)
            return;
        Fill(Schemas, schemas, Step.Schema);
        await LoadTablesAsync();
    }

    private async Task LoadTablesAsync()
    {
        if (_connection is null || Step.Schema.Length == 0)
            return;
        if (await ReadAsync(c => c.TablesAsync(Step.Schema)) is not { } tables)
            return;
        Fill(Tables, tables, Step.Table);
        OnPropertyChanged(nameof(Table));
        await LoadColumnsAsync();
    }

    private async Task LoadColumnsAsync()
    {
        if (_connection is null || Step.Table.Length == 0)
            return;
        if (await ReadAsync(c => c.ColumnsAsync(Step.Schema, Step.Table)) is not { } columns)
            return;

        // The table's columns, with what the step says of each, then those the step names that the table lacks.
        var saved = Step.Columns.ToList();
        Columns.Clear();
        foreach (var column in columns)
            Columns.Add(new DatabaseColumnViewModel(this, column.Name, column, saved.FirstOrDefault(s => s.Column == column.Name)));
        foreach (var missing in saved.Where(s => columns.All(c => c.Name != s.Column)))
            Columns.Add(new DatabaseColumnViewModel(this, missing.Column, null, missing) { IsMissing = true });

        // An upsert starts out matching rows on the primary key.
        if (IsUpsert && saved.Count == 0)
        {
            foreach (var key in Columns.Where(c => c.IsKey))
                key.Match = true;
        }

        ColumnsChanged();
    }

    // Reads from the database, saying why it could not instead.
    private async Task<T?> ReadAsync<T>(Func<IDatabaseConnection, Task<T>> read) where T : class
    {
        if (_connection is not { } connection)
            return null;
        IsBusy = true;
        try
        {
            return await read(connection);
        }
        catch (Exception e) when (e is Npgsql.NpgsqlException or System.Net.Sockets.SocketException or TimeoutException or InvalidOperationException
                                      or OperationCanceledException)
        {
            Error = string.Format(Strings.DatabaseStep_CouldNotRead, e is Npgsql.PostgresException postgres ? postgres.MessageText : e.Message);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Fills a list, keeping the step's choice in it even when the database no longer has it, so it can be seen. The list
    // is changed in place, never cleared, so that a box showing the choice does not lose it on the way.
    private void Fill(ObservableCollection<string> list, IReadOnlyList<string> items, string chosen)
    {
        var wanted = items.ToList();
        if (chosen.Length > 0 && !wanted.Contains(chosen))
            wanted.Add(chosen);
        _filling = true;
        try
        {
            foreach (var gone in list.Where(i => !wanted.Contains(i)).ToList())
                list.Remove(gone);
            for (var i = 0; i < wanted.Count; i++)
            {
                var at = list.IndexOf(wanted[i]);
                if (at < 0)
                    list.Insert(i, wanted[i]);
                else if (at != i)
                    list.Move(at, i);
            }
        }
        finally
        {
            _filling = false;
        }

        OnPropertyChanged(nameof(Schema));
        OnPropertyChanged(nameof(Table));
    }

    private void Set<T>(T value, Action<T> apply, string[]? also = null, [CallerMemberName] string name = "")
    {
        apply(value);
        OnPropertyChanged(name);
        foreach (var other in also ?? [])
            OnPropertyChanged(other);
        Changed();
    }

    internal void Changed()
    {
        OnPropertyChanged(nameof(Problem));
        _owner.DatabaseEdited();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is { } connection)
        {
            _connection = null;
            await connection.DisposeAsync();
        }
    }
}

/// <summary>
/// A column of the table: whether the step reads it, or gives it a value, and the variable its value goes in from
/// the rows read or changed. The step keeps it while either is so.
/// </summary>
public sealed partial class DatabaseColumnViewModel : ViewModelBase
{
    private readonly DatabaseStepViewModel _owner;
    private bool _isKept;

    /// <param name="column">The column as the database has it; null until it is read, or when it is gone.</param>
    /// <param name="saved">What the step says of it, or null when it does not use it.</param>
    public DatabaseColumnViewModel(DatabaseStepViewModel owner, string name, TableColumn? column, DatabaseColumn? saved)
    {
        _owner = owner;
        Name = name;
        Column = column;
        _isKept = saved is not null;
        Entry = saved ?? new DatabaseColumn { Column = name, SetsValue = false };
    }

    public string Name { get; }

    public TableColumn? Column { get; }

    /// <summary>What the step keeps of the column, kept while it is unused so that it comes back as it was.</summary>
    public DatabaseColumn Entry { get; }

    /// <summary>Whether the step keeps the column: it reads it, gives it a value or puts it in a variable.</summary>
    public bool IsKept => _isKept;

    /// <summary>Its type, and whether it has a default or is numbered for each row.</summary>
    public string TypeText => Column is null ? ""
        : Column.IsIdentity ? string.Format(Strings.DatabaseStep_TypeNumbered, Column.Type)
        : Column.Default is not null ? string.Format(Strings.DatabaseStep_TypeWithDefault, Column.Type)
        : Column.Type;

    public bool IsKey => Column?.IsPrimaryKey == true;

    /// <summary>Whether the step names it but the table does not have it.</summary>
    public bool IsMissing { get; init; }

    /// <summary>The tick: for a read, whether it is read; otherwise whether it is given a value.</summary>
    public bool IsUsed
    {
        get => _owner.IsRead ? _isKept : _isKept && Entry.SetsValue;
        set
        {
            if (value == IsUsed)
                return;
            if (_owner.IsRead)
            {
                _isKept = value;
                Entry.SetsValue = true;
            }
            else
            {
                Entry.SetsValue = value;
                if (!value)
                    Entry.Match = false;
                _isKept = value || Entry.Variable.Length > 0;
            }

            Refresh();
        }
    }

    public bool ShowsValue => _owner.GivesValues;

    public bool ShowsMatch => _owner.IsUpsert;

    /// <summary>The value to insert or set, which may use paths; typing one ticks the column.</summary>
    public string Value
    {
        get => Entry.Value;
        set
        {
            Entry.Value = value;
            OnPropertyChanged();
            if (!IsUsed)
                IsUsed = true;
            else
                _owner.Changed();
        }
    }

    /// <summary>What an empty value means: NULL, or for an insert the column's default if it has one.</summary>
    public string ValueHint => Entry.Match ? Strings.DatabaseStep_KeyNeedsValue
        : (_owner.IsInsert || _owner.IsUpsert) && Column is { } c && (c.Default is not null || c.IsIdentity) ? Strings.DatabaseStep_DefaultWhenEmpty
        : Strings.DatabaseStep_NullWhenEmpty;

    /// <summary>For an upsert, whether the row with the same value in this column is the one updated; choosing it ticks the column.</summary>
    public bool Match
    {
        get => Entry.Match;
        set
        {
            Entry.Match = value;
            if (value && !IsUsed)
                IsUsed = true;
            else
                Refresh();
        }
    }

    /// <summary>The variable the column's value goes in, from the rows read or changed; choosing one keeps the column.</summary>
    public string? Variable
    {
        get => Entry.Variable;
        set
        {
            Entry.Variable = value?.Trim() ?? "";
            OnPropertyChanged();
            if (_owner.IsRead)
                _isKept |= Entry.Variable.Length > 0;
            else
                _isKept = Entry.SetsValue || Entry.Variable.Length > 0;
            Refresh();
        }
    }

    /// <summary>Whether the variable gets a list of every row's value, rather than the first row's.</summary>
    public bool AllRows
    {
        get => Entry.AllRows;
        set
        {
            Entry.AllRows = value;
            OnPropertyChanged();
            _owner.Changed();
        }
    }

    [RelayCommand]
    private void Remove() => _owner.RemoveMissing(this);

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsUsed));
        OnPropertyChanged(nameof(Match));
        OnPropertyChanged(nameof(ValueHint));
        _owner.ColumnUseChanged();
    }
}

/// <summary>A test the rows must pass: a column compared with a value.</summary>
public sealed partial class DatabaseFilterViewModel(DatabaseStepViewModel owner, DatabaseFilter filter) : ViewModelBase
{
    public DatabaseFilter Filter { get; } = filter;

    public IReadOnlyList<string> ColumnChoices => owner.ColumnNames.Contains(Filter.Column) || Filter.Column.Length == 0
        ? owner.ColumnNames
        : [.. owner.ColumnNames, Filter.Column];

    public IReadOnlyList<CompareOperator> Operators => owner.Operators;

    public IReadOnlyList<string> ValueChoices => owner.ValueChoices;

    public string? Column
    {
        get => Filter.Column;
        set
        {
            if (value is null)
                return;
            Filter.Column = value;
            owner.Changed();
        }
    }

    public CompareOperator Operator
    {
        get => Filter.Operator;
        set
        {
            Filter.Operator = value;
            OnPropertyChanged(nameof(TakesValue));
            owner.Changed();
        }
    }

    public bool TakesValue => Conditions.TakesRight(Filter.Operator);

    public string Value
    {
        get => Filter.Value;
        set
        {
            Filter.Value = value;
            owner.Changed();
        }
    }

    [RelayCommand]
    private void Remove() => owner.RemoveFilter(this);

    internal void ColumnsChanged()
    {
        OnPropertyChanged(nameof(ColumnChoices));
        OnPropertyChanged(nameof(Column));
    }
}
