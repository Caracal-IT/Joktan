using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Tests.Engine;
using Joktan.Workflows.ViewModels;
using Xunit;

namespace Joktan.Workflows.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class WorkflowDesignerViewModelTests : EngineTest
{
    private WorkflowDesignerViewModel Designer() => new(Engine);

    private static PaletteItem Item(WorkflowDesignerViewModel vm, StepType type) => vm.Palette.SelectMany(g => g.Items).Single(i => i.Type == type);

    [Fact]
    public Task A_user_input_step_creates_a_page_from_its_fields_and_chooses_it() => Headless.Run(() =>
    {
        var vm = Designer();
        vm.NewWorkflowCommand.Execute(null);
        vm.AddFromPaletteCommand.Execute(Item(vm, StepType.UserInput));
        var step = vm.SelectedStep!;
        step.Step.Fields.Clear();
        step.Step.Fields.Add(new InputField { Name = "approved", Label = "Approve it?", Default = "no" });
        step.Step.Fields.Add(new InputField { Name = "reason" });
        Assert.True(step.CanCreatePage);

        step.CreatePageCommand.Execute(null);

        var page = Assert.Single(Engine.Pages.All);
        Assert.Equal($"{vm.Name} - {step.Step.Name}", page.Name);
        Assert.Equal(page.Id, step.Step.PageId);
        Assert.Equal(page.Id, step.PageChoice?.Id);
        Assert.False(step.CanCreatePage);
        Assert.Equal(["approved", "reason"], page.Inputs.Select(i => i.Name));
        Assert.Equal(("Approve it?", "no", PageItemType.TextBox), (page.Items[0].Label, page.Items[0].Default, page.Items[0].Type));

        // A step without fields gets a box to tick and a comment, under a name of its own.
        vm.AddFromPaletteCommand.Execute(Item(vm, StepType.UserInput));
        vm.SelectedStep!.Step.Fields.Clear();
        vm.SelectedStep.Step.Name = step.Step.Name;
        vm.SelectedStep.CreatePageCommand.Execute(null);
        var second = Engine.Pages.Find(vm.SelectedStep.Step.PageId)!;
        Assert.Equal(page.Name + " 2", second.Name);
        Assert.Equal([PageItemType.CheckBox, PageItemType.TextArea], second.Items.Select(i => i.Type));
    });

    [Fact]
    public Task A_new_workflow_starts_with_a_start_linked_to_an_end_and_is_saved() => Headless.Run(() =>
    {
        var vm = Designer();
        Assert.True(vm.HasNoWorkflow);

        vm.NewWorkflowCommand.Execute(null);

        Assert.Equal("New workflow", vm.Name);
        Assert.Equal([StepType.Start, StepType.End], vm.Workflow!.Steps.Select(s => s.Type));
        Assert.Single(vm.Workflow.Links);
        Assert.True(File.Exists(Library.PathOf(vm.Workflow.Id)));
        Assert.Same(vm.Workflow, vm.Diagram.Workflow);

        vm.NewWorkflowCommand.Execute(null);
        Assert.Equal("New workflow 2", vm.Name);
        Assert.Equal(2, vm.Workflows.Count);
    });

    [Fact]
    public Task The_palette_holds_every_step_but_the_start() => Headless.Run(() =>
    {
        var types = Designer().Palette.SelectMany(g => g.Items).Select(i => i.Type).ToList();

        Assert.DoesNotContain(StepType.Start, types);
        Assert.Equal(StepTypes.All.Count - 1, types.Count);
    });

    [Fact]
    public Task A_step_clicked_on_the_palette_follows_the_step_selected() => Headless.Run(() =>
    {
        var vm = Designer();
        vm.NewWorkflowCommand.Execute(null);
        var workflow = vm.Workflow!;
        var start = workflow.Steps[0];
        workflow.Links.Clear();
        vm.Diagram.SelectedStep = start.Id;

        vm.AddFromPaletteCommand.Execute(Item(vm, StepType.Http));

        var http = workflow.Steps.Last();
        Assert.Equal(StepType.Http, http.Type);
        Assert.Contains(workflow.Links, l => l.From == start.Id && l.To == http.Id && l.Output == StepOutputs.Next);
        Assert.Equal("reply", http.ResultVariable);
        Assert.Same(http, vm.SelectedStep!.Step);
        Assert.False(DiagramLayoutOverlaps(workflow));

        // A second one is named and kept apart from the first.
        vm.AddFromPaletteCommand.Execute(Item(vm, StepType.Http));
        Assert.Equal("reply2", workflow.Steps.Last().ResultVariable);
        Assert.Equal("REST request 2", workflow.Steps.Last().Name);
    });

