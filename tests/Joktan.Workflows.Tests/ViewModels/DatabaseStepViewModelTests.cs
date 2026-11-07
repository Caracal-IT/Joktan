using System.Linq;
using System.Threading.Tasks;
using Joktan.Data.Postgres;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Resources;
using Joktan.Workflows.ViewModels;
using Xunit;

namespace Joktan.Workflows.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class DatabaseStepViewModelTests : DataFolderTest
{
    private readonly FakeWorkflowServices _services = new();
    private readonly FakeCredentialStore _credentials = new();
    private int _changes;

    public DatabaseStepViewModelTests()
    {
        _services.AddDatabase("db1", "Shop")
            .Table("orders", new TableColumn("id", "bigint", false, null, IsPrimaryKey: true, IsIdentity: true),
                new TableColumn("name", "text", true, null), new TableColumn("qty", "integer", false, "1"))
            .Table("sales", "totals", new TableColumn("day", "date", false, null));
        _services.AddDatabase("db2", "Down").Throws = new Npgsql.NpgsqlException("Failed to connect to 127.0.0.1:1");
        _services.Connections = new DatabaseConnections(new WorkflowSettings(), _credentials);
    }

    private StepViewModel Panel(WorkflowStep step) => new(step, new StepChoices
    {
        Databases = ((IWorkflowServices)_services).Databases,
        OpenDatabase = _services.OpenDatabase,
        Connections = _services.Connections,
        OpenConnection = (_, _, _, _, _) => _services.Databases["db1"].Database,
        Globals = () => ["customer", "total"],
    }, () => _changes++);

    private static WorkflowStep NewStep(StepType type = StepType.DatabaseRead) => new() { Type = type, Name = "Orders" };

    private async Task<(WorkflowStep Step, StepViewModel Panel, DatabaseStepViewModel Window)> OpenOrders(StepType type)
    {
        var step = NewStep(type);
        step.Database = "db1";
        step.Table = "orders";
        var panel = Panel(step);
        var vm = new DatabaseStepViewModel(panel);
        await vm.Loading;
        return (step, panel, vm);
    }

    [Fact]
    public async Task Choosing_a_database_schema_and_table_reads_its_columns()
    {
        var step = NewStep();
        var panel = Panel(step);
        var vm = new DatabaseStepViewModel(panel);
        await vm.Loading;
        Assert.Equal(["Shop", "Down"], vm.DatabaseChoices.Select(d => d.Name));
        Assert.Null(vm.SelectedDatabase);
        Assert.False(vm.HasColumns);
        Assert.Equal(Strings.DatabaseStep_NoneChosen, panel.DatabaseText);

        vm.SelectedDatabase = vm.DatabaseChoices[0];
        await vm.Loading;
        Assert.Equal("db1", step.Database);
        Assert.Equal(["public", "sales"], vm.Schemas);
        Assert.Equal(["orders"], vm.Tables);
        Assert.Equal("Shop", panel.DatabaseText);

        vm.Table = "orders";
        await vm.Loading;
        Assert.Equal(["id", "name", "qty"], vm.Columns.Select(c => c.Name));
        Assert.Equal("bigint · numbered", vm.Columns[0].TypeText);
        Assert.True(vm.Columns[0].IsKey);
        Assert.Equal("integer · default", vm.Columns[2].TypeText);
        Assert.Equal([Strings.DatabaseStep_TableOrder, "id", "name", "qty"], vm.OrderChoices);
        Assert.Equal("public.orders", panel.TableText);
        Assert.Null(WorkflowCheck.StepProblem(step));

        vm.Schema = "sales";
        await vm.Loading;
        Assert.Equal("", step.Table);
        Assert.Equal(["totals"], vm.Tables);
        Assert.Empty(vm.Columns);
        Assert.True(_changes > 0);
    }

    [Fact]
    public async Task Ticked_columns_are_read_and_any_can_go_in_a_variable_as_a_value_or_a_list()
    {
        var (step, panel, vm) = await OpenOrders(StepType.DatabaseRead);
        Assert.Equal(Strings.DatabaseStep_AllColumns, panel.DatabaseColumnsText);
        Assert.True(vm.IsRead && vm.HasFilters && !vm.NeedsFilters && !vm.GivesValues && !vm.CanMakeManyRows);
        Assert.Equal(Strings.DatabaseStep_PutInVariable, vm.VariableHeading);

        // Choosing a variable ticks the column.
        vm.Columns[1].Variable = "customer";
        Assert.True(vm.Columns[1].IsUsed);
        vm.Columns[2].IsUsed = true;
        vm.Columns[2].AllRows = true;
        vm.Columns[2].Variable = "  quantities ";
        Assert.Equal(["name", "qty"], step.Columns.Select(c => c.Column));
        Assert.Equal("quantities", step.Columns[1].Variable);
        Assert.True(step.Columns[1].AllRows);
        Assert.Equal("name, qty", panel.DatabaseColumnsText);
        Assert.False(vm.Columns[1].ShowsValue);

        vm.UseNoneCommand.Execute(null);
        Assert.Empty(step.Columns);
        vm.UseAllCommand.Execute(null);
        Assert.Equal(3, step.Columns.Count);
        Assert.Equal("quantities", step.Columns[2].Variable);
    }

