using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Engine;

public class WebhookTriggersTests : EngineTest
{
    private Workflow Hooked(Flow flow, string path = "orders/new", bool wait = false)
    {
        flow.Workflow.Triggers.Webhook = true;
        flow.Workflow.Triggers.WebhookPath = path;
        flow.Workflow.Triggers.WebhookWait = wait;
        Library.Save(flow.Workflow);
        return flow.Workflow;
    }

    private WebhookTriggers Hooks => new(Engine);

    [Fact]
    public async Task A_post_starts_the_workflow_with_its_body_and_answers_at_once()
    {
        var workflow = Hooked(new Flow("Orders").Then(Flow.Delay("200ms")).Then(Flow.End("{{input.order}}")));

        var answer = await Hooks.AnswerAsync(workflow, new WebhookCall("POST", "orders/new", "source=shop", """{"order": 42}"""));

        Assert.Equal(202, answer.Status);
        var run = Engine.Find((string)answer.Body["runId"]!)!;
        Assert.Equal(RunTrigger.Webhook, run.Trigger);
        Assert.Equal("shop", (string)run.Data[Templates.Run]!["request"]!["query"]!["source"]!);
        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(42, (int)finished.Output!);
    }

    [Fact]
    public async Task One_that_waits_answers_with_the_output_or_the_failure()
    {
        var done = Hooked(new Flow("Quote").Then(Flow.End("""{"price": {{input.qty}}}""")), "quote", wait: true);
        var answer = await Hooks.AnswerAsync(done, new WebhookCall("GET", "quote", "qty=3", ""));
        Assert.Equal(200, answer.Status);
        Assert.Equal(3, (int)answer.Body["output"]!["price"]!);

        var broken = Hooked(new Flow("Broken").Then(new WorkflowStep { Type = StepType.Fail, Message = "no stock" }), "broken", wait: true);
        answer = await Hooks.AnswerAsync(broken, new WebhookCall("POST", "broken", "", "{}"));
        Assert.Equal(500, answer.Status);
        Assert.Contains("no stock", (string)answer.Body["error"]!);
    }

    [Fact]
    public async Task Bad_requests_and_refused_runs_are_answered_as_such()
    {
        var flow = new Flow("Strict").Then(Flow.End());
        flow.Workflow.Globals.Add(new GlobalVariable { Name = "id", Value = "{{input.id}}", Required = true });
        var workflow = Hooked(flow);

        Assert.Equal(400, (await Hooks.AnswerAsync(workflow, new WebhookCall("POST", "orders/new", "", "not json"))).Status);
        Assert.Equal(405, (await Hooks.AnswerAsync(workflow, new WebhookCall("DELETE", "orders/new", "", ""))).Status);
        Assert.Equal(422, (await Hooks.AnswerAsync(workflow, new WebhookCall("POST", "orders/new", "", "{}"))).Status);

        Settings.WebhookTriggers = false;
        Assert.Equal(503, (await Hooks.AnswerAsync(workflow, new WebhookCall("POST", "orders/new", "", """{"id": 1}"""))).Status);
    }

    [Fact]
    public void Paths_are_found_whatever_their_case_and_slashes_and_checked()
    {
        var workflow = Hooked(new Flow("Orders").Then(Flow.End()), "Orders/New");
        Hooked(new Flow("Off").Then(Flow.End()), "off").Triggers.Webhook = false;

        Assert.Equal(workflow.Id, Hooks.Find("/orders/new/")?.Id);
        Assert.Null(Hooks.Find("off"));
        Assert.Null(Hooks.Find("nothing"));

        Assert.Null(WebhookTriggers.PathProblem("orders/new-1.v2"));
        Assert.NotNull(WebhookTriggers.PathProblem(""));
        Assert.NotNull(WebhookTriggers.PathProblem("orders//new"));
        Assert.NotNull(WebhookTriggers.PathProblem("orders/../secret"));
        Assert.NotNull(WebhookTriggers.PathProblem("orders?x=1"));

        var twin = Hooked(new Flow("Twin").Then(Flow.End()));
        Assert.Equal(workflow.Id, WebhookTriggers.SharedWith(Library, twin)?.Id);
    }

    [Fact]
    public void A_webhook_with_no_path_is_pointed_out()
    {
        var flow = new Flow("Hook").Then(Flow.End());
        flow.Workflow.Triggers.Webhook = true;

        Assert.Contains(WorkflowCheck.Problems(flow.Workflow), p => p.StepId is null && p.Text.Contains("path"));
        Assert.Null(JsonNode.Parse("{}")!["missing"]);
    }
}
