using System.Linq;
using System.Threading.Tasks;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Engine;

public class ParallelTests : EngineTest
{
    private static WorkflowStep Js(string code) => new() { Type = StepType.Script, Language = ScriptLanguage.JavaScript, Code = code };

    /// <summary>Start, then a parallel step with a left and a right branch that meet at a join, then what follows the join.</summary>
    private static (Flow Flow, WorkflowStep Parallel, WorkflowStep Join) Fork(WorkflowStep[] left, WorkflowStep[] right, WorkflowStep after,
        WorkflowKind kind = WorkflowKind.Short)
    {
        var flow = new Flow("Fork", kind).Then(Flow.Set("x", "0"));
        var parallel = new WorkflowStep { Type = StepType.Parallel, Cases = ["left", "right"] };
        flow.Then(parallel);
        var join = flow.Add(new WorkflowStep { Type = StepType.Join, Name = "Meet" });
        foreach (var (name, steps) in new[] { ("left", left), ("right", right) })
        {
            var from = parallel;
            var output = StepOutputs.Case(name);
            foreach (var step in steps)
            {
                flow.Add(step);
                flow.Link(from, step, output);
                (from, output) = (step, StepOutputs.Next);
            }

            flow.Link(from, join, output);
        }

        flow.Add(after);
        flow.Link(join, after);
        return (flow, parallel, join);
    }

    [Fact]
    public async Task Branches_run_at_the_same_time_and_the_run_goes_on_from_their_join_with_what_each_gave()
    {
        var (flow, _, _) = Fork(
            [Js("vars.x = 5; vars.left = 'L'; return 'from left';"), Flow.Delay("300ms")],
            [Js("vars.right = 'R'; return 'from right';"), Flow.Delay("300ms")],
            Flow.End("""{"results": {{last}}, "x": {{vars.x}}, "left": "{{vars.left}}", "right": "{{vars.right}}"}"""));

        var run = await RunAsync(flow);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("from left", (string)run.Output!["results"]!["left"]!);
        Assert.Equal("from right", (string)run.Output!["results"]!["right"]!);

        // What a branch changed comes back; the right branch left x alone, so it does not undo the left one's change.
        Assert.Equal(5, (int)run.Output!["x"]!);
        Assert.Equal(("L", "R"), ((string)run.Output!["left"]!, (string)run.Output!["right"]!));

        var branches = Engine.Runs.Where(r => r.ParentRunId == run.Id).ToList();
        Assert.Equal(["left", "right"], branches.Select(b => b.Branch).Order());
        Assert.All(branches, b => Assert.Equal((RunTrigger.Branch, RunStatus.Completed), (b.Trigger, b.Status)));
        var (left, right) = (branches.Single(b => b.Branch == "left"), branches.Single(b => b.Branch == "right"));
        Assert.True(left.Started < right.Ended && right.Started < left.Ended, "The branches did not run at the same time.");

        // The run shows the join it went on from.
        Assert.Contains(run.Steps, s => s.Name == "Meet");
    }

    [Fact]
    public async Task A_branch_that_fails_stops_the_others_and_the_parallel_step_goes_out_by_its_error_output()
    {
        var (flow, parallel, _) = Fork(
            [new WorkflowStep { Type = StepType.Fail, Message = "broken" }],
            [Flow.Delay("1h")],
            Flow.End());
        var handled = flow.Add(Flow.End("\"handled\""));
        flow.Link(parallel, handled, StepOutputs.Error);

        var run = await RunAsync(flow);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("handled", (string)run.Output!);
        Assert.Contains("Branch left", run.Steps.Single(s => s.Type == StepType.Parallel).Error);
        var right = Engine.Runs.Single(r => r.ParentRunId == run.Id && r.Branch == "right");
        await Engine.WhenFinished(right.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Killed, right.Status);
    }

    [Fact]
    public async Task With_nowhere_to_go_after_a_failed_branch_the_run_fails()
    {
        var (flow, _, _) = Fork([new WorkflowStep { Type = StepType.Fail, Message = "broken" }], [Flow.Log("fine")], Flow.End());

        var run = await RunAsync(flow);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("broken", run.Error);
    }

    [Fact]
    public async Task Killing_the_run_kills_its_branches()
    {
        var (flow, _, _) = Fork([Flow.Delay("1h")], [Flow.Delay("1h")], Flow.End());
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(() => Engine.Runs.Count(r => r.ParentRunId == run.Id && r.Status == RunStatus.Waiting) == 2, "The branches never started waiting.");

        Engine.Kill(run.Id);
        await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);

        foreach (var branch in Engine.Runs.Where(r => r.ParentRunId == run.Id).ToList())
        {
            await Engine.WhenFinished(branch.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
            Assert.Equal(RunStatus.Killed, branch.Status);
        }
    }

    [Fact]
    public async Task A_long_run_waits_again_on_the_same_branches_after_a_restart()
    {
        var (flow, _, _) = Fork([Flow.Delay("600ms"), Flow.Set("left", "\"L\"")], [Flow.Delay("600ms")], Flow.End("{{vars.left}}"), WorkflowKind.Long);
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(() => Engine.Runs.Count(r => r.ParentRunId == run.Id && r.Status == RunStatus.Waiting) == 2, "The branches never started waiting.");

        // Joktan closes while the branches wait, and starts again.
        Engine.Dispose();
        Engine = new WorkflowEngine(Library, Settings, Services, Store);
        Engine.Restore();

        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Completed, finished.Status);
        Assert.Equal("L", (string)finished.Output!);
        Assert.Equal(2, Engine.Runs.Count(r => r.ParentRunId == run.Id));
        Assert.Contains(finished.Log, l => l.Text.Contains("Waiting again for branch"));
    }

    [Fact]
    public void The_check_asks_for_two_branches_that_meet_at_one_join()
    {
        var (good, _, _) = Fork([Flow.Log("a")], [Flow.Log("b")], Flow.End());
        Assert.DoesNotContain(WorkflowCheck.Problems(good.Workflow), p => p.Text.Contains("branch") || p.Text.Contains("join"));

        var single = new Flow("One").Then(new WorkflowStep { Type = StepType.Parallel, Cases = ["only"] });
        var lone = single.Add(Flow.Log("a"));
        single.Link(single.Last, lone, StepOutputs.Case("only"));
        Assert.Contains(WorkflowCheck.Problems(single.Workflow), p => p.StepId == single.Last.Id);

        // Branches that meet at two joins, and a join no branch reaches.
        var (split, _, join) = Fork([Flow.Log("a")], [Flow.Log("b")], Flow.End());
        var other = split.Add(new WorkflowStep { Type = StepType.Join, Name = "Other" });
        split.Workflow.Links.Single(l => l.To == join.Id && l.From == split.Workflow.Steps.Single(s => s.Message == "b").Id).To = other.Id;
        var problems = WorkflowCheck.Problems(split.Workflow);
        Assert.Contains(problems, p => p.StepId == split.Workflow.Steps.Single(s => s.Type == StepType.Parallel).Id);

        var stray = new Flow("Stray").Then(new WorkflowStep { Type = StepType.Join }).Then(Flow.End());
        Assert.Contains(WorkflowCheck.Problems(stray.Workflow), p => p.StepId == stray.Workflow.Steps.Single(s => s.Type == StepType.Join).Id);
    }
}