    [Fact]
    public async Task An_insert_gives_ticked_columns_values_and_can_put_the_new_id_in_a_variable()
    {
        var (step, panel, vm) = await OpenOrders(StepType.DatabaseInsert);
        Assert.True(vm.IsInsert && vm.GivesValues && vm.CanMakeManyRows && !vm.HasFilters);
        Assert.Equal(Strings.DatabaseStep_PutChangedInVariable, vm.VariableHeading);
        Assert.Equal(Strings.DatabaseStep_DefaultWhenEmpty, vm.Columns[2].ValueHint);
        Assert.Equal(Strings.DatabaseStep_NullWhenEmpty, vm.Columns[1].ValueHint);

        // A variable alone keeps the column without giving it a value.
        vm.Columns[0].Variable = "orderId";
        Assert.False(vm.Columns[0].IsUsed);
        Assert.False(Assert.Single(step.Columns).SetsValue);
        vm.Columns[1].Value = "{{vars.customer}}";
        Assert.True(vm.Columns[1].IsUsed);
        Assert.Equal(["id", "name"], step.Columns.Select(c => c.Column));
        Assert.Equal([false, true], step.Columns.Select(c => c.SetsValue));
        Assert.Equal("id, name", panel.DatabaseColumnsText);

        // Unticked, what was said of a column comes back when it is ticked again; with no variable it is dropped.
        vm.Columns[1].IsUsed = false;
        Assert.Equal(["id"], step.Columns.Select(c => c.Column));
        vm.Columns[1].IsUsed = true;
        Assert.Equal("{{vars.customer}}", step.Columns[1].Value);
        vm.Columns[0].Variable = "";
        Assert.Equal(["name"], step.Columns.Select(c => c.Column));

        // All leaves out the column the database numbers.
        vm.UseAllCommand.Execute(null);
        Assert.Equal(["name", "qty"], step.Columns.Select(c => c.Column));

        vm.ManyRows = true;
        vm.ItemVariable = " line ";
        Assert.Equal("line", step.ItemVariable);
        Assert.Equal("{{vars.line.}}", vm.ValueChoices[0]);
        Assert.Contains("{{vars.customer}}", vm.ValueChoices);
        Assert.Contains("{{vars.line}}", vm.ManyRowsTip);
        Assert.Equal(Strings.Check_InsertNeedsRows, vm.Problem);
        vm.Items = "{{input.lines}}";
        Assert.Null(vm.Problem);
    }

    [Fact]
    public async Task An_upsert_matches_on_the_primary_key_to_start_with()
    {
        var (step, _, vm) = await OpenOrders(StepType.DatabaseUpsert);
        Assert.True(vm.IsUpsert && vm.Columns[0].ShowsMatch);
        Assert.True(vm.Columns[0].Match);
        Assert.True(vm.Columns[0].IsUsed);
        Assert.Equal(Strings.DatabaseStep_KeyNeedsValue, vm.Columns[0].ValueHint);
        Assert.Equal(Strings.Check_KeyNeedsValue, vm.Problem);
        vm.Columns[0].Value = "{{input.id}}";
        vm.Columns[1].Value = "{{input.name}}";
        Assert.Null(vm.Problem);

        // Unticking a key column makes it no key; a key chosen ticks its column.
        vm.Columns[0].IsUsed = false;
        Assert.DoesNotContain(step.Columns, c => c.Match);
        Assert.Equal(Strings.Check_UpsertNeedsKey, vm.Problem);
        vm.Columns[1].Match = true;
        Assert.Null(vm.Problem);
        Assert.Equal(["name"], step.Columns.Where(c => c.Match).Select(c => c.Column));
    }

    [Theory]
    [InlineData(StepType.DatabaseUpdate)]
    [InlineData(StepType.DatabaseDelete)]
    public async Task An_update_or_delete_says_its_filters_are_needed_with_values(StepType type)
    {
        var (step, panel, vm) = await OpenOrders(type);
        Assert.True(vm.NeedsFilters);
        Assert.Equal(type != StepType.DatabaseDelete, vm.UsesColumns);
        Assert.Equal(type != StepType.DatabaseDelete, panel.UsesDatabaseColumns);
        Assert.Contains(type == StepType.DatabaseDelete ? Strings.DatabaseStep_Deleted : Strings.DatabaseStep_Changed, vm.FiltersTip);
        Assert.Equal(Strings.DatabaseStep_NoFilters, panel.DatabaseFiltersText);
        if (type == StepType.DatabaseUpdate)
        {
            Assert.Equal(Strings.Check_UpdateNeedsColumns, vm.Problem);
            vm.Columns[1].Value = "paid";
        }

        Assert.Equal(Strings.Check_NeedsFilter, vm.Problem);
        vm.AddFilterCommand.Execute(null);
        var filter = Assert.Single(vm.Filters);
        Assert.Equal("id", filter.Column);
        Assert.Equal(Strings.Check_FilterNeedsValue, vm.Problem);
        filter.Operator = CompareOperator.IsNotEmpty;
        Assert.False(filter.TakesValue);
        Assert.Equal(Strings.Check_FilterNeedsComparison, vm.Problem);
        filter.Operator = CompareOperator.Equals;
        filter.Value = "{{input.id}}";
        Assert.Null(vm.Problem);
        Assert.Equal(Strings.DatabaseStep_OneFilter, panel.DatabaseFiltersText);
        filter.RemoveCommand.Execute(null);
        Assert.Empty(step.Filters);
        Assert.Equal(Strings.Check_NeedsFilter, vm.Problem);
    }

