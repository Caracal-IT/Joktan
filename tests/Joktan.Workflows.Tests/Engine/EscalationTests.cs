using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Engine;

/// <summary>A user input step's question, assigned to a role and escalated to another when nobody answers in time.</summary>
[Collection(ProcessStateCollection.Name)]
public class EscalationTests : EngineTest
{
    private DateTime _clock = new(2030, 1, 7, 8, 0, 0);

    public EscalationTests()
    {
        // The engine's clock is the test's, which moves only when the test moves it.
        Engine.Dispose();
        Engine = new WorkflowEngine(Library, Settings, Services, Store, now: () => _clock);
    }

    private static readonly Assignee Approver = new() { Kind = AssigneeKind.Role, Id = "Approver", Name = "Approver" };

    private static readonly Assignee Manager = new() { Kind = AssigneeKind.Role, Id = "Manager", Name = "Manager" };

    // A long workflow, so that the test's clock moving on does not run out a short run's time limit.
    private WorkflowRun Start(WorkflowStep ask)
    {
        var flow = new Flow("Approve").Then(ask).Then(Flow.End("{{vars.answer}}"));
        flow.Workflow.Kind = WorkflowKind.Long;
        Library.Save(flow.Workflow);
        return Engine.Start(flow.Workflow, JsonNode.Parse("{}"), RunTrigger.Manual);
    }

    private static WorkflowStep Ask(string escalateAfter = "10m", string limit = "") => new()
    {
        Type = StepType.UserInput, Message = "Approve?", Fields = [new InputField { Name = "ok", Default = "true" }], Variable = "answer",
        AssignedTo = Approver.Clone(), Escalate = true, EscalateAfter = escalateAfter, EscalateTo = Manager.Clone(), Duration = limit,
    };

    [Fact]
    public async Task A_question_not_answered_in_time_goes_to_whom_it_is_escalated_to_and_carries_on_waiting()
    {
        var escalated = new List<(WorkflowRun Run, PendingInput Question)>();
        Engine.Escalated += (run, question) => escalated.Add((run, question));
        var run = Start(Ask(limit: "1h"));
        await Until(run, RunStatus.WaitingForInput);

        var question = run.PendingInput!;
        Assert.Equal("Approver", question.AssignedTo.Id);
        Assert.Equal(_clock.AddMinutes(10), question.EscalateAt);
        Assert.False(question.Escalated);

        _clock = _clock.AddMinutes(9);
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        Assert.False(question.Escalated);

        _clock = _clock.AddMinutes(2);
        // The event is raised last, after the question is reassigned and the log written, on the run's thread.
        await Until(() => escalated.Count > 0, "The question was not escalated.");
        Assert.Equal("Manager", question.AssignedTo.Id);
        Assert.Equal("Approver", question.FirstAssignedTo!.Id);
        Assert.Equal("""{"kind":"role","id":"Manager","name":"Manager"}""", JsonText.Of(question.Metadata["assignedTo"]));
        Assert.Contains(run.Log, l => l.Text.Contains("escalated to the role Manager"));
        Assert.Single(escalated);
        Assert.Equal(RunStatus.WaitingForInput, run.Status);

        Engine.Answer(run.Id, new Dictionary<string, string> { ["ok"] = "false" });
        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Completed, finished.Status);
        Assert.Null(finished.EscalateAt);
    }

    [Fact]
    public async Task A_time_limit_that_runs_out_first_ends_the_wait_without_escalating()
    {
        var run = Start(Ask(escalateAfter: "2h", limit: "1h"));
        await Until(run, RunStatus.WaitingForInput);
        var question = run.PendingInput!;

        _clock = _clock.AddHours(3);
        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.False(question.Escalated);
        Assert.Equal(StepOutputs.Timeout, finished.Steps.First(s => s.Type == StepType.UserInput).Output);
    }

    [Fact]
    public async Task A_long_run_keeps_its_escalation_time_over_a_restart_and_is_escalated_already_after_it()
    {
        var run = Start(Ask());
        await Until(run, RunStatus.WaitingForInput);
        var at = run.EscalateAt;
        Assert.Equal(_clock.AddMinutes(10), at);

        // Joktan closes while the question waits, and starts again after the time to escalate.
        Engine.Dispose();
        _clock = _clock.AddMinutes(30);
        var escalated = 0;
        Engine = new WorkflowEngine(Library, Settings, Services, Store, now: () => _clock);
        Engine.Escalated += (_, _) => escalated++;
        Engine.Restore();

        var carried = Engine.Find(run.Id)!;
        await Until(carried, RunStatus.WaitingForInput);
        Assert.Equal(at, carried.EscalateAt);
        Assert.True(carried.PendingInput!.Escalated);
        Assert.Equal("Manager", carried.PendingInput.AssignedTo.Id);
        Assert.Equal(0, escalated);

        Engine.Answer(carried.Id, new Dictionary<string, string>());
        Assert.Equal(RunStatus.Completed, (await Engine.WhenFinished(carried.Id).WaitAsync(Patience, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task A_step_without_escalation_waits_for_its_assignee_only()
    {
        var ask = Ask();
        ask.Escalate = false;
        var run = Start(ask);
        await Until(run, RunStatus.WaitingForInput);

        Assert.Null(run.PendingInput!.EscalateAt);
        Assert.Null(run.PendingInput.EscalateTo);
        Assert.Contains(run.Log, l => l.Text.Contains("Waiting for an answer from the role Approver"));
        _clock = _clock.AddDays(1);
        await Task.Delay(1200, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.WaitingForInput, run.Status);
        Assert.Equal("Approver", run.PendingInput.AssignedTo.Id);
        Engine.Answer(run.Id, new Dictionary<string, string>());
        await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
    }
}
