using System.Linq;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Tests.Engine;
using Xunit;

namespace Joktan.Workflows.Tests.Steps;

[Collection(ProcessStateCollection.Name)]
public class MqttStepsTests : EngineTest
{
    [Fact]
    public async Task A_message_written_on_the_step_is_published_with_paths_filled_in()
    {
        var step = new WorkflowStep { Type = StepType.MqttPublish, Client = "Local", Topic = "tanks/{{input.tank}}/set", Body = """{"level": {{input.level}}}""", Qos = 1, Retain = true };

        var run = await RunAsync(new Flow().Then(step), """{"tank": 4, "level": 70}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        var sent = Assert.Single(Services.Published);
        Assert.Equal(("Local", "tanks/4/set", """{"level": 70}""", 1, true), sent);
        Assert.Equal("tanks/4/set", (string?)run.Output!["topic"]);
    }

    [Theory]
    [InlineData(BodyMode.Keep, "", """{"level":10,"unit":"%"}""")]
    [InlineData(BodyMode.Replace, "", """{"level":99}""")]
    [InlineData(BodyMode.Merge, "", """{"level":99,"unit":"%"}""")]
    [InlineData(BodyMode.Keep, "other/topic", """{"level":10,"unit":"%"}""")]
    public async Task A_folder_message_is_sent_with_its_body_kept_replaced_or_merged_into(BodyMode mode, string topic, string body)
    {
        Services.Messages["tank.json"] = new SavedMessage("tank.json", "tanks/1/level", """{"level":10,"unit":"%"}""", 2, false, true);
        var step = new WorkflowStep { Type = StepType.MqttPublish, Client = "Local", MessageFile = "tank.json", Topic = topic, BodyMode = mode, Body = """{"level":{{input.level}}}""" };

        var run = await RunAsync(new Flow().Then(step), """{"level": 99}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        var sent = Assert.Single(Services.Published);
        Assert.Equal(topic.Length > 0 ? topic : "tanks/1/level", sent.Topic);
        Assert.Equal(body, sent.Payload);
        Assert.Equal(2, sent.Qos);
    }

    [Fact]
    public async Task A_topic_that_cannot_be_published_to_fails_the_step()
    {
        var run = await RunAsync(new Flow().Then(new WorkflowStep { Type = StepType.MqttPublish, Client = "Local", Topic = "a/+/b" }));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Empty(Services.Published);
    }

    [Fact]
    public async Task A_wait_ends_with_the_first_message_that_passes_its_test()
    {
        var wait = new WorkflowStep
        {
            Type = StepType.MqttWait, Client = "Local", Topic = "tanks/+/level", ResultVariable = "message",
            Left = "{{message.payload.level}}", Operator = CompareOperator.GreaterThan, Right = "{{input.limit}}",
        };
        var flow = new Flow().Then(wait).Then(Flow.End("{{vars.message.topic}}"));
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, System.Text.Json.Nodes.JsonNode.Parse("""{"limit": 80}"""), RunTrigger.Manual);
        await Until(() => Services.Listening == 1, "The step never listened.");

        Services.Deliver("tanks/1/level", """{"level": 50}""");
        Services.Deliver("tanks/2/temperature", """{"level": 99}""");
        Services.Deliver("tanks/3/level", """{"level": 90}""");

        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal("\"tanks/3/level\"", JsonText.Of(finished.Output));
        Assert.Equal(0, Services.Listening);
    }

    [Fact]
    public async Task A_wait_that_runs_out_of_time_goes_out_by_timeout()
    {
        var flow = new Flow();
        var wait = flow.Add(new WorkflowStep { Type = StepType.MqttWait, Client = "Local", Topic = "never/#", Duration = "200ms" });
        var late = flow.Add(Flow.End("\"nothing came\""));
        flow.Link(flow.Last, wait).Link(wait, late, StepOutputs.Timeout);

        var run = await RunAsync(flow);

        Assert.Equal("\"nothing came\"", JsonText.Of(run.Output));
        Assert.Equal(0, Services.Listening);
    }

    [Fact]
    public async Task A_wait_on_a_client_that_does_not_exist_fails_the_step()
    {
        var run = await RunAsync(new Flow().Then(new WorkflowStep { Type = StepType.MqttWait, Client = "Nowhere", Topic = "a/#" }));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("Nowhere", run.Error);
    }
}
