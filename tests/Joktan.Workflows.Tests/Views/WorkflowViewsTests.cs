using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Controls;
using Joktan.Workflows.Model;
using Joktan.Workflows.Tests.Engine;
using Joktan.Workflows.ViewModels;
using Joktan.Workflows.Views;
using Xunit;

namespace Joktan.Workflows.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class WorkflowViewsTests : EngineTest
{
    [Fact]
    public Task The_designer_shows_the_canvas_and_the_properties_of_what_is_selected() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);
        Assert.False(Headless.Find<WorkflowCanvas>(window).IsVisible);

        vm.NewWorkflowCommand.Execute(null);
        Headless.Settle();
        Assert.True(Headless.Find<WorkflowCanvas>(window).IsVisible);
        Assert.Equal("New workflow", Headless.Find<TextBox>(window, t => t.Name == "WorkflowName").Text);
        Assert.Equal(3, Headless.All<ToggleSwitch>(window).Count(t => t.Name is "RestSwitch" or "MqttSwitch" or "ScheduleSwitch"));

        vm.AddFromPaletteCommand.Execute(vm.Palette.SelectMany(g => g.Items).First(i => i.Type == StepType.Script));
        Headless.Settle();
        Assert.Equal("Script", Headless.Find<TextBox>(window, t => t.Name == "StepName").Text);
        var code = Headless.Find<CodeEditor>(window, e => e.Name == "CodeBox");
        Assert.True(code.IsEffectivelyVisible);
        Assert.Contains("Log(", code.Editor.Text);
        Assert.True(code.Editor.ShowLineNumbers);
        Assert.True(Headless.Find<NumericUpDown>(window, n => n.Name == "RetriesBox").IsEffectivelyVisible);
        window.Close();
    });

    [Fact]
    public Task Choosing_the_start_step_shows_its_triggers() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);
        var canvas = Headless.Find<WorkflowCanvas>(window);
        var start = vm.Workflow!.Steps.Single(s => s.Type == StepType.Start);
        var onStart = canvas.TranslatePoint(canvas.FromCanvas(new Point(start.X + 60, start.Y + 14)), window)!.Value;

        window.MouseDown(onStart, MouseButton.Left);
        window.MouseUp(onStart, MouseButton.Left);
        Headless.Settle();

        Assert.Equal(start.Id, vm.Diagram.SelectedStep);
        Assert.True(Headless.Find<StackPanel>(window, p => p.Name == "TriggersPanel").IsEffectivelyVisible);
        var rest = Headless.Find<ToggleSwitch>(window, t => t.Name == "RestSwitch");
        Assert.True(rest.IsEffectivelyVisible);
        rest.IsChecked = true;
        Assert.True(vm.Workflow.Triggers.Rest);

        Headless.Find<ToggleSwitch>(window, t => t.Name == "ScheduleSwitch").IsChecked = true;
        Headless.Settle();
        Assert.True(vm.Workflow.Triggers.Scheduled);
        var editor = Headless.Find<Joktan.Mqtt.Views.ScheduleEditor>(window);
        Assert.True(editor.IsEffectivelyVisible);
        Assert.Same(vm.Schedule, editor.DataContext);
        window.Close();
    });

    [Fact]
    public Task The_code_opens_in_a_window_of_its_own_that_edits_the_same_step() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        vm.AddFromPaletteCommand.Execute(vm.Palette.SelectMany(g => g.Items).First(i => i.Type == StepType.Script));
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);

        Headless.Click(Headless.Find<Button>(window, b => b.Name == "OpenEditorButton"));
        var editor = window.OwnedWindows.OfType<CodeEditorWindow>().Single();
        Headless.Settle();

        var big = Headless.Find<CodeEditor>(editor);
        big.Editor.Document.Text = "return 42;";
        Headless.Settle();
        Assert.Equal("return 42;", vm.SelectedStep!.Step.Code);
        Assert.Equal("return 42;", Headless.Find<CodeEditor>(window, e => e.Name == "CodeBox").Editor.Text);

        // Asked for again, the same window comes to the front.
        Headless.Click(Headless.Find<Button>(window, b => b.Name == "OpenEditorButton"));
        Assert.Single(window.OwnedWindows.OfType<CodeEditorWindow>());
        editor.Close();
        window.Close();
    });

    [Fact]
    public Task The_icon_tabs_show_the_workflows_or_the_steps() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);
        var tree = Headless.Find<Border>(window, b => b.Name == "WorkflowsPanel");
        var palette = Headless.Find<Border>(window, b => b.Name == "StepsPanel");
        Assert.True(tree.IsEffectivelyVisible);
        Assert.False(palette.IsEffectivelyVisible);
        Assert.Contains("selected", Headless.Find<Button>(window, b => b.Name == "WorkflowsTab").Classes);

        Headless.Click(Headless.Find<Button>(window, b => b.Name == "StepsTab"));

        Assert.False(tree.IsEffectivelyVisible);
        Assert.True(palette.IsEffectivelyVisible);
        Assert.Contains("selected", Headless.Find<Button>(window, b => b.Name == "StepsTab").Classes);
        Assert.Equal("Steps", vm.LeftPanelTitle);
        window.Close();
    });

    [Fact]
    public Task A_global_variable_chosen_above_the_code_goes_in_at_the_caret() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        vm.AddGlobalCommand.Execute(null);
        vm.Globals[0].Name = "orderId";
        vm.AddFromPaletteCommand.Execute(vm.Palette.SelectMany(g => g.Items).First(i => i.Type == StepType.Script));
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);
        var code = Headless.Find<CodeEditor>(window, e => e.Name == "CodeBox");
        code.Editor.Document.Text = "return ;";
        code.Editor.CaretOffset = 7;

        var box = Headless.Find<ComboBox>(window, c => c.Name == "InsertVariableBox");
        box.SelectedItem = "orderId";
        Headless.Settle();

        Assert.Equal("return Vars[\"orderId\"];", vm.SelectedStep!.Step.Code);
        Assert.Null(box.SelectedItem);
        window.Close();
    });

    [Fact]
    public Task A_side_panel_floats_in_a_window_and_goes_back_when_it_is_closed() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);
        var name = Headless.Find<TextBox>(window, t => t.Name == "WorkflowName");

        Headless.Click(Headless.Find<Button>(window, b => b.Name == "FloatRightButton"));
        var floating = Assert.Single(window.OwnedWindows);
        Assert.Same(floating, TopLevel.GetTopLevel(name));
        Assert.Equal(WorkflowDesignerViewModel.CollapsedWidth, Headless.Find<Grid>(window, g => g.Name == "Panels").ColumnDefinitions[4].Width.Value);

        floating.Close();
        Headless.Settle();
        Assert.False(vm.IsRightFloating);
        Assert.Same(window, TopLevel.GetTopLevel(name));

        Headless.Click(Headless.Find<Button>(window, b => b.Name == "CollapseLeftButton"));
        Assert.Equal(WorkflowDesignerViewModel.CollapsedWidth, Headless.Find<Grid>(window, g => g.Name == "Panels").ColumnDefinitions[0].Width.Value);
        Headless.Click(Headless.Find<Button>(window, b => b.Name == "WorkflowsTab"));
        Assert.Equal(vm.LeftWidth, Headless.Find<Grid>(window, g => g.Name == "Panels").ColumnDefinitions[0].Width.Value);
        window.Close();
    });

    [Fact]
    public Task Clicking_a_step_on_the_palette_adds_it() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        vm.ShowStepsTabCommand.Execute(null);
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);

        Headless.Click(PaletteButton(window, StepType.Delay));

        Assert.Contains(vm.Workflow!.Steps, s => s.Type == StepType.Delay);
        window.Close();
    });

    private static Button PaletteButton(Window window, StepType type)
        => Headless.Find<Button>(window, b => b.DataContext is PaletteItem item && item.Type == type && b.Classes.Contains("palette-item"));

    private static Point Middle(Control control, Window window)
        => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    [Fact]
    public Task Every_step_on_the_palette_has_its_picture() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        vm.ShowStepsTabCommand.Execute(null);
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);

        foreach (var type in StepTypes.All.Where(t => t != StepType.Start))
        {
            var icon = Headless.All<PathIcon>(PaletteButton(window, type)).Single();
            Assert.Same(StepVisuals.Icon(type), icon.Data);
        }

        Assert.Equal(StepTypes.All.Count, StepTypes.All.Select(StepVisuals.Icon).Distinct().Count());
        window.Close();
    });

    [Fact]
    public Task A_step_dragged_from_the_palette_is_dropped_where_it_is_let_go() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        vm.ShowStepsTabCommand.Execute(null);
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);
        var canvas = Headless.Find<WorkflowCanvas>(window);
        var ghost = Headless.Find<Border>(window, b => b.Name == "DragGhost");
        var from = Middle(PaletteButton(window, StepType.Delay), window);
        var to = Middle(canvas, window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(30, 0));
        Headless.Settle();
        Assert.True(ghost.IsVisible);
        Assert.Equal(StepType.Delay, ((PaletteItem)ghost.DataContext!).Type);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Headless.Settle();

        Assert.False(ghost.IsVisible);
        var dropped = Assert.Single(vm.Workflow!.Steps, s => s.Type == StepType.Delay);
        var centre = canvas.ToCanvas(canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), canvas)!.Value);
        Assert.InRange(dropped.X + DiagramLayout.Width / 2, centre.X - DiagramLayout.Grid, centre.X + DiagramLayout.Grid);

        // A drag is not also a click, so only the one step was added.
        Assert.Equal(3, vm.Workflow.Steps.Count);
        window.Close();
    });

    [Fact]
    public Task A_step_let_go_away_from_the_canvas_is_not_added() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        vm.ShowStepsTabCommand.Execute(null);
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);
        var from = Middle(PaletteButton(window, StepType.Log), window);
        var away = Middle(Headless.Find<TextBox>(window, t => t.Name == "WorkflowName"), window);

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(away);
        window.MouseUp(away, MouseButton.Left);
        Headless.Settle();

        Assert.Equal(2, vm.Workflow!.Steps.Count);
        Assert.False(Headless.Find<Border>(window, b => b.Name == "DragGhost").IsVisible);
        window.Close();
    });

    [Fact]
    public Task The_runs_page_asks_the_question_a_run_waits_on() => Headless.Run(async () =>
    {
        var vm = new WorkflowRunsViewModel(Engine);
        var flow = new Flow("Approve").Then(new WorkflowStep
        {
            Type = StepType.UserInput, Message = "Approve?", Fields = [new InputField { Name = "ok", Default = "true" }],
        });
        Library.Save(flow.Workflow);
        var window = Headless.Show(new WorkflowRunsView(), vm, 1500, 950);
        var run = Engine.Start(flow.Workflow, JsonNode.Parse("{}"), RunTrigger.Manual);
        await Headless.Until(() => Headless.Find<Border>(window, b => b.Name == "QuestionCard").IsEffectivelyVisible);

        Assert.Equal("Waiting for input", Headless.Find<TextBlock>(window, t => t.Name == "StatusText").Text);
        Headless.Click(Headless.Find<Button>(window, b => b.Name == "AnswerButton"));

        var finished = await Engine.WhenFinished(run.Id);
        Assert.Equal(RunStatus.Completed, finished.Status);
        await Headless.Until(() => !Headless.Find<Border>(window, b => b.Name == "QuestionCard").IsEffectivelyVisible);
        window.Close();
    });

    [Fact]
    public Task The_runs_page_kills_a_run() => Headless.Run(async () =>
    {
        var vm = new WorkflowRunsViewModel(Engine);
        var flow = new Flow().Then(Flow.Delay("1h"));
        Library.Save(flow.Workflow);
        var window = Headless.Show(new WorkflowRunsView(), vm, 1500, 950);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Headless.Until(() => Headless.Find<Button>(window, b => b.Name == "PauseButton").IsEffectivelyVisible);

        Headless.Click(Headless.Find<Button>(window, b => b.Name == "KillButton"));

        await Engine.WhenFinished(run.Id);
        await Headless.Until(() => Headless.Find<TextBlock>(window, t => t.Name == "StatusText").Text == "Killed");
        window.Close();
    });

    [Fact]
    public Task The_settings_page_uses_switches() => Headless.Run(() =>
    {
        var window = Headless.Show(new WorkflowSettingsView(), new WorkflowSettingsViewModel(Engine));

        Assert.Equal(3, Headless.All<ToggleSwitch>(window).Count());
        Assert.Empty(Headless.All<CheckBox>(window));
        Assert.Equal(10m, Headless.Find<NumericUpDown>(window, n => n.Name == "MaxRunningBox").Value);
        window.Close();
    });
}
