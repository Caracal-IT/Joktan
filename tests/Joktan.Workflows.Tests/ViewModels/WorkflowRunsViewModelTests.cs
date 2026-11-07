using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Controls;
using Joktan.Workflows.Model;
using Joktan.Workflows.Tests.Engine;
using Joktan.Workflows.ViewModels;
using Xunit;

namespace Joktan.Workflows.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class WorkflowRunsViewModelTests : EngineTest
{
    [Fact]
    public Task Runs_are_listed_newest_first_and_filtered() => Headless.Run(async () =>
    {
        var vm = new WorkflowRunsViewModel(Engine);
        Assert.True(vm.HasNoRuns);
        var done = await RunAsync(new Flow("Done").Then(Flow.End()));

        // The list is refreshed on the UI thread: let it show the first run before the next starts.
        await Headless.Until(() => vm.Runs.Count == 1);
        Assert.Equal(done.Id, vm.SelectedRun?.Run.Id);
        var failed = await RunAsync(new Flow("Broken").Then(new WorkflowStep { Type = StepType.Fail, Message = "no" }));
        Headless.Settle();

        Assert.Equal([failed.Id, done.Id], vm.Runs.Select(r => r.Run.Id));

        // The run first shown stays selected as newer ones arrive.
        Assert.Equal(done.Id, vm.SelectedRun!.Run.Id);

        vm.Filter = RunFilter.Failed;
        Assert.Equal([failed.Id], vm.Runs.Select(r => r.Run.Id));
        vm.Filter = RunFilter.UnderWay;
        Assert.True(vm.HasNoRuns);

        // Showing a run widens the filter to find it.
        vm.Select(done.Id);
        Assert.Equal(RunFilter.All, vm.Filter);
        Assert.Equal(done.Id, vm.SelectedRun!.Run.Id);
    });

    [Fact]
    public Task A_run_is_drawn_with_the_path_it_took() => Headless.Run(async () =>
    {
        var vm = new WorkflowRunsViewModel(Engine);
        var flow = new Flow();
        var check = flow.Add(Flow.If("{{input.ok}}", CompareOperator.Equals, "true"));
        var yes = flow.Add(Flow.End());
        var no = flow.Add(new WorkflowStep { Type = StepType.Fail, Message = "not ok" });
        flow.Link(flow.Last, check).Link(check, yes, StepOutputs.True).Link(check, no, StepOutputs.False);

        var run = await RunAsync(flow, """{"ok": false}""");
        Headless.Settle();
        vm.Select(run.Id);

        Assert.Equal(StepMark.Done, vm.Diagram.Marks[check.Id].Mark);
        Assert.Equal(StepMark.Failed, vm.Diagram.Marks[no.Id].Mark);
        Assert.False(vm.Diagram.Marks.ContainsKey(yes.Id));
        Assert.Contains($"{check.Id}|false|{no.Id}", vm.Diagram.Taken);
        Assert.Equal("not ok", vm.SelectedRun!.Error);
        Assert.True(vm.SelectedRun.IsFailed);
        Assert.Equal(3, vm.SelectedRun.Steps.Count);
    });

    [Fact]
    public Task A_run_can_be_paused_resumed_and_killed_from_the_page() => Headless.Run(async () =>
    {
        var vm = new WorkflowRunsViewModel(Engine);
        var flow = new Flow().Then(Flow.Delay("1h"));
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Headless.Until(() => run.Status == RunStatus.Waiting);
        vm.Select(run.Id);
        var item = vm.SelectedRun!;
        Assert.True(item.CanPause);

        vm.PauseCommand.Execute(null);
        Assert.True(item.CanResume);
        vm.ResumeCommand.Execute(null);
        await Headless.Until(() => run.Status == RunStatus.Waiting);
        vm.KillCommand.Execute(null);
        await Headless.Until(() => item.CanRemove);

        Assert.Equal("Killed", item.StatusText);
        vm.RemoveCommand.Execute(null);
        Headless.Settle();
        Assert.True(vm.HasNoRuns);
    });

    [Fact]
    public Task A_question_is_answered_on_the_page() => Headless.Run(async () =>
    {
        var vm = new WorkflowRunsViewModel(Engine);
        var flow = new Flow().Then(new WorkflowStep
        {
            Type = StepType.UserInput, Message = "Ship order {{input.id}}?", ResultVariable = "answer",
            Fields = [new InputField { Name = "ship", Label = "Ship it", Default = "yes" }],
        }).Then(Flow.End("{{vars.answer.ship}}"));
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, JsonNode.Parse("""{"id": 9}"""), RunTrigger.Manual);
        await Headless.Until(() => vm.SelectedRun?.IsWaitingForInput == true);

        var item = vm.SelectedRun!;
        Assert.Equal("Ship order 9?", item.Question);
        Assert.Equal("Ship it", item.Answers.Single().Label);
        item.Answers[0].Value = "later";
        vm.SubmitAnswerCommand.Execute(null);

        var finished = await Engine.WhenFinished(run.Id);
        Assert.Equal("\"later\"", Joktan.Workflows.Engine.JsonText.Of(finished.Output));
    });

    [Fact]
    public Task A_run_is_started_by_hand_and_run_again() => Headless.Run(async () =>
    {
        var vm = new WorkflowRunsViewModel(Engine);
        var flow = new Flow("Echo").Then(Flow.End("{{input.word}}"));
        flow.Workflow.Samples = [new NamedInput { Name = "Word", Input = """{"word": "sample"}""" }, new NamedInput { Name = "Other", Input = """{"word": "other"}""" }];
        Library.Save(flow.Workflow);

        vm.ToggleStartCommand.Execute(null);
        Assert.Equal("Echo", vm.StartWorkflow!.Name);
        Assert.Equal(["Word", "Other", "Type it in…"], vm.StartSampleChoices);
        Assert.Equal("""{"word": "sample"}""", vm.StartInput);
        vm.StartSample = "Other";
        Assert.Equal("""{"word": "other"}""", vm.StartInput);
        vm.StartInput = "{ broken";
        vm.StartCommand.Execute(null);
        Assert.Contains("not JSON", vm.Error);

        vm.StartInput = """{"word": "hi"}""";
        vm.StartCommand.Execute(null);
        Assert.False(vm.IsStarting);
        var first = vm.SelectedRun!.Run;
        await Engine.WhenFinished(first.Id);
        Headless.Settle();

        vm.RunAgainCommand.Execute(null);
        var second = vm.SelectedRun!.Run;
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("\"hi\"", Joktan.Workflows.Engine.JsonText.Of((await Engine.WhenFinished(second.Id)).Output));

        Headless.Settle();
        vm.ClearFinishedCommand.Execute(null);
        Headless.Settle();
        Assert.True(vm.HasNoRuns);
    });
}
