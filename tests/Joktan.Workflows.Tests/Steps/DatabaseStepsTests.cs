using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Data.Postgres;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Steps;
using Joktan.Workflows.Tests.Engine;
using Xunit;

namespace Joktan.Workflows.Tests.Steps;

[Collection(ProcessStateCollection.Name)]
public class DatabaseStepsTests : EngineTest
{
    private static WorkflowStep Step(StepType type, string table = "orders") => new()
    {
        Type = type, Database = "db1", Schema = "shop", Table = table, ResultVariable = "result",
    };

    private static DatabaseFilter Where(string column, string value, CompareOperator op = CompareOperator.Equals) => new() { Column = column, Operator = op, Value = value };

    [Fact]
    public async Task A_read_gives_its_rows_and_puts_columns_in_variables_as_single_values_or_lists()
    {
        var database = Services.AddDatabase();
        database.Answer = _ => """[{"id": 1, "name": "tea", "qty": 2}, {"id": 2, "name": "cake", "qty": 5}]""";
        var read = Step(StepType.DatabaseRead);
        read.Columns = [new() { Column = "id" }, new() { Column = "name", Variable = "firstName" }, new() { Column = "qty", Variable = "quantities", AllRows = true }];
        read.Filters = [Where("qty", "{{input.min}}", CompareOperator.GreaterOrEqual), new() { Column = "gone", Operator = CompareOperator.IsEmpty }];
        read.OrderBy = "id";
        read.Limit = 50;

        var run = await RunAsync(new Flow().Then(read).Then(Flow.End("{{vars.result}}")), """{"min": 2}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("""[{"id":1,"name":"tea","qty":2},{"id":2,"name":"cake","qty":5}]""", JsonText.Of(run.Output));
        Assert.Equal("\"tea\"", JsonText.Of(run.Data[Templates.Vars]!["firstName"]));
        Assert.Equal("[2,5]", JsonText.Of(run.Data[Templates.Vars]!["quantities"]));
        var sent = Assert.Single(database.Sent);
        Assert.Equal(RowQueries.Select("shop", "orders", ["id", "name", "qty"], [new("qty", FilterKind.GreaterOrEqual, "2"), new("gone", FilterKind.IsEmpty)], "id", false, 50).Sql, sent.Sql);
        Assert.Equal(["2"], sent.Parameters);
        Assert.Contains(run.Log, l => l.Text == "Read 2 rows from shop.orders.");
        Assert.Equal(1, database.Disposed);
    }

    [Fact]
    public async Task The_result_can_be_the_first_row_alone_which_is_null_without_rows()
    {
        var database = Services.AddDatabase();
        database.Answer = _ => """[{"name": "tea", "id": 7}]""";
        var first = Step(StepType.DatabaseRead);
        first.FirstRowOnly = true;
        Assert.Equal("""{"name":"tea","id":7}""", JsonText.Of((await RunAsync(new Flow().Then(first).Then(Flow.End("{{vars.result}}")))).Output));

        database.Answer = _ => "[]";
        var none = Step(StepType.DatabaseRead);
        none.FirstRowOnly = true;
        none.Columns = [new() { Column = "name", Variable = "name" }, new() { Column = "id", Variable = "ids", AllRows = true }];
        var run = await RunAsync(new Flow("Empty").Then(none));
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Null(run.Data[Templates.Vars]!["result"]);
        Assert.Null(run.Data[Templates.Vars]!["name"]);
        Assert.Equal("[]", JsonText.Of(run.Data[Templates.Vars]!["ids"]));
    }

    [Fact]
    public async Task An_insert_sends_its_values_as_text_and_puts_the_new_id_in_a_variable()
    {
        var database = Services.AddDatabase();
        database.Answer = _ => """[{"id": 9, "name": "tea", "extra": {"cup": "big"}, "paid": true}]""";
        var insert = Step(StepType.DatabaseInsert);
        insert.Columns = [new() { Column = "id", SetsValue = false, Variable = "orderId" }, new() { Column = "name", Value = "{{input.name}}" },
            new() { Column = "extra", Value = "{{input.extra}}" }, new() { Column = "paid", Value = "true" }, new() { Column = "note", Value = "" },
            new() { Column = "qty", Value = "{{input.qty}}" }];

        var run = await RunAsync(new Flow().Then(insert).Then(Flow.End("{{vars.orderId}}")), """{"name": "tea", "extra": {"cup": "big"}, "qty": 3}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("9", JsonText.Of(run.Output));
        var sent = Assert.Single(database.Sent);
        Assert.StartsWith("WITH t AS (INSERT INTO \"shop\".\"orders\" (\"name\", \"extra\", \"paid\", \"note\", \"qty\") VALUES ($1, $2, $3, $4, $5)", sent.Sql);
        Assert.Equal(["tea", """{"cup":"big"}""", "true", null, "3"], sent.Parameters);
        Assert.Equal(9, (int?)run.Data[Templates.Vars]!["result"]![0]!["id"]);
        Assert.Contains(run.Log, l => l.Text == "Inserted 1 row into shop.orders.");
    }

    [Fact]
    public async Task An_insert_of_many_rows_makes_one_for_each_item_and_puts_the_item_variable_back()
    {
        var database = Services.AddDatabase();
        database.Answer = statement => new JsonArray(Enumerable.Range(0, statement.Parameters.Count / 2).Select(i => (JsonNode?)new JsonObject { ["id"] = i }).ToArray()).ToJsonString();
        var insert = Step(StepType.DatabaseInsert);
        insert.ManyRows = true;
        insert.Items = "{{input.lines}}";
        insert.ItemVariable = "line";
        insert.Columns = [new() { Column = "name", Value = "{{vars.line.name}}" }, new() { Column = "order", Value = "{{vars.orderId}}" },
            new() { Column = "id", SetsValue = false, Variable = "ids", AllRows = true }];
        var flow = new Flow().Then(Flow.Set("orderId", "42")).Then(Flow.Set("line", "\"kept\"")).Then(insert).Then(Flow.End("{{vars}}"));

        var run = await RunAsync(flow, """{"lines": [{"name": "tea"}, {"name": "cake"}, {"name": null}]}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        var sent = Assert.Single(database.Sent);
        Assert.Equal(["tea", "42", "cake", "42", null, "42"], sent.Parameters);
        Assert.Equal("[0,1,2]", JsonText.Of(run.Output!["ids"]));
        Assert.Equal("\"kept\"", JsonText.Of(run.Output["line"]));

        // Without a variable of that name before, none is left after; and no items insert nothing.
        var again = Step(StepType.DatabaseInsert);
        again.ManyRows = true;
        again.Items = "{{input.lines}}";
        again.Columns = [new() { Column = "name", Value = "{{vars.item.name}}" }];
        var none = await RunAsync(new Flow("None").Then(again).Then(Flow.End("{{vars}}")), """{"lines": []}""");
        Assert.Equal(RunStatus.Completed, none.Status);
        Assert.Equal("[]", JsonText.Of(none.Output!["result"]));
        Assert.Null(none.Output["item"]);
        Assert.Single(database.Sent);

        var notList = await RunAsync(new Flow("Not a list").Then(again), """{"lines": 5}""");
        Assert.Equal(RunStatus.Failed, notList.Status);
        Assert.Contains("is not a list", notList.Error);
    }

    [Fact]
    public async Task An_update_changes_the_rows_its_filters_choose()
    {
        var database = Services.AddDatabase();
        database.Answer = _ => """[{"id": 7, "status": "paid"}]""";
        var update = Step(StepType.DatabaseUpdate);
        update.Columns = [new() { Column = "status", Value = "paid" }, new() { Column = "paid_at", Value = "" }, new() { Column = "id", SetsValue = false, Variable = "changed", AllRows = true }];
        update.Filters = [Where("id", "{{input.id}}")];

        var run = await RunAsync(new Flow().Then(update).Then(Flow.End("{{vars.changed}}")), """{"id": 7}""");

        Assert.Equal("[7]", JsonText.Of(run.Output));
        var sent = Assert.Single(database.Sent);
        Assert.Equal(RowQueries.Update("shop", "orders", [new("status", "paid"), new("paid_at", null)], [new("id", FilterKind.Equal, "7")]).Sql, sent.Sql);
        Assert.Equal(["paid", null, "7"], sent.Parameters);
        Assert.Contains(run.Log, l => l.Text == "Updated 1 row of shop.orders.");
    }

    [Theory]
    [InlineData(StepType.DatabaseUpdate)]
    [InlineData(StepType.DatabaseDelete)]
    public async Task An_update_or_delete_never_runs_unless_its_filters_have_values(StepType type)
    {
        var database = Services.AddDatabase();
        WorkflowStep Make(params DatabaseFilter[] filters)
        {
            var step = Step(type);
            step.Columns = [new() { Column = "status", Value = "x" }];
            step.Filters = [.. filters];
            return step;
        }

        async Task<string?> Fails(string name, WorkflowStep step, string input = "{}")
        {
            var run = await RunAsync(new Flow(name).Then(step), input);
            Assert.Equal(RunStatus.Failed, run.Status);
            return run.Error;
        }

        Assert.Contains("Add a filter", await Fails("None", Make()));
        Assert.Contains("Add a filter", await Fails("Blank column", Make(new DatabaseFilter { Column = " ", Value = "1" })));
        Assert.Contains("filter on id came out empty (no value is given)", await Fails("No value", Make(Where("id", ""))));
        Assert.Contains("filter on id came out empty ({{input.id}})", await Fails("Missing", Make(Where("id", "{{input.id}}"))));
        Assert.Contains("came out empty", await Fails("Null", Make(Where("id", "{{input.id}}")), """{"id": null}"""));
        Assert.Contains("came out empty", await Fails("Spaces", Make(Where("name", "{{input.name}}", CompareOperator.Contains)), """{"name": "  "}"""));
        Assert.Contains("came out empty", await Fails("One of two", Make(Where("id", "1"), Where("shop", "{{input.shop}}"))));
        Assert.Contains("At least one filter must compare", await Fails("Only empty", Make(new DatabaseFilter { Column = "done", Operator = CompareOperator.IsNotEmpty })));
        Assert.Empty(database.Sent);
        Assert.Equal(0, database.Opened);

        // With values, and a test for emptiness beside them, it runs.
        var run = await RunAsync(new Flow("Fine").Then(Make(Where("id", "{{input.id}}"), new DatabaseFilter { Column = "done", Operator = CompareOperator.IsEmpty })), """{"id": 0}""");
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(["0"], database.Sent.Single().Parameters.TakeLast(1));
    }

    [Fact]
    public async Task A_delete_gives_the_rows_it_deleted()
    {
        var database = Services.AddDatabase();
        database.Answer = _ => """[{"id": 3}, {"id": 4}]""";
        var delete = Step(StepType.DatabaseDelete);
        delete.Filters = [Where("order_id", "{{input.order}}"), new() { Column = "sent", Operator = CompareOperator.IsEmpty }];

        var run = await RunAsync(new Flow().Then(delete).Then(Flow.End("{{vars.result}}")), """{"order": 12}""");

        Assert.Equal("""[{"id":3},{"id":4}]""", JsonText.Of(run.Output));
        var sent = Assert.Single(database.Sent);
        Assert.Equal(RowQueries.Delete("shop", "orders", [new("order_id", FilterKind.Equal, "12"), new("sent", FilterKind.IsEmpty)]).Sql, sent.Sql);
        Assert.Contains(run.Log, l => l.Text == "Deleted 2 rows from shop.orders.");
    }

    [Fact]
    public async Task An_upsert_updates_the_row_its_keys_match_or_inserts_it_and_needs_its_keys()
    {
        var database = Services.AddDatabase();
        database.Answer = _ => """[{"sku": "T1", "price": 3}]""";
        var upsert = Step(StepType.DatabaseUpsert);
        upsert.ManyRows = true;
        upsert.Items = "{{input.prices}}";
        upsert.Columns = [new() { Column = "sku", Value = "{{vars.item.sku}}", Match = true }, new() { Column = "price", Value = "{{vars.item.price}}", Variable = "price" }];

        var run = await RunAsync(new Flow().Then(upsert).Then(Flow.End("{{vars.price}}")), """{"prices": [{"sku": "T1", "price": 3}, {"sku": "C2", "price": 4}]}""");

        Assert.Equal("3", JsonText.Of(run.Output));
        var sent = Assert.Single(database.Sent);
        Assert.Equal(RowQueries.Upsert("shop", "orders", ["sku", "price"], ["sku"], [["T1", "3"], ["C2", "4"]])[0].Sql, sent.Sql);
        Assert.Contains("ON CONFLICT (\"sku\") DO UPDATE SET \"price\" = EXCLUDED.\"price\"", sent.Sql);
        Assert.Equal(["T1", "3", "C2", "4"], sent.Parameters);

        var missingKey = await RunAsync(new Flow("No key").Then(upsert), """{"prices": [{"sku": "T1", "price": 3}, {"price": 4}]}""");
        Assert.Contains("A key column (sku) came out empty", missingKey.Error);
        upsert.Columns[0].Match = false;
        Assert.Contains("Choose the key columns", (await RunAsync(new Flow("Keyless").Then(upsert), """{"prices": []}""")).Error);
        Assert.Single(database.Sent);
    }

    [Fact]
    public async Task What_the_database_says_goes_out_by_the_error_output()
    {
        var database = Services.AddDatabase();
        database.Throws = new Npgsql.PostgresException("relation \"shop.orders\" does not exist", "ERROR", "ERROR", "42P01");
        var flow = new Flow().Then(Step(StepType.DatabaseRead));
        var read = flow.Last;
        var handled = flow.Add(Flow.End("\"handled\""));
        flow.Link(read, handled, StepOutputs.Error);

        var run = await RunAsync(flow);

        Assert.Equal("\"handled\"", JsonText.Of(run.Output));
        Assert.Contains(run.Steps, s => s.StepId == read.Id && s.Error == "The database said: relation \"shop.orders\" does not exist");

        var missing = Step(StepType.DatabaseRead);
        missing.Database = "gone";
        Assert.EndsWith(": There is no database gone.", (await RunAsync(new Flow("Gone").Then(missing))).Error);
        missing.Table = "";
        Assert.EndsWith(": Choose the table the step works on.", (await RunAsync(new Flow("No table").Then(missing))).Error);
    }

    [Theory]
    [InlineData(CompareOperator.Equals, FilterKind.Equal)]
    [InlineData(CompareOperator.NotEquals, FilterKind.NotEqual)]
    [InlineData(CompareOperator.GreaterThan, FilterKind.Greater)]
    [InlineData(CompareOperator.GreaterOrEqual, FilterKind.GreaterOrEqual)]
    [InlineData(CompareOperator.LessThan, FilterKind.Less)]
    [InlineData(CompareOperator.LessOrEqual, FilterKind.LessOrEqual)]
    [InlineData(CompareOperator.Contains, FilterKind.Contains)]
    [InlineData(CompareOperator.StartsWith, FilterKind.StartsWith)]
    [InlineData(CompareOperator.IsEmpty, FilterKind.IsEmpty)]
    [InlineData(CompareOperator.IsNotEmpty, FilterKind.IsNotEmpty)]
    [InlineData(CompareOperator.CSharp, FilterKind.Equal)]
    public void Each_comparison_has_its_filter(CompareOperator op, FilterKind kind) => Assert.Equal(kind, DatabaseSteps.Kind(op));

    [Fact]
    public void Values_are_sent_as_text_or_json()
    {
        Assert.Null(DatabaseSteps.Text(null));
        Assert.Equal("it's", DatabaseSteps.Text(JsonValue.Create("it's")));
        Assert.Equal("2.5", DatabaseSteps.Text(JsonValue.Create(2.5)));
        Assert.Equal("false", DatabaseSteps.Text(JsonValue.Create(false)));
        Assert.Equal("""{"a":[1]}""", DatabaseSteps.Text(JsonNode.Parse("""{"a": [1]}""")));
    }
}