    private static bool DiagramLayoutOverlaps(Workflow workflow)
        => workflow.Steps.Any(a => workflow.Steps.Any(b => a != b && Joktan.Workflows.Controls.DiagramLayout.Box(a).Intersects(Joktan.Workflows.Controls.DiagramLayout.Box(b))));

    [Fact]
    public Task The_canvas_drops_moves_links_and_deletes() => Headless.Run(() =>
    {
        var vm = Designer();
        vm.NewWorkflowCommand.Execute(null);
        var workflow = vm.Workflow!;
        var (start, end) = (workflow.Steps[0], workflow.Steps[1]);

        vm.Diagram.Dropped!(StepType.Log, new Point(400, 300));
        var log = workflow.Steps.Last();
        Assert.Equal((400.0, 300.0), (log.X, log.Y));

        // A new link from an output takes the place of the old one.
        vm.Diagram.Linked!(start.Id, StepOutputs.Next, log.Id);
        Assert.Single(workflow.Links, l => l.From == start.Id);
        Assert.Equal(log.Id, workflow.Links.Single(l => l.From == start.Id).To);
        vm.Diagram.Linked!(log.Id, StepOutputs.Next, end.Id);

        vm.Diagram.SelectedStep = log.Id;
        vm.Diagram.SelectionChanged!();
        Assert.True(vm.HasSelectedStep);
        vm.DeleteSelectionCommand.Execute(null);
        Assert.DoesNotContain(log, workflow.Steps);
        Assert.Empty(workflow.Links);

        // The start stays.
        vm.Diagram.SelectedStep = start.Id;
        vm.DeleteSelectionCommand.Execute(null);
        Assert.Contains(start, workflow.Steps);
    });

    [Fact]
    public Task A_link_can_be_selected_and_deleted() => Headless.Run(() =>
    {
        var vm = Designer();
        vm.NewWorkflowCommand.Execute(null);
        var link = vm.Workflow!.Links[0];

        vm.Diagram.SelectedLink = link;
        vm.Diagram.SelectionChanged!();

        Assert.Equal("From Start by next to End.", vm.SelectedLinkText);
        Assert.False(vm.ShowsWorkflowProperties);
        vm.DeleteSelectionCommand.Execute(null);
        Assert.Empty(vm.Workflow.Links);
        Assert.True(vm.ShowsWorkflowProperties);
    });

    [Fact]
    public Task Changes_are_saved_and_problems_shown() => Headless.Run(() =>
    {
        var vm = Designer();
        vm.NewWorkflowCommand.Execute(null);

        vm.Name = "Orders";
        vm.Kind = WorkflowKind.Long;
        vm.MqttTrigger = true;
        Assert.True(vm.HasProblems);
        vm.MqttClient = "Local";
        vm.MqttTopic = "orders/#";
        vm.Flush();

        Assert.False(vm.HasProblems);
        var saved = new WorkflowLibrary(Library.Folder).Find("Orders")!;
        Assert.Equal(WorkflowKind.Long, saved.Kind);
        Assert.Equal("orders/#", saved.Triggers.MqttTopic);
        Assert.Equal("Long running · MQTT", vm.SelectedWorkflow!.Detail);

        vm.Diagram.Dropped!(StepType.Http, new Point(0, 400));
        vm.Workflow!.Steps.Last().Url = "";
        vm.Diagram.Linked!(vm.Workflow.Steps[1].Id, StepOutputs.Next, vm.Workflow.Steps.Last().Id);
        Assert.Contains(vm.Workflow.Steps.Last().Id, vm.Diagram.Problems.Keys);
    });

