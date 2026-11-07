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

/// <summary>An engine over a library, store and settings of the test's own, with fake MQTT clients and requests.</summary>
public abstract class EngineTest : DataFolderTest
{
    protected EngineTest()
    {
        Library = new WorkflowLibrary(Data.At("definitions"));
        Store = new RunStore(Data.At("runs"));
        Engine = new WorkflowEngine(Library, Settings, Services, Store);
    }

    protected WorkflowSettings Settings { get; } = new() { ShortTimeLimitSeconds = 20 };

    protected FakeWorkflowServices Services { get; } = new();

    protected WorkflowLibrary Library { get; }

    protected RunStore Store { get; }

    protected WorkflowEngine Engine { get; set; }

    protected static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Saves the workflow, runs it and waits for it to finish.</summary>
    protected async Task<WorkflowRun> RunAsync(Flow flow, string input = "{}")
    {
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, JsonNode.Parse(input), RunTrigger.Manual);
        return await Engine.WhenFinished(run.Id).WaitAsync(Patience);
    }

    /// <summary>Waits until a run reaches a status.</summary>
    protected async Task Until(WorkflowRun run, RunStatus status)
    {
        var deadline = DateTime.Now + Patience;
        while (run.Status != status)
        {
            Assert.True(DateTime.Now < deadline, $"The run stayed {run.Status}, not {status}.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    protected static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.Now + Patience;
        while (!condition())
        {
            Assert.True(DateTime.Now < deadline, what);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    protected override void Dispose(bool disposing)
    {
        Engine.Dispose();
        base.Dispose(disposing);
    }
}

[Collection(ProcessStateCollection.Name)]
public class WorkflowEngineTests : EngineTest
{
    [Fact]
    public async Task Steps_run_in_order_and_the_end_gives_the_output()
    {
        var flow = new Flow()
            .Then(Flow.Set("greeting", "\"Hello, {{input.name}}\""))
            .Then(Flow.End("{\"text\": \"{{vars.greeting}}\", \"count\": {{input.count}}}"));

        var run = await RunAsync(flow, """{"name": "Ada", "count": 3}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("""{"text":"Hello, Ada","count":3}""", JsonText.Of(run.Output));
        Assert.Equal(["Start", "SetVariable 2", "End 3"], run.Steps.Select(s => s.Name));
        Assert.All(run.Steps, s => Assert.Equal(StepStatus.Done, s.Status));
    }

    [Fact]
    public async Task A_condition_follows_the_branch_it_chose()
    {
        var flow = new Flow();
        var check = flow.Add(Flow.If("{{input.temperature}}", CompareOperator.GreaterThan, "30"));
        var hot = flow.Add(Flow.End("\"hot\""));
        var cold = flow.Add(Flow.End("\"cold\""));
        flow.Link(flow.Last, check).Link(check, hot, StepOutputs.True).Link(check, cold, StepOutputs.False);

        var warm = await RunAsync(flow, """{"temperature": 31.5}""");
        var cool = await RunAsync(flow, """{"temperature": "12"}""");

        Assert.Equal("\"hot\"", JsonText.Of(warm.Output));
        Assert.Equal(StepOutputs.True, warm.Steps.Single(s => s.StepId == check.Id).Output);
        Assert.Equal("\"cold\"", JsonText.Of(cool.Output));
        Assert.DoesNotContain(cool.Steps, s => s.StepId == hot.Id);
    }

    [Fact]
    public async Task A_failed_step_goes_out_by_its_error_output_when_it_has_one()
    {
        var flow = new Flow();
        var publish = flow.Add(new WorkflowStep { Type = StepType.MqttPublish, Client = "Local", Topic = "a/b", Body = "x" });
        var handled = flow.Add(Flow.End("\"handled\""));
        flow.Link(flow.Last, publish).Link(publish, handled, StepOutputs.Error);
        Services.PublishError = "not connected";

        var run = await RunAsync(flow);

        Assert.Equal(RunStatus.Completed, run.Status);
        var failed = run.Steps.Single(s => s.StepId == publish.Id);
        Assert.Equal(StepStatus.Failed, failed.Status);
        Assert.Contains("not connected", failed.Error);
        Assert.Equal(StepOutputs.Error, failed.Output);
    }

    [Fact]
    public async Task A_failed_step_with_nowhere_to_go_fails_the_run()
    {
        var flow = new Flow().Then(new WorkflowStep { Type = StepType.MqttPublish, Name = "Send", Client = "Local", Topic = "a/b" });
        Services.PublishError = "not connected";

        var run = await RunAsync(flow);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.StartsWith("Send:", run.Error);
        Assert.Contains(run.Log, l => l.IsError);
    }

    [Fact]
    public async Task A_fail_step_fails_the_run_with_its_reason()
    {
        var run = await RunAsync(new Flow().Then(new WorkflowStep { Type = StepType.Fail, Message = "No stock for {{input.item}}" }), """{"item": "bolts"}""");

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("No stock for bolts", run.Error);
        Assert.Equal((StepStatus.Failed, (string?)null), (run.Steps[^1].Status, run.Steps[^1].Output));
    }

    [Fact]
    public async Task A_run_with_no_end_step_gives_the_last_result()
    {
        var run = await RunAsync(new Flow().Then(Flow.CSharp("return 6 * 7;")));

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("42", JsonText.Of(run.Output));
    }

    [Fact]
    public async Task A_workflow_without_a_start_cannot_run()
    {
        var workflow = new Workflow { Id = "x", Name = "Empty" };

        var e = Assert.Throws<WorkflowException>(() => Engine.Start(workflow, null, RunTrigger.Manual));

        Assert.Contains("start", e.Message);
    }

    [Fact]
    public async Task A_run_can_be_paused_and_resumed()
    {
        var flow = new Flow().Then(Flow.Delay("400ms")).Then(Flow.Set("done", "true")).Then(Flow.End("{{vars.done}}"));
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(run, RunStatus.Waiting);

        Assert.True(Engine.Pause(run.Id));
        Assert.Equal(RunStatus.Paused, run.Status);
        await Task.Delay(800, TestContext.Current.CancellationToken);

        // The clock stood still while it was paused.
        Assert.Equal(RunStatus.Paused, run.Status);
        Assert.Equal(2, run.Steps.Count);

        Assert.True(Engine.Resume(run.Id));
        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Completed, finished.Status);
        Assert.Equal("true", JsonText.Of(finished.Output));
    }

    [Fact]
    public async Task A_killed_run_stops_at_once()
    {
        var flow = new Flow().Then(Flow.Delay("1h")).Then(Flow.End());
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(run, RunStatus.Waiting);

        Assert.True(Engine.Kill(run.Id));
        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.Killed, finished.Status);
        Assert.Equal(StepStatus.Failed, finished.Steps.Last().Status);
        Assert.False(Engine.Kill(run.Id));
    }

    [Fact]
    public async Task A_short_workflow_is_stopped_at_the_time_limit()
    {
        Settings.ShortTimeLimitSeconds = 1;
        var run = await RunAsync(new Flow().Then(Flow.Delay("1m")).Then(Flow.End()));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("longer than", run.Error);
    }

    [Fact]
    public async Task A_user_input_step_waits_for_the_answer()
    {
        var ask = new WorkflowStep
        {
            Type = StepType.UserInput, Message = "Approve {{input.order}}?", ResultVariable = "answer",
            Fields = [new InputField { Name = "approved", Default = "false" }, new InputField { Name = "note", Label = "Note" }],
        };
        var flow = new Flow().Then(ask).Then(Flow.End("{{vars.answer}}"));
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, JsonNode.Parse("""{"order": 17}"""), RunTrigger.Manual);
        await Until(run, RunStatus.WaitingForInput);

        Assert.Equal("Approve 17?", run.PendingInput!.Message);
        Assert.Equal("approved", run.PendingInput.Fields[0].Label);
        Assert.Throws<WorkflowException>(() => Engine.Answer(run.Id, new Dictionary<string, string> { ["colour"] = "red" }));
        Engine.Answer(run.Id, new Dictionary<string, string> { ["approved"] = "true" });

        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal("""{"approved":true,"note":""}""", JsonText.Of(finished.Output));
        Assert.Null(finished.PendingInput);
    }

    [Fact]
    public async Task A_user_input_steps_question_carries_its_metadata_and_always_the_run_id()
    {
        var ask = new WorkflowStep
        {
            Type = StepType.UserInput, Message = "Approve?", AssignedTo = new() { Kind = AssigneeKind.Role, Id = "Approver", Name = "Approver" },
            Metadata = [new() { Name = "customer", Value = "{{vars.customer}}" }, new() { Name = "priority", Value = "3" }, new() { Name = "region", Value = "EU" },
                new() { Name = "lines", Value = "{{vars.lines}}" }, new() { Name = "runId", Value = "forged" }, new() { Name = " ", Value = "" },
                new() { Name = "assignedTo", Value = "forged" }],
        };
        var flow = new Flow().Then(ask).Then(Flow.End());
        flow.Workflow.Globals = [new() { Name = "customer", Value = "{{input.customer}}" }, new() { Name = "lines", Value = "[1, 2]" }];
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, JsonNode.Parse("""{"customer": "Acme"}"""), RunTrigger.Manual);
        await Until(run, RunStatus.WaitingForInput);

        var metadata = run.PendingInput!.Metadata;
        Assert.Equal(["runId", "serial", "assignedTo", "customer", "priority", "region", "lines"], metadata.Select(m => m.Key));
        Assert.Equal(run.Id, (string?)metadata["runId"]);
        Assert.Equal("""{"kind":"role","id":"Approver","name":"Approver"}""", JsonText.Of(metadata["assignedTo"]));
        Assert.Equal(run.Serial, (long?)metadata["serial"]);
        Assert.Equal("\"Acme\"", JsonText.Of(metadata["customer"]));
        Assert.Equal("3", JsonText.Of(metadata["priority"]));
        Assert.Equal("\"EU\"", JsonText.Of(metadata["region"]));
        Assert.Equal("[1,2]", JsonText.Of(metadata["lines"]));

        Engine.Answer(run.Id, new Dictionary<string, string>());
        Assert.Equal(RunStatus.Completed, (await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task A_user_input_step_with_a_time_limit_goes_out_by_timeout()
    {
        var flow = new Flow();
        var ask = flow.Add(new WorkflowStep { Type = StepType.UserInput, Message = "Anyone?", Duration = "200ms" });
        var late = flow.Add(Flow.End("\"no answer\""));
        flow.Link(flow.Last, ask).Link(ask, late, StepOutputs.Timeout);

        var run = await RunAsync(flow);

        Assert.Equal("\"no answer\"", JsonText.Of(run.Output));
    }

    [Fact]
    public async Task A_called_workflow_gives_its_output_to_the_caller_that_waits()
    {
        var child = new Flow("Double").Then(Flow.CSharp("return (int)Input![\"n\"]! * 2;")).Then(Flow.End());
        Library.Save(child.Workflow);
        var parent = new Flow("Caller")
            .Then(new WorkflowStep { Type = StepType.CallWorkflow, Workflow = child.Workflow.Id, Input = """{"n": {{input.n}}}""", ResultVariable = "doubled" })
            .Then(Flow.End("{{vars.doubled}}"));

        var run = await RunAsync(parent, """{"n": 21}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("42", JsonText.Of(run.Output));
        var childRun = Assert.Single(Engine.Runs, r => r.WorkflowId == child.Workflow.Id);
        Assert.Equal(run.Id, childRun.ParentRunId);
        Assert.Equal(RunTrigger.Workflow, childRun.Trigger);
    }

    [Fact]
    public async Task A_called_workflow_that_is_not_waited_on_runs_beside_the_caller()
    {
        var child = new Flow("Slow").Then(Flow.Delay("300ms")).Then(Flow.End("\"slow done\""));
        Library.Save(child.Workflow);
        var parent = new Flow("Caller").Then(new WorkflowStep { Type = StepType.CallWorkflow, Workflow = child.Workflow.Id, Wait = false, ResultVariable = "started" });

        var run = await RunAsync(parent);

        Assert.Equal(RunStatus.Completed, run.Status);
        var childId = (string)run.Output!["runId"]!;
        Assert.False(Engine.Find(childId)!.Status.IsFinished());
        var childRun = await Engine.WhenFinished(childId).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal("\"slow done\"", JsonText.Of(childRun.Output));
    }

    [Fact]
    public async Task Killing_a_caller_kills_the_workflow_it_waits_on()
    {
        var child = new Flow("Forever").Then(Flow.Delay("1h"));
        Library.Save(child.Workflow);
        var parent = new Flow("Caller").Then(new WorkflowStep { Type = StepType.CallWorkflow, Workflow = child.Workflow.Id });
        Library.Save(parent.Workflow);
        var run = Engine.Start(parent.Workflow, null, RunTrigger.Manual);
        await Until(() => Engine.Runs.Any(r => r.WorkflowId == child.Workflow.Id && r.Status == RunStatus.Waiting), "The child never started waiting.");

        Engine.Kill(run.Id);
        await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);

        var childRun = Engine.Runs.Single(r => r.WorkflowId == child.Workflow.Id);
        await Engine.WhenFinished(childRun.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Killed, childRun.Status);
    }

    [Fact]
    public async Task Runs_beyond_the_most_allowed_wait_in_the_queue()
    {
        Settings.MaxRunning = 1;
        var flow = new Flow().Then(Flow.CSharp("await System.Threading.Tasks.Task.Delay(200); return 1;"));
        Library.Save(flow.Workflow);

        var first = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        var second = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(first, RunStatus.Running);

        Assert.Equal(RunStatus.Queued, second.Status);
        await Engine.WhenFinished(second.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Completed, first.Status);
        Assert.True(second.Steps[0].Started >= first.Ended);
    }

    [Fact]
    public async Task A_long_run_carries_on_after_a_restart_from_the_step_it_was_in()
    {
        var flow = new Flow("Long", WorkflowKind.Long).Then(Flow.Set("seen", "1")).Then(Flow.Delay("500ms")).Then(Flow.End("{{vars.seen}}"));
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(run, RunStatus.Waiting);

        // Joktan closes in the middle of the delay, and starts again.
        Engine.Dispose();
        Engine = new WorkflowEngine(Library, Settings, Services, Store);
        Engine.Restore();

        var carried = Engine.Find(run.Id)!;
        var finished = await Engine.WhenFinished(carried.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Completed, finished.Status);
        Assert.Equal("1", JsonText.Of(finished.Output));
        Assert.Contains(finished.Log, l => l.Text.Contains("started again"));
    }

    [Fact]
    public async Task A_long_run_waits_paused_after_a_restart_when_the_settings_say_so()
    {
        Settings.ResumeLongRuns = false;
        var flow = new Flow("Long", WorkflowKind.Long).Then(Flow.Delay("1h"));
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(run, RunStatus.Waiting);

        Engine.Dispose();
        Engine = new WorkflowEngine(Library, Settings, Services, Store);
        Engine.Restore();

        var carried = Engine.Find(run.Id)!;
        Assert.Equal(RunStatus.Paused, carried.Status);
        Assert.True(Engine.Resume(carried.Id));
        await Until(carried, RunStatus.Waiting);
        Assert.True(Engine.Kill(carried.Id));
    }

    [Fact]
    public async Task A_short_run_cut_off_by_a_restart_is_marked_failed()
    {
        var flow = new Flow().Then(Flow.Delay("1h"));
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(run, RunStatus.Waiting);
        Store.Save(run);

        Engine.Dispose();
        Engine = new WorkflowEngine(Library, Settings, Services, Store);
        Engine.Restore();

        Assert.Equal(RunStatus.Failed, Engine.Find(run.Id)!.Status);
    }

    [Fact]
    public async Task Only_as_many_finished_runs_as_the_settings_keep_are_kept()
    {
        Settings.KeepRuns = 2;
        var flow = new Flow().Then(Flow.End());
        for (var i = 0; i < 4; i++)
            await RunAsync(flow);

        Assert.Equal(2, Engine.Runs.Count);
        Assert.Equal(2, Store.LoadAll().Count);
    }

    [Fact]
    public async Task Finished_runs_can_be_cleared()
    {
        var flow = new Flow().Then(Flow.End());
        await RunAsync(flow);
        await RunAsync(flow);

        Assert.Equal(2, Engine.ClearFinished());

        Assert.Empty(Engine.Runs);
        Assert.Empty(Store.LoadAll());
    }

    [Fact]
    public async Task Editing_a_workflow_does_not_change_a_run_under_way()
    {
        var flow = new Flow().Then(Flow.Delay("300ms")).Then(Flow.End("\"before\""));
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);

        flow.Workflow.Steps.Last().Value = "\"after\"";
        Library.Save(flow.Workflow);

        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal("\"before\"", JsonText.Of(finished.Output));
    }

    [Fact]
    public async Task An_mqtt_message_starts_the_workflows_that_listen_for_it()
    {
        var flow = new Flow("Listener").Then(Flow.End("{\"level\": {{input.level}}, \"topic\": \"{{run.topic}}\"}"));
        flow.Workflow.Triggers = new WorkflowTriggers { Mqtt = true, MqttClient = "Local", MqttTopic = "tanks/+/level" };
        Library.Save(flow.Workflow);
        Engine.Restore();
        await Until(() => Services.Listening == 1, "The trigger never listened.");

        Services.Deliver("tanks/7/level", """{"level": 81}""");
        Services.Deliver("tanks/7/temperature", """{"level": 0}""");

        await Until(() => Engine.Runs.Count == 1 && Engine.Runs[0].Status.IsFinished(), "The message did not start one run.");
        var run = Engine.Runs.Single();
        Assert.Equal(RunTrigger.Mqtt, run.Trigger);
        Assert.Equal("tanks/7/level", run.TriggerDetail);
        Assert.Equal("""{"level":81,"topic":"tanks/7/level"}""", JsonText.Of(run.Output));
    }

    [Fact]
    public async Task An_mqtt_trigger_on_a_client_that_does_not_exist_is_reported()
    {
        var flow = new Flow("Listener").Then(Flow.End());
        flow.Workflow.Triggers = new WorkflowTriggers { Mqtt = true, MqttClient = "Nowhere", MqttTopic = "a/#" };
        Library.Save(flow.Workflow);

        Engine.RefreshTriggers();
        await Until(() => Engine.TriggerProblems.Count > 0, "No problem was reported.");

        Assert.Contains("Nowhere", Engine.TriggerProblems[flow.Workflow.Id]);
    }

    [Fact]
    public async Task Mqtt_triggers_stop_when_the_settings_turn_them_off()
    {
        var flow = new Flow("Listener").Then(Flow.End());
        flow.Workflow.Triggers = new WorkflowTriggers { Mqtt = true, MqttClient = "Local", MqttTopic = "a/#" };
        Library.Save(flow.Workflow);
        await Until(() => Services.Listening == 1, "The trigger never listened.");

        Settings.MqttTriggers = false;
        Engine.RefreshTriggers();

        Assert.Equal(0, Services.Listening);
    }
    [Fact]
    public async Task A_failing_step_is_tried_again_as_often_as_it_asks()
    {
        var flow = new Flow();
        var send = flow.Add(new WorkflowStep { Type = StepType.MqttPublish, Client = "Local", Topic = "a/b", Retries = 2, RetryDelay = "50ms" });
        flow.Link(flow.Last, send);
        Services.PublishError = "not connected";

        var run = await RunAsync(flow);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(3, run.Steps.Count(s => s.StepId == send.Id));
        Assert.Contains(run.Log, l => l.Text.Contains("retry 2 of 2"));
    }

    [Fact]
    public async Task A_retry_that_works_carries_on_as_if_nothing_failed()
    {
        var flow = new Flow();
        var send = flow.Add(new WorkflowStep { Type = StepType.MqttPublish, Client = "Local", Topic = "a/b", Body = "hi", Retries = 3, RetryDelay = "300ms" });
        var end = flow.Add(Flow.End("\"sent\""));
        flow.Link(flow.Last, send).Link(send, end);
        Services.PublishError = "not connected";
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(run, RunStatus.Waiting);

        Services.PublishError = null;

        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal("\"sent\"", JsonText.Of(finished.Output));
        Assert.Equal(0, finished.Attempt);
        Assert.Single(Services.Published);
    }

    [Fact]
    public async Task A_loop_with_no_way_out_fails_at_the_step_limit()
    {
        Settings.MaxSteps = 50;
        var flow = new Flow();
        var spin = flow.Add(Flow.Set("n", "1"));
        flow.Link(flow.Last, spin).Link(spin, spin);

        var run = await RunAsync(flow);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("50 steps", run.Error);
    }

    [Fact]
    public async Task A_run_that_loops_keeps_only_its_latest_steps()
    {
        Settings.MaxSteps = WorkflowRun.KeptSteps + 100;
        var flow = new Flow();
        var spin = flow.Add(Flow.Set("n", "1"));
        flow.Link(flow.Last, spin).Link(spin, spin);

        var run = await RunAsync(flow);

        Assert.Equal(WorkflowRun.KeptSteps, run.Steps.Count);
        Assert.Equal(WorkflowRun.KeptSteps + 101, run.StepCount);
    }

    [Fact]
    public async Task A_paused_short_run_does_not_use_up_its_time_limit()
    {
        Settings.ShortTimeLimitSeconds = 1;
        var flow = new Flow().Then(Flow.Delay("300ms")).Then(Flow.End("\"done\""));
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(run, RunStatus.Waiting);

        Engine.Pause(run.Id);
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Paused, run.Status);
        Engine.Resume(run.Id);

        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Completed, finished.Status);
    }

    [Fact]
    public async Task Time_in_the_queue_does_not_count_against_a_short_run()
    {
        Settings.MaxRunning = 1;
        Settings.ShortTimeLimitSeconds = 1;
        var slow = new Flow("Slow").Then(Flow.CSharp("await System.Threading.Tasks.Task.Delay(700); return 1;"));
        Library.Save(slow.Workflow);

        var first = Engine.Start(slow.Workflow, null, RunTrigger.Manual);
        var second = Engine.Start(slow.Workflow, null, RunTrigger.Manual);

        Assert.Equal(RunStatus.Completed, (await Engine.WhenFinished(first.Id).WaitAsync(Patience, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(RunStatus.Completed, (await Engine.WhenFinished(second.Id).WaitAsync(Patience, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task After_a_restart_a_long_run_waits_on_the_workflow_it_had_already_called()
    {
        var child = new Flow("Child", WorkflowKind.Long).Then(Flow.Delay("600ms")).Then(Flow.End("\"child done\""));
        Library.Save(child.Workflow);
        var parent = new Flow("Parent", WorkflowKind.Long).Then(new WorkflowStep { Type = StepType.CallWorkflow, Workflow = child.Workflow.Id, ResultVariable = "r" }).Then(Flow.End("{{vars.r}}"));
        Library.Save(parent.Workflow);
        var run = Engine.Start(parent.Workflow, null, RunTrigger.Manual);
        await Until(() => Engine.Runs.Any(r => r.WorkflowId == child.Workflow.Id && r.Status == RunStatus.Waiting), "The child never started waiting.");

        Engine.Dispose();
        Engine = new WorkflowEngine(Library, Settings, Services, Store);
        Engine.Restore();

        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal("\"child done\"", JsonText.Of(finished.Output));
        Assert.Single(Engine.Runs, r => r.WorkflowId == child.Workflow.Id);
        Assert.Contains(finished.Steps, s => s.Error?.Contains("Joktan closed") == true);
    }

    [Fact]
    public async Task After_a_restart_a_long_run_starts_again_a_short_workflow_that_was_cut_off()
    {
        var child = new Flow("Child").Then(Flow.Delay("400ms")).Then(Flow.End("\"child done\""));
        Library.Save(child.Workflow);
        var parent = new Flow("Parent", WorkflowKind.Long).Then(new WorkflowStep { Type = StepType.CallWorkflow, Workflow = child.Workflow.Id }).Then(Flow.End());
        Library.Save(parent.Workflow);
        var run = Engine.Start(parent.Workflow, null, RunTrigger.Manual);
        await Until(() => Engine.Runs.Any(r => r.WorkflowId == child.Workflow.Id && r.Status == RunStatus.Waiting), "The child never started waiting.");

        Engine.Dispose();
        Engine = new WorkflowEngine(Library, Settings, Services, Store);
        Engine.Restore();

        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Completed, finished.Status);

        // Short runs are not written down as they go, so the one cut off is gone and a new one was started.
        var again = Assert.Single(Engine.Runs, r => r.WorkflowId == child.Workflow.Id);
        Assert.Equal(RunStatus.Completed, again.Status);
        Assert.Equal(2, finished.Log.Count(l => l.Text.StartsWith("Started Child", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_question_keeps_its_deadline_across_a_restart()
    {
        var flow = new Flow("Ask", WorkflowKind.Long);
        var ask = flow.Add(new WorkflowStep { Type = StepType.UserInput, Message = "Now?", Duration = "1s" });
        var late = flow.Add(Flow.End("\"late\""));
        flow.Link(flow.Last, ask).Link(ask, late, StepOutputs.Timeout);
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(run, RunStatus.WaitingForInput);
        var deadline = run.Deadline;

        Engine.Dispose();
        Engine = new WorkflowEngine(Library, Settings, Services, Store);
        Engine.Restore();
        var carried = Engine.Find(run.Id)!;
        await Until(carried, RunStatus.WaitingForInput);

        Assert.Equal(deadline, carried.PendingInput!.Until);
        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal("\"late\"", JsonText.Of(finished.Output));
    }

    [Fact]
    public async Task Workflows_that_call_each_other_without_end_are_stopped()
    {
        var flow = new Flow("Again");
        flow.Then(new WorkflowStep { Type = StepType.CallWorkflow, Workflow = flow.Workflow.Id, Wait = false });

        await RunAsync(flow);
        await Until(() => Engine.Runs.All(r => r.Status.IsFinished()) && Engine.Runs.Count > WorkflowSettings.MaxCallDepth, "The calls did not stop.");

        Assert.Contains(Engine.Runs, r => r.Status == RunStatus.Failed && r.Error!.Contains("deep"));
        Assert.True(Engine.Runs.Count <= WorkflowSettings.MaxCallDepth + 1);
    }

    [Fact]
    public async Task Every_run_has_a_serial_number_one_more_than_the_last_never_used_twice()
    {
        var flow = new Flow().Then(Flow.End("{{run.serial}}"));
        var first = await RunAsync(flow);
        var second = await RunAsync(flow);

        Assert.Equal(1, first.Serial);
        Assert.Equal(2, second.Serial);
        Assert.Equal("2", JsonText.Of(second.Output));
        Assert.Equal("#2", second.SerialText);
        Assert.Same(second, Engine.Find("#2"));
        Assert.Same(second, Engine.Find("2"));

        // Cleared away, and after a restart, the numbers still go on from where they were.
        Engine.ClearFinished();
        Engine.Dispose();
        Engine = new WorkflowEngine(Library, Settings, Services, Store);
        Engine.Restore();
        Assert.Equal(3, (await RunAsync(flow)).Serial);
    }

    [Fact]
    public async Task A_lost_counter_starts_again_above_the_highest_run_kept()
    {
        var flow = new Flow().Then(Flow.End());
        await RunAsync(flow);
        await RunAsync(flow);
        System.IO.File.Delete(System.IO.Path.Combine(Store.Folder, "serial.txt"));

        Engine.Dispose();
        Engine = new WorkflowEngine(Library, Settings, Services, Store);
        Engine.Restore();

        Assert.Equal(3, (await RunAsync(flow)).Serial);
    }
}
