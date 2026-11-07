using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Tests.Engine;
using Xunit;

namespace Joktan.Workflows.Tests.Steps;

[Collection(ProcessStateCollection.Name)]
public class FlowStepsTests : EngineTest
{
    // A loop over items whose body adds each to a total and comes back, ending with the total.
    private static (Flow Flow, WorkflowStep Loop) Summing(WorkflowStep loop)
    {
        var flow = new Flow();
        var start = flow.Last;
        var zero = flow.Add(Flow.Set("total", "0"));
        flow.Add(loop);
        var add = flow.Add(Flow.CSharp("Vars[\"total\"] = (double)Vars[\"total\"]! + (double)Vars[\"item\"]!; return null;"));
        var end = flow.Add(Flow.End("{{vars.total}}"));
        flow.Link(start, zero).Link(zero, loop).Link(loop, add, StepOutputs.Loop).Link(add, loop).Link(loop, end, StepOutputs.Done);
        return (flow, loop);
    }

    [Fact]
    public async Task For_each_goes_round_once_for_each_item()
    {
        var (flow, loop) = Summing(new WorkflowStep { Type = StepType.ForEach, Items = "{{input.amounts}}" });

        var run = await RunAsync(flow, """{"amounts": [1.5, 2, 3]}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("6.5", JsonText.Of(run.Output));
        Assert.Equal(4, run.Steps.Count(s => s.StepId == loop.Id));
        Assert.Equal(StepOutputs.Done, run.Steps.Last(s => s.StepId == loop.Id).Output);
        Assert.Equal(2, (int?)run.Data[Templates.Vars]!["index"]);
        Assert.Empty(run.Loops);
    }

    [Fact]
    public async Task For_each_counts_when_given_a_number_and_goes_through_an_object_by_its_entries()
    {
        var count = new Flow().Then(new WorkflowStep { Type = StepType.ForEach, Items = "{{input.n}}", ItemVariable = "i" });
        var counted = count.Last;
        var collect = count.Add(Flow.CSharp("var seen = Vars[\"seen\"] as JsonArray ?? new JsonArray(); seen.Add((int)Vars[\"i\"]!); Vars[\"seen\"] = seen; return null;"));
        count.Link(counted, collect, StepOutputs.Loop).Link(collect, counted);
        var end = count.Add(Flow.End("{{vars.seen}}"));
        count.Link(counted, end, StepOutputs.Done);

        Assert.Equal("[0,1,2]", JsonText.Of((await RunAsync(count, """{"n": 3}""")).Output));

        var entries = new Flow("Entries").Then(new WorkflowStep { Type = StepType.ForEach, Items = """{"a": 1}""", ItemVariable = "entry" });
        var loop = entries.Last;
        var end2 = entries.Add(Flow.End("{{vars.entry}}"));
        entries.Link(loop, loop, StepOutputs.Loop).Link(loop, end2, StepOutputs.Done);
        Assert.Equal("""{"key":"a","value":1}""", JsonText.Of((await RunAsync(entries)).Output));
    }

    [Fact]
    public async Task For_each_of_nothing_goes_straight_to_done_and_of_text_fails()
    {
        var (empty, _) = Summing(new WorkflowStep { Type = StepType.ForEach, Items = "{{input.none}}" });
        Assert.Equal("0", JsonText.Of((await RunAsync(empty)).Output));

        var (wrong, _) = Summing(new WorkflowStep { Type = StepType.ForEach, Items = "\"words\"" });
        var failed = await RunAsync(wrong);
        Assert.Equal(RunStatus.Failed, failed.Status);
        Assert.Contains("cannot be gone through", failed.Error);
    }

    [Fact]
    public async Task While_goes_round_as_long_as_its_test_holds()
    {
        var flow = new Flow();
        var loop = flow.Add(new WorkflowStep { Type = StepType.While, Left = "{{vars.n}}", Operator = CompareOperator.LessThan, Right = "4", IndexVariable = "times" });
        var grow = flow.Add(Flow.CSharp("Vars[\"n\"] = ((int?)Vars[\"n\"] ?? 0) + 1; return null;"));
        var end = flow.Add(Flow.End("{\"n\": {{vars.n}}, \"times\": {{vars.times}}}"));
        flow.Link(flow.Last, loop).Link(loop, grow, StepOutputs.Loop).Link(grow, loop).Link(loop, end, StepOutputs.Done);

        var run = await RunAsync(flow);

        Assert.Equal("""{"n":4,"times":3}""", JsonText.Of(run.Output));
    }

    [Theory]
    [InlineData("paid", "\"shipping\"")]
    [InlineData("200", "\"ok\"")]
    [InlineData("other", "\"unknown\"")]
    public async Task Switch_goes_out_by_the_case_that_matches_or_by_default(string status, string expected)
    {
        var flow = new Flow();
        var choose = flow.Add(new WorkflowStep { Type = StepType.Switch, Left = "{{input.status}}", Cases = ["new", "paid", "200"] });
        var paid = flow.Add(Flow.End("\"shipping\""));
        var ok = flow.Add(Flow.End("\"ok\""));
        var other = flow.Add(Flow.End("\"unknown\""));
        flow.Link(flow.Last, choose).Link(choose, paid, StepOutputs.Case("paid")).Link(choose, ok, StepOutputs.Case("200")).Link(choose, other, StepOutputs.Default);
        var input = status == "200" ? """{"status": 200}""" : $$"""{"status": "{{status}}"}""";

        var run = await RunAsync(flow, input);

        Assert.Equal(expected, JsonText.Of(run.Output));
    }

    [Fact]
    public void A_switch_has_an_output_for_each_case_then_default_and_error()
    {
        var step = new WorkflowStep { Type = StepType.Switch, Cases = ["a", "", "b", "a"] };

        Assert.Equal(["case:a", "case:b", "default", "error"], StepTypes.Outputs(step));
        Assert.Equal("= a", StepTypes.OutputText("case:a"));
    }

    [Fact]
    public void A_loop_whose_body_never_comes_back_is_pointed_out()
    {
        var flow = new Flow();
        var loop = flow.Add(new WorkflowStep { Type = StepType.ForEach, Items = "3" });
        var body = flow.Add(Flow.Log("x"));
        flow.Link(flow.Last, loop).Link(loop, body, StepOutputs.Loop);

        Assert.Contains(WorkflowCheck.Problems(flow.Workflow), p => p.StepId == loop.Id);

        flow.Link(body, loop);
        Assert.DoesNotContain(WorkflowCheck.Problems(flow.Workflow), p => p.StepId == loop.Id);
    }

    [Fact]
    public async Task Global_variables_are_set_from_the_input_before_the_first_step()
    {
        var flow = new Flow().Then(Flow.End("{\"id\": {{vars.orderId}}, \"region\": \"{{vars.region}}\", \"limits\": {{vars.limits}}}"));
        flow.Workflow.Globals =
        [
            new GlobalVariable { Name = "orderId", Value = "{{input.order.id}}", Required = true },
            new GlobalVariable { Name = "region", Value = "eu" },
            new GlobalVariable { Name = "limits", Value = """{"max": 10}""" },
        ];

        var run = await RunAsync(flow, """{"order": {"id": 42}}""");

        Assert.Equal("""{"id":42,"region":"eu","limits":{"max":10}}""", JsonText.Of(run.Output));
    }

    [Fact]
    public void A_run_missing_a_required_global_is_refused_before_it_starts()
    {
        var flow = new Flow("Orders").Then(Flow.End());
        flow.Workflow.Globals = [new GlobalVariable { Name = "orderId", Value = "{{input.order.id}}", Required = true }];

        var e = Assert.Throws<WorkflowException>(() => Engine.Start(flow.Workflow, JsonNode.Parse("""{"order": {}}"""), RunTrigger.Rest));

        Assert.Contains("orderId", e.Message);
        Assert.Empty(Engine.Runs);
    }

    [Fact]
    public void Global_names_must_be_names_and_differ()
    {
        var flow = new Flow().Then(Flow.End());
        flow.Workflow.Globals = [new() { Name = "1st" }, new() { Name = "a" }, new() { Name = "a" }];

        Assert.Equal(2, WorkflowCheck.Problems(flow.Workflow).Count(p => p.StepId is null));
    }

    [Fact]
    public async Task An_mqtt_message_that_is_not_json_starts_no_run()
    {
        var flow = new Flow("Listener").Then(Flow.End());
        flow.Workflow.Triggers = new WorkflowTriggers { Mqtt = true, MqttClient = "Local", MqttTopic = "a/#" };
        Library.Save(flow.Workflow);
        await Until(() => Services.Listening == 1, "The trigger never listened.");

        Services.Deliver("a/b", "not json at all");

        Assert.Empty(Engine.Runs);
        Assert.Contains("not JSON", Engine.TriggerProblems[flow.Workflow.Id]);

        Services.Deliver("a/b", "{}");
        await Until(() => Engine.Runs.Count == 1, "A JSON message started no run.");
        Assert.False(Engine.TriggerProblems.ContainsKey(flow.Workflow.Id));
    }
}