    [Fact]
    public Task A_copy_starts_with_its_triggers_off() => Headless.Run(() =>
    {
        var vm = Designer();
        vm.NewWorkflowCommand.Execute(null);
        vm.RestTrigger = true;
        vm.ScheduleTrigger = true;

        vm.DuplicateCommand.Execute(null);

        Assert.Equal("New workflow copy", vm.Name);
        Assert.False(vm.RestTrigger);
        Assert.False(vm.ScheduleTrigger);
        Assert.Equal(2, Library.All.Count);
    });

    [Fact]
    public Task A_workflow_is_deleted_once_confirmed() => Headless.Run(() =>
    {
        var vm = Designer();
        vm.NewWorkflowCommand.Execute(null);

        vm.AskDeleteCommand.Execute(null);
        Assert.True(vm.IsConfirmingDelete);
        vm.CancelDeleteCommand.Execute(null);
        Assert.Single(Library.All);

        vm.AskDeleteCommand.Execute(null);
        vm.ConfirmDeleteCommand.Execute(null);

        Assert.Empty(Library.All);
        Assert.True(vm.HasNoWorkflow);
    });

    [Fact]
    public Task Run_starts_it_with_the_sample_chosen_or_the_input_typed_in() => Headless.Run(async () =>
    {
        var vm = Designer();
        string? opened = null;
        vm.OpenRun = id => opened = id;
        vm.NewWorkflowCommand.Execute(null);
        vm.Workflow!.Steps[1].Value = "{{input.n}}";
        Assert.Equal(["Type it in…"], vm.RunInputChoices);
        Assert.True(vm.IsTypingInput);

        vm.TypedInput = "{ nope";
        vm.RunCommand.Execute(null);
        Assert.Contains("not JSON", vm.Error);
        Assert.Null(opened);

        vm.TypedInput = """{"n": 1}""";
        vm.RunCommand.Execute(null);
        Assert.Equal("1", JsonText.Of((await Engine.WhenFinished(opened!)).Output));

        vm.AddSampleCommand.Execute(null);
        vm.Samples[0].Name = "Five";
        vm.Samples[0].Input = """{"n": 5}""";
        vm.AddSampleCommand.Execute(null);
        vm.Samples[1].Input = """{"n": 7}""";
        Assert.Equal(["Five", "Sample", "Type it in…"], vm.RunInputChoices);

        vm.RunInputChoice = "Five";
        Assert.False(vm.IsTypingInput);
        vm.RunCommand.Execute(null);
        Assert.Equal("5", JsonText.Of((await Engine.WhenFinished(opened!)).Output));
        Assert.Contains("\"n\": 5", vm.RestExample);

        vm.Flush();
        Assert.Equal(["Five", "Sample"], new WorkflowLibrary(Library.Folder).Find(vm.Workflow.Id)!.Samples.Select(s => s.Name));
    });

    [Fact]
    public Task Workflows_saved_elsewhere_appear_in_the_list() => Headless.Run(() =>
    {
        var vm = Designer();
        Library.Save(new Flow("From elsewhere").Workflow);

        Assert.Contains(vm.Workflows, w => w.Name == "From elsewhere");
        Assert.Equal("From elsewhere", vm.Name);
    });

    [Fact]
    public Task Triggers_are_set_on_the_start_step() => Headless.Run(() =>
    {
        var vm = Designer();
        vm.NewWorkflowCommand.Execute(null);
        var start = vm.Workflow!.Steps.Single(s => s.Type == StepType.Start);
        Assert.Equal("By hand", vm.Diagram.Summary(start));

        vm.Diagram.SelectedStep = start.Id;
        vm.Diagram.SelectionChanged!();
        Assert.True(vm.ShowsTriggers);
        Assert.False(vm.ShowsWorkflowProperties);

        vm.RestTrigger = true;
        vm.MqttTrigger = true;
        vm.MqttTopic = "orders/#";
        Assert.Equal("By hand · REST API · MQTT orders/#", vm.Diagram.Summary(start));
        vm.Flush();
        Assert.True(new WorkflowLibrary(Library.Folder).Find(vm.Workflow.Id)!.Triggers.Rest);

        // Other steps have no triggers.
        vm.Diagram.SelectedStep = vm.Workflow.Steps.Single(s => s.Type == StepType.End).Id;
        vm.Diagram.SelectionChanged!();
        Assert.False(vm.ShowsTriggers);
    });

