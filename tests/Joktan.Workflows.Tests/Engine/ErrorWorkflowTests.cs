using System.Linq;
using System.Threading.Tasks;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Engine;

public class ErrorWorkflowTests : EngineTest
{
    private static Flow Failing(string name = "Orders") => new Flow(name).Then(new WorkflowStep { Type = StepType.Fail, Name = "Refuse", Message = "boom" });

    // A workflow that ends with what it was given, to see the failure it was told about.
    private Workflow Handler(string name = "On failure")
    {
        var handler = new Flow(name).Then(Flow.End("{{input}}")).Workflow;
        Library.Save(handler);
        return handler;
    }

    private async Task<WorkflowRun> HandlerRun(Workflow handler)
    {
        await Until(() => Engine.Runs.Any(r => r.WorkflowId == handler.Id), "The error workflow never started.");
        var run = Engine.Runs.Single(r => r.WorkflowId == handler.Id);
        return await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_run_that_fails_starts_its_error_workflow_with_what_failed()
    {
        var handler = Handler();
        var flow = Failing();
        flow.Workflow.ErrorWorkflow = handler.Id;

        var failed = await RunAsync(flow, """{"order": 7}""");

        var run = await HandlerRun(handler);
        Assert.Equal((RunTrigger.Error, failed.Id), (run.Trigger, run.TriggerDetail));
        var input = run.Output!;
        Assert.Equal("Orders", (string)input["workflow"]!["name"]!);
        Assert.Equal(failed.Serial, (long)input["run"]!["serial"]!);
        Assert.Contains("boom", (string)input["error"]!);
        Assert.Equal("Refuse", (string)input["step"]!["name"]!);
        Assert.Equal(7, (int)input["input"]!["order"]!);
        Assert.Contains(failed.Log, l => l.Text.Contains("error workflow"));
    }

    [Fact]
    public async Task Workflows_that_name_none_use_the_one_on_the_settings()
    {
        var handler = Handler();
        Settings.ErrorWorkflow = handler.Id;

        await RunAsync(Failing());

        Assert.Equal(RunTrigger.Error, (await HandlerRun(handler)).Trigger);
    }

    [Fact]
    public async Task An_error_workflow_that_fails_starts_no_other()
    {
        var handler = new Flow("Fragile").Then(new WorkflowStep { Type = StepType.Fail, Message = "again" }).Workflow;
        Library.Save(handler);
        Settings.ErrorWorkflow = handler.Id;

        await RunAsync(Failing());
        var first = await HandlerRun(handler);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.Failed, first.Status);
        Assert.Single(Engine.Runs, r => r.WorkflowId == handler.Id);
    }

    [Fact]
    public async Task Runs_that_complete_or_are_killed_start_none()
    {
        var handler = Handler();
        Settings.ErrorWorkflow = handler.Id;

        await RunAsync(new Flow("Fine").Then(Flow.End()));
        var slow = new Flow("Slow").Then(Flow.Delay("1h"));
        Library.Save(slow.Workflow);
        var killed = Engine.Start(slow.Workflow, null, RunTrigger.Manual);
        await Until(killed, RunStatus.Waiting);
        Engine.Kill(killed.Id);
        await Engine.WhenFinished(killed.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(Engine.Runs, r => r.WorkflowId == handler.Id);
    }

    [Fact]
    public async Task A_failed_branch_starts_it_once_for_its_run_not_for_the_branch()
    {
        var handler = Handler();
        Settings.ErrorWorkflow = handler.Id;
        var flow = new Flow("Fork").Then(new WorkflowStep { Type = StepType.Parallel, Cases = ["a", "b"] });
        var parallel = flow.Last;
        var join = flow.Add(new WorkflowStep { Type = StepType.Join });
        var fail = flow.Add(new WorkflowStep { Type = StepType.Fail, Message = "branch broke" });
        var log = flow.Add(Flow.Log("fine"));
        flow.Link(parallel, fail, StepOutputs.Case("a")).Link(parallel, log, StepOutputs.Case("b")).Link(log, join);

        var failed = await RunAsync(flow);

        var run = await HandlerRun(handler);
        Assert.Equal(failed.Id, run.TriggerDetail);
        Assert.Single(Engine.Runs, r => r.WorkflowId == handler.Id);
    }
}
