using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Agent.Tools;
using Joktan.Tests.TestSupport;
using Joktan.Workflows;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Tests;
using Xunit;

namespace Joktan.Agent.Tests.Tools;

[Collection(ProcessStateCollection.Name)]
public sealed class WorkflowToolsTests : DataFolderTest
{
    private readonly FakeWorkflowServices _services = new();
    private readonly WorkflowEngine _engine;

    public WorkflowToolsTests()
    {
        _engine = new WorkflowEngine(new WorkflowLibrary(Data.At("definitions")), new WorkflowSettings(), _services, new RunStore(Data.At("runs")))
        {
            Pages = new PageLibrary(Data.At("pages")),
        };
    }

    protected override void Dispose(bool disposing)
    {
        _engine.Dispose();
        base.Dispose(disposing);
    }

    private async Task<JsonNode?> Call(string name, string arguments = "{}")
    {
        var tool = WorkflowTools.Create(_engine).Single(t => t.Name == name);
        using var json = JsonDocument.Parse(arguments);
        return JsonSerializer.SerializeToNode(await tool.RunAsync(new ToolArgs(json.RootElement.Clone())));
    }

    private const string Demo = """
        {"workflow": {
          "Name": "Demo order",
          "Kind": "Long",
          "Triggers": {"Mqtt": true, "MqttClient": "Local", "MqttTopic": "demo/orders/+"},
          "Steps": [
            {"Id": "start", "Type": "Start", "Name": "Start"},
            {"Id": "check", "Type": "Condition", "Left": "{{input.payload.qty}}", "Operator": "GreaterThan", "Right": "0", "Labels": {"true": "Valid"}},
            {"Id": "done", "Type": "End", "Value": "{{last}}"},
            {"Type": "Fail", "Message": "No quantity"}
          ],
          "Links": [
            {"From": "start", "To": "check"},
            {"From": "check", "Output": "true", "To": "done"}
          ]
        }, "folder": "Demo"}
        """;

    [Fact]
    public async Task Saves_a_new_workflow_in_its_folder_and_lays_out_its_steps()
    {
        var saved = await Call("workflow_save", Demo);

        var workflow = Assert.Single(_engine.Library.All);
        Assert.Equal(saved!["Id"]!.GetValue<string>(), workflow.Id);
        Assert.Equal("Demo", _engine.Library.FolderOf(workflow.Id));
        Assert.Equal(WorkflowKind.Long, workflow.Kind);
        Assert.True(workflow.Triggers.Mqtt);
        Assert.Equal(CompareOperator.GreaterThan, workflow.Steps[1].Operator);
        Assert.Equal("Valid", workflow.Steps[1].Labels["true"]);

        // The step given no id got one, and a name; none sit on top of another.
        Assert.All(workflow.Steps, s => Assert.NotEmpty(s.Id));
        Assert.Equal("Fail", workflow.Steps[3].Name);
        Assert.Equal(workflow.Steps.Count, workflow.Steps.Select(s => (s.X, s.Y)).Distinct().Count());
        Assert.True(workflow.Steps[1].X > workflow.Steps[0].X);

        // What the designer would show: the fail step is never reached.
        var problems = saved["Problems"]!.AsArray();
        Assert.Contains(problems, p => p!["Step"]?.GetValue<string>() == "Fail");
    }

    [Fact]
    public async Task Changes_a_workflow_given_its_id_and_reads_it_back()
    {
        var id = (await Call("workflow_save", Demo))!["Id"]!.GetValue<string>();
        var read = await Call("workflow_get", """{"workflow": "Demo order"}""");
        var definition = read!["Workflow"]!.AsObject();
        definition["Description"] = "Changed";

        await Call("workflow_save", new JsonObject { ["workflow"] = definition.DeepClone() }.ToJsonString());

        var workflow = Assert.Single(_engine.Library.All);
        Assert.Equal(id, workflow.Id);
        Assert.Equal("Changed", workflow.Description);
        Assert.Equal("Demo", _engine.Library.FolderOf(id));
    }

    [Fact]
    public async Task Refuses_a_second_workflow_with_the_same_name()
    {
        await Call("workflow_save", Demo);

        var failure = await Assert.ThrowsAsync<ToolFailure>(() => Call("workflow_save", """{"workflow": {"Name": "demo ORDER"}}"""));

        Assert.Contains("already", failure.Message);
    }

    [Fact]
    public async Task Points_out_clients_pages_and_databases_that_are_not_there()
    {
        var saved = await Call("workflow_save", """
            {"workflow": {"Name": "Missing", "Steps": [
              {"Id": "a", "Type": "Start"},
              {"Id": "b", "Type": "MqttPublish", "Client": "Nobody", "Topic": "x"},
              {"Id": "c", "Type": "UserInput", "PageId": "nopage"},
              {"Id": "d", "Type": "DatabaseRead", "Database": "nodb", "Table": "t"}
            ], "Links": [{"From": "a", "To": "b"}, {"From": "b", "To": "c"}, {"From": "c", "To": "d"}]}}
            """);

        var texts = saved!["Problems"]!.AsArray().Select(p => p!["Text"]!.GetValue<string>()).ToList();
        Assert.Contains(texts, t => t.Contains("Nobody"));
        Assert.Contains(texts, t => t.Contains("nopage"));
        Assert.Contains(texts, t => t.Contains("database_list"));
    }

    [Fact]
    public async Task Saves_a_page_that_a_user_input_step_can_show()
    {
        var saved = await Call("workflow_page_save", """
            {"page": {"Name": "Approve order", "Title": "Order {{vars.orderId}}", "Items": [
              {"Type": "Value", "Label": "Total", "Text": "{{vars.total}}"},
              {"Type": "DropDown", "Name": "decision", "Options": ["Approve", "Reject"], "Required": true},
              {"Type": "TextArea", "Name": "comment"}
            ]}}
            """);

        var page = Assert.Single(_engine.Pages.All);
        Assert.Equal(saved!["Id"]!.GetValue<string>(), page.Id);
        Assert.Null(saved["Problem"]);
        Assert.Equal(["decision", "comment"], page.Inputs.Select(i => i.Name));
        Assert.Equal(PageItemType.DropDown, page.Items[1].Type);

        var listed = await Call("workflow_pages");
        Assert.Equal("Approve order", listed![0]!["Name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Gives_back_what_is_wrong_with_a_page()
    {
        var saved = await Call("workflow_page_save", """{"page": {"Name": "Bad", "Items": [{"Type": "DropDown", "Name": "pick"}]}}""");

        Assert.NotNull(saved!["Problem"]);
    }

    [Fact]
    public async Task Describes_every_type_of_step()
    {
        var format = await Call("workflow_step_types");

        var steps = format!["Steps"]!.AsArray();
        Assert.Equal(StepTypes.All.Count, steps.Count);
        Assert.All(steps, s => Assert.NotEmpty(s!["Settings"]!.AsArray()));
    }

    [Fact]
    public async Task Only_deleting_asks_for_approval()
    {
        var tools = WorkflowTools.Create(_engine).ToDictionary(t => t.Name);

        Assert.Equal(ToolRisk.Change, tools["workflow_save"].Risk);
        Assert.Equal(ToolRisk.Change, tools["workflow_page_save"].Risk);
        Assert.Equal(ToolRisk.Risky, tools["workflow_delete"].Risk);
        Assert.Equal(ToolRisk.Risky, tools["workflow_page_delete"].Risk);
        await Task.CompletedTask;
    }
}