    [Fact]
    public Task Global_variables_are_offered_the_paths_of_the_sample_inputs() => Headless.Run(() =>
    {
        var vm = Designer();
        vm.NewWorkflowCommand.Execute(null);
        Assert.Empty(vm.InputPathChoices);

        vm.AddSampleCommand.Execute(null);
        vm.Samples[0].Input = """{"order": {"id": 7}}""";
        vm.AddSampleCommand.Execute(null);
        vm.Samples[1].Input = """{"order": {"id": 8, "rush": true}, "broken": """;
        Assert.Equal(["{{input.order}}", "{{input.order.id}}"], vm.InputPathChoices);

        vm.AddGlobalCommand.Execute(null);
        Assert.Equal("{{input.order}}", vm.Globals[0].Value);
        vm.Globals[0].Value = "anything typed";
        Assert.Equal("anything typed", vm.Workflow!.Globals[0].Value);
    });

    [Fact]
    public Task Side_panels_collapse_float_and_keep_their_widths() => Headless.Run(() =>
    {
        var vm = Designer();
        Assert.True(vm.IsLeftDocked);

        vm.ToggleLeftCollapsedCommand.Execute(null);
        Assert.False(vm.IsLeftDocked);
        Assert.Equal(Avalonia.Layout.Orientation.Vertical, vm.LeftTabsOrientation);

        // Choosing a tab opens the panel again.
        vm.ShowStepsTabCommand.Execute(null);
        Assert.True(vm.IsLeftDocked);
        Assert.True(vm.IsStepsTab);

        vm.LeftWidth = 5000;
        vm.ToggleRightCollapsedCommand.Execute(null);
        var saved = WorkflowSettings.Load();
        Assert.Equal(800, saved.DesignerLeftWidth);
        Assert.True(saved.DesignerRightCollapsed);
        Assert.True(Designer().IsRightCollapsed);

        vm.FloatLeftCommand.Execute(null);
        Assert.True(vm.IsLeftFloating);
        Assert.False(vm.IsLeftDocked);
    });

    [Fact]
    public Task A_schedule_is_edited_on_the_start_step_and_kept_once_it_reads() => Headless.Run(() =>
    {
        var vm = Designer();
        vm.NewWorkflowCommand.Execute(null);
        vm.AddSampleCommand.Execute(null);
        Engine.Restore();

        vm.ScheduleTrigger = true;
        Assert.Equal(Joktan.Mqtt.ScheduleType.Interval, vm.Schedule.ScheduleType);
        Assert.DoesNotContain(vm.Schedule.TypeChoices, c => c.StartsWith("Manual"));
        Assert.Equal("The schedule starts once the workflow is saved.", vm.ScheduleStatus);

        vm.Schedule.ScheduleTypeIndex = 2;
        vm.Schedule.DailyTimes = "nope";
        Assert.NotNull(vm.ScheduleError);
        Assert.Null(vm.ScheduleStatus);
        vm.Schedule.DailyTimes = "07:15";
        Assert.Null(vm.ScheduleError);
        vm.ScheduleInputChoice = vm.ScheduleInputChoices[1];
        vm.Flush();

        var saved = new WorkflowLibrary(Library.Folder).Find(vm.Workflow!.Id)!;
        Assert.True(saved.Triggers.Scheduled);
        Assert.Equal(Joktan.Mqtt.ScheduleType.Daily, saved.Triggers.Schedule.Type);
        Assert.Equal([new System.TimeOnly(7, 15)], saved.Triggers.Schedule.Times);
        Assert.Equal(vm.Samples[0].Name, saved.Triggers.ScheduleInput);
        Assert.StartsWith("Next run: ", vm.ScheduleStatus);
        Assert.Contains("Schedule", vm.SelectedWorkflow!.Detail);
        Assert.Contains("07:15", StepSummary.Triggers(saved.Triggers));

        // Another workflow, then back: the editor shows what was saved.
        vm.NewWorkflowCommand.Execute(null);
        Assert.False(vm.ScheduleTrigger);
        vm.Select(saved.Id);
        Assert.True(vm.Schedule.IsDaily);
        Assert.Equal("07:15", vm.Schedule.DailyTimes);
        Assert.Equal(vm.Samples[0].Name, vm.ScheduleInputChoice);
    });
}
