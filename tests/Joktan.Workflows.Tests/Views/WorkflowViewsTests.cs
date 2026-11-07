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
    public Task Condition_and_switch_outputs_take_labels_written_on_their_links() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        var workflow = vm.Workflow!;
        var start = workflow.Steps.Single(s => s.Type == StepType.Start);
        var end = workflow.Steps.Single(s => s.Type == StepType.End);
        var condition = new WorkflowStep { Id = "if", Name = "Approved?", Type = StepType.Condition, X = start.X + 320, Y = start.Y };
        var choice = new WorkflowStep { Id = "sw", Name = "Size", Type = StepType.Switch, X = condition.X + 360, Y = condition.Y + 200, Cases = ["\"s\"", "\"m\""] };
        end.X = choice.X + 360;
        end.Y = condition.Y;
        workflow.Steps.AddRange([condition, choice]);
        workflow.Links.Clear();
        workflow.Links.AddRange([
            new WorkflowLink { From = start.Id, To = condition.Id },
            new WorkflowLink { From = condition.Id, Output = StepOutputs.True, To = end.Id },
            new WorkflowLink { From = condition.Id, Output = StepOutputs.False, To = choice.Id },
            new WorkflowLink { From = choice.Id, Output = StepOutputs.Case("\"s\""), To = end.Id },
            new WorkflowLink { From = choice.Id, Output = StepOutputs.Default, To = end.Id },
        ]);
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);

        vm.Diagram.SelectedStep = condition.Id;
        vm.Diagram.SelectionChanged!();
        Headless.Settle();
        Headless.Find<TextBox>(window, t => t.Name == "TrueLabelBox").Text = "Approved";
        Headless.Find<TextBox>(window, t => t.Name == "FalseLabelBox").Text = "Rejected";
        Assert.Equal("Approved", condition.Labels[StepOutputs.True]);

        vm.Diagram.SelectedStep = choice.Id;
        vm.Diagram.SelectionChanged!();
        Headless.Settle();
        vm.SelectedStep!.Cases[0].Label = "Small";
        Headless.Find<TextBox>(window, t => t.Name == "DefaultLabelBox").Text = "Anything else";
        Assert.Equal("Small", choice.Labels[StepOutputs.Case("\"s\"")]);

        // Renaming a case keeps its label; the link's own box changes the same label.
        vm.SelectedStep.Cases[0].Text = "\"xs\"";
        vm.SelectedStep.Cases[0].Apply();
        Assert.Equal("Small", StepTypes.Label(choice, StepOutputs.Case("\"xs\"")));
        vm.Diagram.SelectedStep = null;
        vm.Diagram.SelectedLink = workflow.Links.Single(l => l.From == condition.Id && l.Output == StepOutputs.True);
        vm.Diagram.SelectionChanged!();
        Assert.Equal("Approved", vm.SelectedLinkLabel);
        vm.SelectedLinkLabel = "OK";
        Assert.Equal("OK", StepTypes.Label(condition, StepOutputs.True));
        vm.SelectedLinkLabel = " ";
        Assert.False(condition.Labels.ContainsKey(StepOutputs.True));
        vm.SelectedLinkLabel = "Approved";
        Headless.Settle();
        Picture(window, "designer-link-labels");
        window.Close();
    });

    [Fact]
    public Task Swimlanes_are_added_named_and_saved_with_the_workflow() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);
        Assert.Empty(vm.Workflow!.Lanes);

        // The button is further down the panel than the window shows, so its command is run.
        var add = Headless.Find<Button>(window, b => b.Name == "AddLaneButton");
        add.Command!.Execute(null);
        add.Command.Execute(null);
        Headless.Settle();
        Assert.Equal(2, Headless.Find<ItemsControl>(window, i => i.Name == "LaneList").ItemCount);
        vm.Lanes[0].Name = "Customer";
        vm.Lanes[1].Name = "Warehouse";
        var end = vm.Workflow.Steps.Single(s => s.Type == StepType.End);
        Assert.Equal(0, DiagramLayout.LaneOf(vm.Workflow, end));
        end.Y = DiagramLayout.LaneTop(vm.Workflow, 1) + 60;
        Assert.Equal(1, DiagramLayout.LaneOf(vm.Workflow, end));
        Headless.Settle();
        Picture(window, "designer-swimlanes");

        var id = vm.Workflow.Id;
        vm.Flush();
        Engine.Library.Reload();
        Assert.Equal(["Customer", "Warehouse"], Engine.Library.Find(id)!.Lanes.Select(l => l.Name));
        window.Close();
    });

    // Saves what a window drew when JOKTAN_SCREENSHOTS names a folder, to look at by eye.
    internal static void Picture(Window window, string name)
    {
        if (System.Environment.GetEnvironmentVariable("JOKTAN_SCREENSHOTS") is { Length: > 0 } folder)
        {
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(System.IO.Path.Combine(folder, name + ".png"));
        }
    }

    private FakeDatabase Shop()
    {
        var database = Services.AddDatabase("db1", "Shop");
        database.Table("orders", new Joktan.Data.Postgres.TableColumn("id", "bigint", false, null, IsPrimaryKey: true, IsIdentity: true),
            new Joktan.Data.Postgres.TableColumn("customer", "text", true, null), new Joktan.Data.Postgres.TableColumn("qty", "integer", false, "1"),
            new Joktan.Data.Postgres.TableColumn("paid", "boolean", true, null));
        return database;
    }

    [Fact]
    public Task The_database_steps_have_their_own_section_and_are_set_up_in_a_window() => Headless.Run(async () =>
    {
        Shop();
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        vm.Workflow!.Globals.Add(new GlobalVariable { Name = "customer" });
        Assert.Equal(5, vm.Palette.Single(g => g.Title == Resources.Strings.StepGroup_Database).Items.Count);
        vm.AddFromPaletteCommand.Execute(vm.Palette.SelectMany(g => g.Items).First(i => i.Type == StepType.DatabaseUpdate));
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 950);
        Assert.True(Headless.Find<Button>(window, b => b.Name == "ConfigureDatabaseButton").IsEffectivelyVisible);
        Assert.Contains("customer", Headless.Find<ComboBox>(window, c => c.Name == "ResultVariableBox").ItemsSource!.Cast<string>());
        Picture(window, "designer-database-step");

        Headless.Click(Headless.Find<Button>(window, b => b.Name == "ConfigureDatabaseButton"));
        var editor = window.OwnedWindows.OfType<DatabaseStepWindow>().Single();
        var step = (DatabaseStepViewModel)editor.DataContext!;
        step.SelectedDatabase = step.DatabaseChoices[0];
        await step.Loading;
        step.Table = "orders";
        await step.Loading;
        Headless.Settle();
        Assert.Equal(4, Headless.Find<ItemsControl>(editor, i => i.Name == "ColumnsList").ItemCount);
        Assert.Equal("Shop", (Headless.Find<ComboBox>(editor, c => c.Name == "DatabaseBox").SelectedItem as Joktan.Workflows.Engine.DatabaseChoice)?.Name);
        Assert.Equal("public", Headless.Find<ComboBox>(editor, c => c.Name == "SchemaBox").SelectedItem);
        Assert.Equal("orders", Headless.Find<ComboBox>(editor, c => c.Name == "TableBox").SelectedItem);
        Assert.True(Headless.Find<Border>(editor, b => b.Name == "FiltersCard").IsEffectivelyVisible);
        Assert.Equal(Resources.Strings.Check_UpdateNeedsColumns, Headless.Find<TextBlock>(editor, t => t.Name == "ProblemText").Text);

        step.Columns[3].Value = "true";
        step.Columns[0].Variable = "changedIds";
        step.Columns[0].AllRows = true;
        step.AddFilterCommand.Execute(null);
        step.Filters[0].Column = "customer";
        step.Filters[0].Value = "{{vars.customer}}";
        Headless.Settle();
        Assert.False(Headless.Find<TextBlock>(editor, t => t.Name == "ProblemText").IsVisible);
        Assert.Equal("paid, id", vm.SelectedStep!.DatabaseColumnsText.Replace("id, paid", "paid, id"));
        Picture(editor, "database-update-window");

        // Asked for again, the same window comes to the front.
        Headless.Click(Headless.Find<Button>(window, b => b.Name == "ConfigureDatabaseButton"));
        Assert.Single(window.OwnedWindows.OfType<DatabaseStepWindow>());
        editor.Close();
        window.Close();
    });

    [Fact]
    public Task A_user_input_step_takes_metadata_from_constants_or_global_variables() => Headless.Run(() =>
    {
        var vm = new WorkflowDesignerViewModel(Engine);
        vm.NewWorkflowCommand.Execute(null);
        vm.Workflow!.Globals.Add(new GlobalVariable { Name = "customer" });
        vm.AddFromPaletteCommand.Execute(vm.Palette.SelectMany(g => g.Items).First(i => i.Type == StepType.UserInput));
        var window = Headless.Show(new WorkflowDesignerView(), vm, 1500, 1100);

        Headless.Click(Headless.Find<Button>(window, b => b.Name == "AddMetadataButton"));
        var value = Headless.Find<ComboBox>(Headless.Find<ItemsControl>(window, i => i.Name == "MetadataList"));
        Assert.Equal(["{{vars.customer}}"], value.ItemsSource!.Cast<string>());
        value.SelectedItem = "{{vars.customer}}";
        Headless.Settle();
        Assert.Equal("{{vars.customer}}", vm.SelectedStep!.Step.Metadata.Single().Value);
        Picture(window, "designer-user-input-metadata");
        window.Close();
    });

    [Fact]
    public Task A_read_and_an_upsert_show_what_they_need() => Headless.Run(async () =>
    {
        Shop();
        foreach (var (type, name) in new[] { (StepType.DatabaseRead, "database-read-window"), (StepType.DatabaseUpsert, "database-upsert-window"),
                     (StepType.DatabaseInsert, "database-insert-window"), (StepType.DatabaseDelete, "database-delete-window") })
        {
            var flow = new Flow(name);
            var model = flow.Add(new WorkflowStep { Type = type, Name = "Orders", Database = "db1", Table = "orders", ManyRows = type == StepType.DatabaseUpsert });
            var panel = new StepViewModel(model, new StepChoices
            {
                Databases = ((Joktan.Workflows.Engine.IWorkflowServices)Services).Databases,
                OpenDatabase = Services.OpenDatabase,
                Globals = () => ["customer"],
            }, () => { }, flow.Workflow);
            var vm = new DatabaseStepViewModel(panel);
            await vm.Loading;
            if (type == StepType.DatabaseRead)
            {
                vm.Columns[1].Variable = "customer";
                vm.FirstRowOnly = true;
            }

            var window = Headless.Show(new DatabaseStepWindow { DataContext = vm });
            Assert.Equal(type != StepType.DatabaseDelete, window.FindControl<Border>("ColumnsCard")!.IsVisible);
            Assert.True((type is StepType.DatabaseRead or StepType.DatabaseDelete) == window.FindControl<Border>("FiltersCard")!.IsVisible, $"filters of {type}");
            Assert.Equal(type is StepType.DatabaseUpsert or StepType.DatabaseInsert, window.FindControl<ToggleSwitch>("ManyRowsSwitch")!.IsEffectivelyVisible);
            Picture(window, name);
            window.Close();
        }
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
        Picture(window, "palette");
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
            Metadata = [new() { Name = "region", Value = "EU" }], AssignedTo = new() { Kind = AssigneeKind.Role, Id = "Approver", Name = "Approver" },
        });
        Library.Save(flow.Workflow);
        var window = Headless.Show(new WorkflowRunsView(), vm, 1500, 950);
        var run = Engine.Start(flow.Workflow, JsonNode.Parse("{}"), RunTrigger.Manual);
        await Headless.Until(() => Headless.Find<Border>(window, b => b.Name == "QuestionCard").IsEffectivelyVisible);

        Assert.Equal("Waiting for input", Headless.Find<TextBlock>(window, t => t.Name == "StatusText").Text);
        var shown = Headless.All<SelectableTextBlock>(Headless.Find<ItemsControl>(window, i => i.Name == "QuestionMetadata")).Select(t => t.Text).ToList();
        Assert.Equal([run.Id, run.Serial.ToString(), """{"kind":"role","id":"Approver","name":"Approver"}""", "EU"], shown);
        Assert.Equal("For the role Approver", Headless.Find<TextBlock>(window, t => t.Name == "AssignedToText").Text);
        Picture(window, "runs-question-metadata");
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