    [Fact]
    public async Task A_reads_order_limit_and_result_are_kept_on_the_step()
    {
        var (step, panel, vm) = await OpenOrders(StepType.DatabaseRead);
        Assert.DoesNotContain(CompareOperator.CSharp, vm.Operators);
        Assert.Equal(Strings.DatabaseStep_AllRowsRead, panel.DatabaseFiltersText);
        vm.OrderChoice = "qty";
        vm.Descending = true;
        vm.Limit = -5;
        vm.FirstRowOnly = true;
        vm.ResultVariable = " rows ";
        Assert.Equal("qty", step.OrderBy);
        Assert.True(step.Descending);
        Assert.Equal(0, step.Limit);
        Assert.True(step.FirstRowOnly);
        Assert.Equal("rows", step.ResultVariable);
        vm.OrderChoice = Strings.DatabaseStep_TableOrder;
        Assert.Equal("", step.OrderBy);
    }

    [Fact]
    public async Task What_the_step_names_is_shown_while_the_database_cannot_be_read()
    {
        var step = NewStep();
        step.Database = "db2";
        step.Schema = "shop";
        step.Table = "orders";
        step.Columns = [new() { Column = "name" }];
        var panel = Panel(step);
        var vm = new DatabaseStepViewModel(panel);
        await vm.Loading;

        Assert.Contains("Failed to connect", vm.Error);
        Assert.Equal(["shop"], vm.Schemas);
        Assert.Equal("orders", vm.Table);
        Assert.Equal(["name"], vm.Columns.Select(c => c.Name));
        Assert.True(vm.Columns[0].IsUsed);

        // A column the table no longer has is marked, and can be removed.
        _services.Databases["db2"].Database.Throws = null;
        _services.Databases["db2"].Database.Table("shop", "orders", new TableColumn("id", "bigint", false, null));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Null(vm.Error);
        Assert.Equal(["id", "name"], vm.Columns.Select(c => c.Name));
        Assert.True(vm.Columns[1].IsMissing);
        vm.Columns[1].RemoveCommand.Execute(null);
        Assert.Empty(step.Columns);
        Assert.Equal(["id"], vm.Columns.Select(c => c.Name));

        step.Database = "gone";
        Assert.Equal(Strings.DatabaseStep_Gone, panel.DatabaseText);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("There is no database gone.", vm.Error);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task A_new_connection_is_saved_with_its_password_kept_apart_and_chosen()
    {
        var step = NewStep();
        var vm = new DatabaseStepViewModel(Panel(step));
        await vm.Loading;
        Assert.True(vm.CanAddConnection);

        vm.AddConnectionCommand.Execute(null);
        var editor = vm.NewConnection!;
        Assert.True(editor.IsNew);
        editor.Name = "";
        editor.SaveCommand.Execute(null);
        Assert.Equal(Strings.Connection_EnterAName, editor.Error);
        editor.Name = "Reports";
        editor.Host = "db.example";
        editor.Port = 6543;
        editor.Password = "s3cret";
        await editor.TestCommand.ExecuteAsync(null);
        Assert.Null(editor.Error);
        Assert.Equal(string.Format(Strings.Connection_Works, 2), editor.Status);
        editor.SaveCommand.Execute(null);

        Assert.Null(vm.NewConnection);
        var saved = Assert.Single(_services.Connections!.All);
        Assert.Equal("postgres://postgres@db.example:6543/postgres", saved.ConnectionText);
        Assert.Equal("s3cret", _credentials.Saved[saved.Account]);
        Assert.Equal(saved.Id, step.Database);
        Assert.Equal("Reports", vm.SelectedDatabase?.Name);
        Assert.Contains("s3cret", _credentials.Saved.Values);
        Assert.DoesNotContain("s3cret", System.IO.File.ReadAllText(Data.At("settings/workflows.json")));

        vm.AddConnectionCommand.Execute(null);
        vm.NewConnection!.CancelCommand.Execute(null);
        Assert.Null(vm.NewConnection);
    }
}
