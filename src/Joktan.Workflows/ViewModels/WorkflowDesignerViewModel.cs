using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.ViewModels;
using Joktan.Workflows.Controls;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Resources;
using Joktan.Workflows.Steps;

namespace Joktan.Workflows.ViewModels;

/// <summary>
/// Workflows / Designer: the workflows in the folder, a palette of steps to drag onto the canvas, and the
/// properties of what is selected. Changes are saved as they are made.
/// </summary>
public sealed partial class WorkflowDesignerViewModel : PageViewModel
{
    private readonly WorkflowEngine _engine;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private Workflow? _unsaved;
    private bool _loading;

    public WorkflowDesignerViewModel(WorkflowEngine engine) : base(Strings.Designer_Title, Strings.Designer_Subtitle)
    {
        _engine = engine;
        engine.Pages.Changed += PagesChanged;
        Choices = new StepChoices
        {
            Clients = () => engine.Services.MqttClients,
            FolderMessages = engine.Services.FolderMessages,
            Requests = engine.Services.SavedRequests,
            Workflows = () => engine.Library.All.Select(w => new WorkflowChoice(w.Id, w.Name)).ToList(),
            Pages = () => engine.Pages.All.Select(p => new PageChoice(p.Id, p.Name)).ToList(),
            CreatePage = (step, name) =>
            {
                var page = WorkflowPage.For(step, engine.Pages.FreeName(name));
                engine.Pages.Save(page);
                return new PageChoice(page.Id, page.Name);
            },
            Scripts = engine.Scripts,
            Databases = engine.Services.Databases,
            Assignees = engine.Services.Assignees,
            OpenDatabase = engine.Services.OpenDatabase,
            Connections = engine.Services.Connections,
            DefaultWorkingFolder = () => engine.Settings.WorkingFolder.Trim(),
            Globals = () => Workflow?.Globals.Select(g => g.Name.Trim()).Where(WorkflowCheck.IsName).Distinct().ToList() ?? [],
            GlobalImports = language =>
            {
                var settings = engine.Settings;
                IEnumerable<string> global = language switch
                {
                    ScriptLanguage.Go => settings.GoLibraries,
                    ScriptLanguage.JavaScript => settings.JavaScriptLibraries,
                    _ => settings.CSharpImports.Concat(settings.CSharpLibraries),
                };
                return global.Select(g => g.Trim()).Where(g => g.Length > 0).ToList();
            },
        };
        Diagram = new WorkflowDiagram
        {
            IsEditable = true,
            Moved = (_, _) => Changed(),
            Linked = Link,
            Dropped = (type, at) => AddStep(type, at, linkFrom: null),
            DeleteRequested = DeleteSelection,
            SelectionChanged = SelectionChanged,
            LanesChanged = LanesChanged,
        };
        Diagram.Summary = step => StepSummary.Of(step, engine.Library, Diagram.Workflow);
        Palette = StepTypes.Palette.Select(g => new PaletteGroup(g.Key.Text(), g.Select(t => new PaletteItem(t)).ToList())).ToList();
        _saveTimer.Tick += (_, _) => Flush();
        engine.Library.Changed += Reload;
        engine.RunsChanged += () => Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(ScheduleStatus)));
        Reload();
    }

    public WorkflowDiagram Diagram { get; }

    /// <summary>Which of the left column's tabs shows: the workflows, or the steps to add.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStepsTab), nameof(LeftPanelTitle))]
    public partial bool IsWorkflowsTab { get; set; } = true;

    public bool IsStepsTab => !IsWorkflowsTab;

    public string LeftPanelTitle => IsWorkflowsTab ? Strings.Designer_Workflows : Strings.Designer_Steps;

    // A tab chosen while its panel is collapsed opens the panel; while it floats, brings its window forward.
    [RelayCommand]
    private void ShowWorkflowsTab()
    {
        IsWorkflowsTab = true;
        RevealLeft();
    }

    [RelayCommand]
    private void ShowStepsTab()
    {
        IsWorkflowsTab = false;
        RevealLeft();
    }

    [RelayCommand]
    private void ShowProperties()
    {
        if (IsRightFloating)
            FloatingRequested?.Invoke(false);
        else
            IsRightCollapsed = false;
    }

    private void RevealLeft()
    {
        if (IsLeftFloating)
            FloatingRequested?.Invoke(true);
        else
            IsLeftCollapsed = false;
    }

    // ---- The side panels: their widths, and whether each is collapsed or floating ----

    public const double CollapsedWidth = 44;

    /// <summary>Raised to bring a floating panel's window forward: true for the left one.</summary>
    public event Action<bool>? FloatingRequested;

    public double LeftWidth
    {
        get => _engine.Settings.DesignerLeftWidth;
        set
        {
            _engine.Settings.DesignerLeftWidth = Math.Clamp(value, 180, 800);
            _engine.Settings.Save();
            OnPropertyChanged();
        }
    }

    public double RightWidth
    {
        get => _engine.Settings.DesignerRightWidth;
        set
        {
            _engine.Settings.DesignerRightWidth = Math.Clamp(value, 260, 900);
            _engine.Settings.Save();
            OnPropertyChanged();
        }
    }

    public bool IsLeftCollapsed
    {
        get => _engine.Settings.DesignerLeftCollapsed;
        set
        {
            _engine.Settings.DesignerLeftCollapsed = value;
            _engine.Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLeftDocked));
            OnPropertyChanged(nameof(LeftTabsOrientation));
        }
    }

    public bool IsRightCollapsed
    {
        get => _engine.Settings.DesignerRightCollapsed;
        set
        {
            _engine.Settings.DesignerRightCollapsed = value;
            _engine.Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRightDocked));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLeftDocked), nameof(LeftTabsOrientation))]
    public partial bool IsLeftFloating { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRightDocked))]
    public partial bool IsRightFloating { get; set; }

    /// <summary>Whether the left panel shows in its column, rather than collapsed into its tabs or floating.</summary>
    public bool IsLeftDocked => !IsLeftCollapsed && !IsLeftFloating;

    public bool IsRightDocked => !IsRightCollapsed && !IsRightFloating;

    /// <summary>The left tabs run across the top, or down the narrow column when the panel is not there.</summary>
    public Avalonia.Layout.Orientation LeftTabsOrientation => IsLeftDocked ? Avalonia.Layout.Orientation.Horizontal : Avalonia.Layout.Orientation.Vertical;

    [RelayCommand]
    private void ToggleLeftCollapsed() => IsLeftCollapsed = !IsLeftCollapsed;

    [RelayCommand]
    private void ToggleRightCollapsed() => IsRightCollapsed = !IsRightCollapsed;

    [RelayCommand]
    private void FloatLeft() => IsLeftFloating = true;

    [RelayCommand]
    private void FloatRight() => IsRightFloating = true;

    public StepChoices Choices { get; }

    public IReadOnlyList<PaletteGroup> Palette { get; }

    public ObservableCollection<WorkflowItemViewModel> Workflows { get; } = [];

    /// <summary>The workflows in their folders, for the tree on the left.</summary>
    public ObservableCollection<WorkflowTreeNode> Tree { get; } = [];

    // What the tree last showed, so that saving a workflow only rebuilds it when something it shows has changed.
    private string _treeShape = "";

    // Folders folded away in the tree, kept as it is rebuilt.
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    /// <summary>The node chosen in the tree: a workflow opens it, a folder is where new ones go.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderSelected), nameof(TargetFolderText))]
    public partial WorkflowTreeNode? SelectedNode { get; set; }

    public bool IsFolderSelected => SelectedNode is { IsFolder: true };

    /// <summary>Where a new workflow or folder goes: the folder chosen, or the folder of the workflow open.</summary>
    public string TargetFolder => SelectedNode is { IsFolder: true } folder ? folder.Path
        : Workflow is { } workflow ? _engine.Library.FolderOf(workflow.Id) : "";

    public string TargetFolderText => TargetFolder.Length == 0 ? Strings.Designer_InTheTopFolder : string.Format(Strings.Designer_InFolder, TargetFolder);

    [ObservableProperty]
    public partial string NewFolderName { get; set; } = "";

    [RelayCommand]
    private void NewFolder()
    {
        var name = NewFolderName.Trim();
        if (name.Length == 0)
            return;
        try
        {
            var path = _engine.Library.CreateFolder(TargetFolder.Length == 0 ? name : TargetFolder + "/" + name);
            NewFolderName = "";
            Error = null;
            SelectedNode = Find(Tree, n => n.IsFolder && n.Path == path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error = string.Format(Strings.Designer_CouldNotSave, e.Message);
        }
    }

    /// <summary>Deletes the folder chosen, if nothing is in it.</summary>
    [RelayCommand]
    private void DeleteFolder()
    {
        if (SelectedNode is not { IsFolder: true } folder)
            return;
        try
        {
            Error = _engine.Library.DeleteFolder(folder.Path) ? null : Strings.Designer_FolderNotEmpty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }
    }

    /// <summary>The folders a workflow can be moved to, the top first.</summary>
    public IReadOnlyList<string> FolderChoices => [Strings.Designer_TopFolder, .. _engine.Library.Folders];

    public string WorkflowFolder
    {
        get => Workflow is { } workflow && _engine.Library.FolderOf(workflow.Id) is { Length: > 0 } folder ? folder : Strings.Designer_TopFolder;
        set
        {
            if (Workflow is not { } workflow || value is null)
                return;
            var folder = value == Strings.Designer_TopFolder ? "" : value;
            if (folder == _engine.Library.FolderOf(workflow.Id))
                return;
            _unsaved = null;
            _saveTimer.Stop();
            try
            {
                _engine.Library.Save(workflow, folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Error = string.Format(Strings.Designer_CouldNotSave, e.Message);
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(TargetFolderText));
        }
    }

    partial void OnSelectedNodeChanged(WorkflowTreeNode? value)
    {
        if (value?.Item is { } item && item != SelectedWorkflow)
            SelectedWorkflow = item;
    }

    public IReadOnlyList<WorkflowKind> Kinds { get; } = Enum.GetValues<WorkflowKind>();

    public string Folder => _engine.Library.Folder;

    /// <summary>Opens a script step's code in a window of its own. Set by the view.</summary>
    public Action<StepViewModel>? OpenEditor { get; set; }

    /// <summary>Opens the window a database step is set up in. Set by the view.</summary>
    public Action<StepViewModel>? OpenDatabaseEditor { get; set; }

    /// <summary>Opens a run on Workflows / Runs. Set by the app.</summary>
    public Action<string>? OpenRun { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkflow), nameof(HasNoWorkflow), nameof(Name), nameof(Description), nameof(Kind), nameof(RestTrigger),
        nameof(MqttTrigger), nameof(MqttClient), nameof(MqttTopic), nameof(ScheduleTrigger), nameof(ScheduleInputChoice),
        nameof(ShowsWorkflowProperties), nameof(ShowsTriggers))]
    public partial WorkflowItemViewModel? SelectedWorkflow { get; set; }

    public Workflow? Workflow => SelectedWorkflow?.Workflow;

    public bool HasWorkflow => Workflow is not null;

    public bool HasNoWorkflow => Workflow is null;

    /// <summary>The step selected on the canvas.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedStep), nameof(ShowsWorkflowProperties), nameof(ShowsTriggers))]
    public partial StepViewModel? SelectedStep { get; set; }

    public bool HasSelectedStep => SelectedStep is not null;

    /// <summary>Offers the roles and users again, after the Users page read or changed them.</summary>
    public void PeopleChanged() => SelectedStep?.PeopleChanged();

    /// <summary>Shows the pages of Workflows / Pages again in the selected step's list.</summary>
    public void PagesChanged() => SelectedStep?.PagesChanged();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLink), nameof(ShowsWorkflowProperties), nameof(ShowsTriggers))]
    public partial string? SelectedLinkText { get; set; }

    public bool HasSelectedLink => SelectedLinkText is not null;

    /// <summary>The label of the output the link selected leaves by, written on the link.</summary>
    public string SelectedLinkLabel
    {
        get => SelectedLinkStep is { } step ? StepTypes.Label(step, Diagram.SelectedLink!.Output) : "";
        set
        {
            if (SelectedLinkStep is not { } step || Diagram.SelectedLink is not { } link || StepTypes.Label(step, link.Output) == value.Trim())
                return;
            Edit(_ => StepTypes.SetLabel(step, link.Output, value));
            Diagram.Refresh();
            OnPropertyChanged();
        }
    }

    private WorkflowStep? SelectedLinkStep
        => Diagram.SelectedLink is { } link ? Workflow?.Steps.FirstOrDefault(s => s.Id == link.From) : null;

    public bool ShowsWorkflowProperties => HasWorkflow && !HasSelectedStep && !HasSelectedLink;

    /// <summary>Whether what starts the workflow is shown: with the start step, where a run begins, and with the workflow itself.</summary>
    public bool ShowsTriggers => ShowsWorkflowProperties || SelectedStep is { IsStart: true };

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    public ObservableCollection<string> Problems { get; } = [];

    /// <summary>The workflow's global variables, set from the input when a run starts.</summary>
    public ObservableCollection<GlobalViewModel> Globals { get; } = [];

    public bool HasGlobals => Globals.Count > 0;

    [RelayCommand]
    private void AddGlobal()
    {
        if (Workflow is not { } workflow)
            return;
        var name = "value";
        for (var n = 2; workflow.Globals.Any(g => g.Name == name); n++)
            name = "value" + n;
        var global = new GlobalVariable { Name = name, Value = InputPathChoices.FirstOrDefault() ?? "{{input." + name + "}}" };
        workflow.Globals.Add(global);
        Globals.Add(new GlobalViewModel(global, RemoveGlobal, Changed));
        OnPropertyChanged(nameof(HasGlobals));
        Changed();
    }

    private void RemoveGlobal(GlobalViewModel global)
    {
        Workflow?.Globals.Remove(global.Global);
        Globals.Remove(global);
        OnPropertyChanged(nameof(HasGlobals));
        Changed();
    }

    public bool HasProblems => Problems.Count > 0;

    // ---- The workflow's own properties ----

    public string Name
    {
        get => Workflow?.Name ?? "";
        set => Edit(w => w.Name = value);
    }

    public string Description
    {
        get => Workflow?.Description ?? "";
        set => Edit(w => w.Description = value);
    }

    public WorkflowKind Kind
    {
        get => Workflow?.Kind ?? WorkflowKind.Short;
        set => Edit(w => w.Kind = value);
    }

    public bool RestTrigger
    {
        get => Workflow?.Triggers.Rest ?? false;
        set
        {
            Edit(w => w.Triggers.Rest = value);
            OnPropertyChanged();
        }
    }

    public bool MqttTrigger
    {
        get => Workflow?.Triggers.Mqtt ?? false;
        set
        {
            Edit(w => w.Triggers.Mqtt = value);
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> ClientChoices => _engine.Services.MqttClients;

    public string MqttClient
    {
        get => Workflow?.Triggers.MqttClient ?? "";
        set => Edit(w => w.Triggers.MqttClient = value ?? "");
    }

    public string MqttTopic
    {
        get => Workflow?.Triggers.MqttTopic ?? "";
        set => Edit(w => w.Triggers.MqttTopic = value);
    }

    public bool ScheduleTrigger
    {
        get => Workflow?.Triggers.Scheduled ?? false;
        set
        {
            Edit(w => w.Triggers.Scheduled = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScheduleStatus));
        }
    }

    /// <summary>Edits the workflow's schedule; what is typed is kept only once it reads as a schedule.</summary>
    [ObservableProperty]
    public partial ScheduleEditorViewModel Schedule { get; private set; } = NewScheduleEditor(WorkflowSchedules.Default);

    /// <summary>Why what is typed is not a schedule, or null.</summary>
    [ObservableProperty]
    public partial string? ScheduleError { get; set; }

    /// <summary>What a scheduled run starts with: an empty input, or one of the samples.</summary>
    public IReadOnlyList<string> ScheduleInputChoices => [Strings.Designer_ScheduleEmptyInput, .. Samples.Select(s => s.Name).Where(n => n.Length > 0).Distinct()];

    public string ScheduleInputChoice
    {
        get => Workflow?.Triggers.ScheduleInput is { Length: > 0 } name ? name : Strings.Designer_ScheduleEmptyInput;
        set
        {
            // The list empties and fills again when the samples change; that is not a choice.
            if (value is null || value == ScheduleInputChoice)
                return;
            Edit(w => w.Triggers.ScheduleInput = value == Strings.Designer_ScheduleEmptyInput ? "" : value);
        }
    }

    /// <summary>When the schedule starts the workflow next, or why it does not.</summary>
    public string? ScheduleStatus
    {
        get
        {
            if (Workflow is not { Triggers.Scheduled: true } workflow || ScheduleError is not null)
                return null;
            if (!_engine.Settings.ScheduleTriggers)
                return Strings.Designer_SchedulesOff;
            if (_engine.ScheduleProblems.TryGetValue(workflow.Id, out var problem))
                return problem;
            if (!_engine.IsScheduleActive(workflow.Id) || _unsaved == workflow)
                return Strings.Designer_ScheduleStartsWhenSaved;
            return _engine.NextScheduledRun(workflow.Id) is { } next
                ? string.Format(Strings.Designer_ScheduleNextRun, next)
                : Strings.Designer_ScheduleNoMoreRuns;
        }
    }

    private static ScheduleEditorViewModel NewScheduleEditor(Mqtt.MessageSchedule schedule)
        => new(schedule, allowsManual: false, Strings.Designer_ScheduleWhen, Strings.Designer_ScheduleMaxRuns);

    // The editor follows the workflow selected, and writes what is typed to it once it reads as a schedule.
    private void LoadSchedule()
    {
        Schedule.PropertyChanged -= OnScheduleEdited;
        var schedule = Workflow?.Triggers.Schedule is { Type: not Mqtt.ScheduleType.Manual } current ? current : WorkflowSchedules.Default;
        Schedule = NewScheduleEditor(schedule);
        Schedule.PropertyChanged += OnScheduleEdited;
        ScheduleError = null;
    }

    private void OnScheduleEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        try
        {
            var schedule = Schedule.Build();
            ScheduleError = null;
            if (Workflow is { } workflow && WorkflowSchedules.Key(schedule) != WorkflowSchedules.Key(workflow.Triggers.Schedule))
                Edit(w => w.Triggers.Schedule = schedule);
        }
        catch (FormatException ex)
        {
            ScheduleError = ex.Message;
        }

        OnPropertyChanged(nameof(ScheduleStatus));
    }

    /// <summary>The workflow's swimlanes, if it has any, top to bottom.</summary>
    public ObservableCollection<LaneViewModel> Lanes { get; } = [];

    [RelayCommand]
    private void AddLane()
    {
        if (Workflow is not { } workflow)
            return;
        var name = $"{Strings.Designer_LaneNameStart} {workflow.Lanes.Count + 1}";
        var lane = DiagramLayout.AddLane(workflow, name);
        Lanes.Add(new LaneViewModel(lane, workflow, RemoveLane, LanesChanged));
        LanesChanged();
    }

    private void RemoveLane(LaneViewModel lane)
    {
        if (Workflow is not { } workflow || workflow.Lanes.IndexOf(lane.Lane) is not (>= 0 and var index))
            return;
        DiagramLayout.RemoveLane(workflow, index);
        Lanes.Remove(lane);
        LanesChanged();
    }

    // A lane changed, so the others may have moved, and the canvas draws them again.
    private void LanesChanged()
    {
        foreach (var lane in Lanes)
            lane.Refresh();
        Diagram.Refresh();
        Changed();
    }

    /// <summary>The workflow's sample inputs, each with a name.</summary>
    public ObservableCollection<SampleViewModel> Samples { get; } = [];

    [RelayCommand]
    private void AddSample()
    {
        if (Workflow is not { } workflow)
            return;
        var name = Strings.Designer_SampleNameStart;
        for (var n = 2; workflow.Samples.Any(s => s.Name == name); n++)
            name = $"{Strings.Designer_SampleNameStart} {n}";
        var sample = new NamedInput { Name = name };
        workflow.Samples.Add(sample);
        Samples.Add(new SampleViewModel(sample, RemoveSample, Changed, SamplesRenamed, InputsChanged));
        SamplesRenamed();
        RunInputChoice = name;
        Changed();
    }

    private void RemoveSample(SampleViewModel sample)
    {
        Workflow?.Samples.Remove(sample.Sample);
        Samples.Remove(sample);
        SamplesRenamed();
        Changed();
    }

    /// <summary>What Run starts the workflow with: one of its samples, or what is typed in.</summary>
    public IReadOnlyList<string> RunInputChoices => [.. Samples.Select(s => s.Name).Where(n => n.Length > 0).Distinct(), Strings.Designer_TypeItIn];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTypingInput))]
    public partial string? RunInputChoice { get; set; }

    public bool IsTypingInput => HasWorkflow && RunInputChoice == Strings.Designer_TypeItIn;

    /// <summary>The input typed in for Run, as JSON.</summary>
    [ObservableProperty]
    public partial string TypedInput { get; set; } = "{}";

    /// <summary>The paths into the sample inputs, such as {{input.order.id}}, for a global variable to take its value from.</summary>
    public IReadOnlyList<string> InputPathChoices
    {
        get
        {
            var paths = new List<string>();
            foreach (var sample in Workflow?.Samples ?? [])
            {
                try
                {
                    paths.AddRange(Templates.PathsOf(JsonNode.Parse(sample.Input)).Skip(1));
                }
                catch (JsonException)
                {
                    // A sample that is not JSON yet offers nothing.
                }
            }

            return paths.Distinct().ToList();
        }
    }

    private void InputsChanged() => OnPropertyChanged(nameof(InputPathChoices));

    // The list of names follows the samples; the choice stays when it is still there.
    private void SamplesRenamed()
    {
        var choice = RunInputChoice;
        OnPropertyChanged(nameof(RunInputChoices));
        RunInputChoice = choice is not null && RunInputChoices.Contains(choice) ? choice : RunInputChoices[0];
        OnPropertyChanged(nameof(RestExample));
        OnPropertyChanged(nameof(ScheduleInputChoices));
        OnPropertyChanged(nameof(ScheduleInputChoice));
    }

    /// <summary>What the REST API's workflow_start call is given to start this workflow.</summary>
    public string RestExample => Workflow is { } workflow
        ? $"POST /api/workflow_start\n{{\"workflow\": \"{workflow.Name}\", \"input\": {(workflow.Samples.FirstOrDefault()?.Input.Trim() is { Length: > 0 } sample ? sample : "{}")}, \"wait\": true}}"
        : "";

    public string? TriggerProblem => Workflow is { } workflow && _engine.TriggerProblems.TryGetValue(workflow.Id, out var problem) ? problem : null;

    // ---- Commands ----

    [RelayCommand]
    private void NewWorkflow()
    {
        Flush();
        var start = new WorkflowStep { Id = NewStepId(), Type = StepType.Start, Name = StepType.Start.Text(), X = 40, Y = 120 };
        var end = new WorkflowStep { Id = NewStepId(), Type = StepType.End, Name = StepType.End.Text(), X = 340, Y = 120 };
        var workflow = new Workflow
        {
            Id = WorkflowLibrary.NewId(),
            Name = _engine.Library.FreeName(Strings.Designer_NewWorkflow),
            Steps = [start, end],
            Links = [new WorkflowLink { From = start.Id, To = end.Id }],
        };
        Save(workflow, TargetFolder);
        Select(workflow.Id);
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Workflow is not { } original)
            return;
        Flush();
        var copy = WorkflowEngine.Copy(original);
        copy.Id = WorkflowLibrary.NewId();
        copy.Name = _engine.Library.FreeName(string.Format(Strings.Designer_CopyOf, original.Name));

        // Only one workflow can listen for a trigger by accident; the copy starts with its triggers off.
        copy.Triggers.Rest = false;
        copy.Triggers.Mqtt = false;
        copy.Triggers.Scheduled = false;
        Save(copy, _engine.Library.FolderOf(original.Id));
        Select(copy.Id);
    }

    [RelayCommand]
    private void AskDelete() => IsConfirmingDelete = Workflow is not null;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private void ConfirmDelete()
    {
        IsConfirmingDelete = false;
        if (Workflow is not { } workflow)
            return;
        _unsaved = null;
        _saveTimer.Stop();
        try
        {
            _engine.Library.Delete(workflow.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }
    }

    /// <summary>Adds a step from the palette: after the step selected, linked from its next output if that is free.</summary>
    [RelayCommand]
    private void AddFromPalette(PaletteItem item)
    {
        if (Workflow is not { } workflow)
            return;
        var after = Diagram.SelectedStep is { } id ? workflow.Steps.FirstOrDefault(s => s.Id == id) : null;
        AddStep(item.Type, DiagramLayout.FreePlace(workflow, after), after);
    }

    [RelayCommand]
    private void DeleteSelection()
    {
        if (Workflow is not { } workflow)
            return;
        if (Diagram.SelectedLink is { } link)
        {
            workflow.Links.Remove(link);
            Diagram.SelectedLink = null;
        }
        else if (Diagram.SelectedStep is { } id && workflow.Steps.FirstOrDefault(s => s.Id == id) is { Type: not StepType.Start } step)
        {
            workflow.Steps.Remove(step);
            workflow.Links.RemoveAll(l => l.From == id || l.To == id);
            Diagram.SelectedStep = null;
        }
        else
        {
            return;
        }

        SelectionChanged();
        Changed();
    }

    [RelayCommand]
    private void Fit() => Diagram.Fit();

    /// <summary>Runs the workflow by hand with the sample chosen, or the input typed in, and opens the run.</summary>
    [RelayCommand]
    private void Run()
    {
        if (Workflow is not { } workflow)
            return;
        Flush();
        Error = null;
        JsonNode? input;
        try
        {
            var text = IsTypingInput ? TypedInput : workflow.Samples.FirstOrDefault(s => s.Name == RunInputChoice)?.Input ?? "{}";
            input = text.Trim().Length == 0 ? new JsonObject() : JsonNode.Parse(text);
        }
        catch (JsonException e)
        {
            Error = string.Format(Strings.Designer_TheSampleInputIsNotJson, e.Message);
            return;
        }

        try
        {
            var run = _engine.Start(workflow, input, RunTrigger.Manual);
            OpenRun?.Invoke(run.Id);
        }
        catch (WorkflowException e)
        {
            Error = e.Message;
        }
    }


    /// <summary>Writes what has not been saved yet.</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        if (_unsaved is { } workflow)
        {
            _unsaved = null;
            Save(workflow);
        }
    }

    /// <summary>Selects a workflow by id.</summary>
    public void Select(string id)
    {
        if (Workflows.FirstOrDefault(w => w.Workflow.Id == id) is { } item)
            SelectedWorkflow = item;
    }

    partial void OnSelectedWorkflowChanged(WorkflowItemViewModel? oldValue, WorkflowItemViewModel? newValue)
    {
        if (_loading)
            return;
        if (oldValue is not null && _unsaved == oldValue.Workflow)
            Flush();
        IsConfirmingDelete = false;
        Error = null;
        Diagram.Workflow = newValue?.Workflow;
        Globals.Clear();
        foreach (var global in newValue?.Workflow.Globals ?? [])
            Globals.Add(new GlobalViewModel(global, RemoveGlobal, Changed));
        Lanes.Clear();
        foreach (var lane in newValue?.Workflow.Lanes ?? [])
            Lanes.Add(new LaneViewModel(lane, newValue!.Workflow, RemoveLane, LanesChanged));
        Samples.Clear();
        foreach (var sample in newValue?.Workflow.Samples ?? [])
            Samples.Add(new SampleViewModel(sample, RemoveSample, Changed, SamplesRenamed, InputsChanged));
        RunInputChoice = null;
        InputsChanged();
        SamplesRenamed();
        OnPropertyChanged(nameof(HasGlobals));
        LoadSchedule();
        Diagram.SelectedStep = null;
        Diagram.SelectedLink = null;
        SelectionChanged();
        CheckProblems();
        Diagram.Fit();
        OnPropertyChanged(nameof(RestExample));
        OnPropertyChanged(nameof(TriggerProblem));
        OnPropertyChanged(nameof(ScheduleStatus));
        OnPropertyChanged(nameof(WorkflowFolder));
        OnPropertyChanged(nameof(TargetFolderText));
        if (newValue is not null && SelectedNode?.Item != newValue)
            SelectedNode = Find(Tree, n => n.Item == newValue);
    }

    private void AddStep(StepType type, Point at, WorkflowStep? linkFrom)
    {
        if (Workflow is not { } workflow)
            return;
        var step = NewStep(workflow, type);
        (step.X, step.Y) = (at.X, at.Y);
        workflow.Steps.Add(step);
        if (linkFrom is not null && StepTypes.Outputs(linkFrom.Type).Contains(StepOutputs.Next)
                                 && !workflow.Links.Any(l => l.From == linkFrom.Id && l.Output == StepOutputs.Next))
            workflow.Links.Add(new WorkflowLink { From = linkFrom.Id, Output = StepOutputs.Next, To = step.Id });
        Diagram.SelectedStep = step.Id;
        Diagram.SelectedLink = null;
        SelectionChanged();
        Changed();
    }

    /// <summary>A step of a type with settings to start from, named so that it differs from the steps already there.</summary>
    public static WorkflowStep NewStep(Workflow workflow, StepType type)
    {
        var step = new WorkflowStep { Id = NewStepId(), Type = type, Name = FreeName(workflow, type.Text()) };
        var variable = type switch
        {
            StepType.Http or StepType.Grpc => "reply",
            StepType.MqttWait => "message",
            StepType.Script or StepType.CallWorkflow => "result",
            StepType.UserInput => "answer",
            _ => null,
        };
        if (variable is not null)
        {
            step.ResultVariable = variable;
            for (var n = 2; workflow.Steps.Any(s => s.ResultVariable == step.ResultVariable); n++)
                step.ResultVariable = variable + n;
        }

        switch (type)
        {
            case StepType.Script:
                step.Code = ScriptRunner.CSharpTemplate;
                break;
            case StepType.Condition:
                step.Left = "{{last}}";
                step.Operator = CompareOperator.IsNotEmpty;
                break;
            case StepType.Delay:
                step.Duration = "5s";
                break;
            case StepType.UserInput:
                step.Message = Strings.Designer_DefaultQuestion;
                step.Fields = [new InputField { Name = "approved", Label = Strings.Designer_DefaultFieldLabel, Default = "true" }];
                break;
            case StepType.SetVariable:
                step.Variable = "value";
                step.Value = "{{last}}";
                break;
            case StepType.Log:
                step.Message = "{{last}}";
                break;
            case StepType.MqttPublish:
                step.Body = "{{last}}";
                break;
            case StepType.Http:
                step.Url = "https://";
                break;
            case StepType.ForEach:
                step.Items = "{{input.items}}";
                break;
            case StepType.While:
                step.Left = "{{vars.index}}";
                step.Operator = CompareOperator.LessThan;
                step.Right = "10";
                break;
            case StepType.Switch:
                step.Left = "{{input.status}}";
                step.Cases = ["new", "paid"];
                break;
        }

        return step;
    }

    private static string FreeName(Workflow workflow, string name)
    {
        var candidate = name;
        for (var n = 2; workflow.Steps.Any(s => s.Name == candidate); n++)
            candidate = $"{name} {n}";
        return candidate;
    }

    private static string NewStepId() => Guid.NewGuid().ToString("N")[..8];

    // One link leaves each output, so a new one takes the place of the old.
    private void Link(string from, string output, string to)
    {
        if (Workflow is not { } workflow)
            return;
        workflow.Links.RemoveAll(l => l.From == from && l.Output == output);
        var link = new WorkflowLink { From = from, Output = output, To = to };
        workflow.Links.Add(link);
        Changed();
    }

    private void SelectionChanged()
    {
        var workflow = Workflow;
        var step = Diagram.SelectedStep is { } id ? workflow?.Steps.FirstOrDefault(s => s.Id == id) : null;
        if (SelectedStep?.Step != step)
            SelectedStep = step is null ? null : new StepViewModel(step, Choices, Changed, workflow)
            {
                OpenEditor = s => OpenEditor?.Invoke(s),
                OpenDatabaseEditor = s => OpenDatabaseEditor?.Invoke(s),
            };
        SelectedLinkText = Diagram.SelectedLink is { } link && workflow is not null ? Describe(workflow, link) : null;
        OnPropertyChanged(nameof(SelectedLinkLabel));
        Diagram.Refresh();
    }

    private static string Describe(Workflow workflow, WorkflowLink link)
        => string.Format(Strings.Designer_LinkFromTo,
            workflow.Steps.FirstOrDefault(s => s.Id == link.From)?.Name ?? "?",
            StepTypes.OutputText(link.Output),
            workflow.Steps.FirstOrDefault(s => s.Id == link.To)?.Name ?? "?");

    private void Edit(Action<Workflow> change)
    {
        if (Workflow is not { } workflow)
            return;
        change(workflow);
        Changed();
        OnPropertyChanged(nameof(RestExample));
    }

    // Saved a moment after the last change, so typing does not write the file on every key.
    private void Changed()
    {
        if (Workflow is not { } workflow)
            return;
        _unsaved = workflow;
        _saveTimer.Stop();
        _saveTimer.Start();
        SelectedWorkflow?.Refresh();
        CheckProblems();
        Diagram.Refresh();
    }

    private void CheckProblems()
    {
        Problems.Clear();
        Diagram.Problems.Clear();
        if (Workflow is { } workflow)
        {
            foreach (var problem in WorkflowCheck.Problems(workflow))
            {
                if (problem.StepId is { } id)
                    Diagram.Problems[id] = Diagram.Problems.TryGetValue(id, out var before) ? before + "\n" + problem.Text : problem.Text;
                else
                    Problems.Add(problem.Text);
            }
        }

        OnPropertyChanged(nameof(HasProblems));
    }

    private void Save(Workflow workflow, string? folder = null)
    {
        try
        {
            _engine.Library.Save(workflow, folder);
            Error = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = string.Format(Strings.Designer_CouldNotSave, e.Message);
        }
    }

    // The list follows the library, keeping its items, so what is selected stays selected while it is saved.
    private void Reload()
    {
        var all = _engine.Library.All;
        _loading = true;
        foreach (var item in Workflows.Where(i => !all.Contains(i.Workflow)).ToList())
            Workflows.Remove(item);
        for (var i = 0; i < all.Count; i++)
        {
            var existing = Workflows.FirstOrDefault(w => w.Workflow == all[i]);
            if (existing is null)
                Workflows.Insert(i, new WorkflowItemViewModel(all[i]));
            else
            {
                existing.Refresh();
                if (Workflows.IndexOf(existing) is var at && at != i)
                    Workflows.Move(at, i);
            }
        }

        _loading = false;
        BuildTree();
        if (SelectedWorkflow is null || !Workflows.Contains(SelectedWorkflow))
            SelectedWorkflow = Workflows.FirstOrDefault();
        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(TriggerProblem));
        OnPropertyChanged(nameof(ScheduleStatus));
        OnPropertyChanged(nameof(FolderChoices));
        OnPropertyChanged(nameof(WorkflowFolder));
    }

    // Folders first, then workflows, each by name. Rebuilt only when what it shows has changed.
    private void BuildTree()
    {
        var library = _engine.Library;
        var folders = library.Folders;
        var placed = Workflows.Select(w => (Item: w, Folder: library.FolderOf(w.Workflow.Id))).ToList();
        var shape = string.Join("|", folders) + "#" + string.Join("|", placed.Select(p => $"{p.Folder}/{p.Item.Workflow.Id}/{p.Item.Name}"));
        if (shape == _treeShape)
            return;
        _treeShape = shape;

        foreach (var node in All(Tree).Where(n => n.IsFolder))
        {
            if (node.IsExpanded)
                _collapsed.Remove(node.Path);
            else
                _collapsed.Add(node.Path);
        }

        var selected = SelectedNode;
        var byPath = new Dictionary<string, WorkflowTreeNode>(StringComparer.Ordinal);
        var roots = new List<WorkflowTreeNode>();
        foreach (var folder in folders)
        {
            var node = new WorkflowTreeNode(folder, null) { IsExpanded = !_collapsed.Contains(folder) };
            byPath[folder] = node;
            var parent = folder.Contains('/') ? folder[..folder.LastIndexOf('/')] : "";
            if (byPath.TryGetValue(parent, out var above))
                above.Children.Add(node);
            else
                roots.Add(node);
        }

        foreach (var (item, folder) in placed.OrderBy(p => p.Item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var node = new WorkflowTreeNode(folder, item);
            if (byPath.TryGetValue(folder, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }

        Tree.Clear();
        foreach (var node in roots)
            Tree.Add(node);
        SelectedNode = selected?.IsFolder == true
            ? Find(Tree, n => n.IsFolder && n.Path == selected.Path)
            : Find(Tree, n => n.Item is not null && n.Item == SelectedWorkflow);
    }

    private static IEnumerable<WorkflowTreeNode> All(IEnumerable<WorkflowTreeNode> nodes)
        => nodes.SelectMany(n => All(n.Children).Prepend(n));

    private static WorkflowTreeNode? Find(IEnumerable<WorkflowTreeNode> nodes, Func<WorkflowTreeNode, bool> match)
        => All(nodes).FirstOrDefault(match);
}

/// <summary>A sample input of the workflow being designed.</summary>
/// <summary>A swimlane, with its name and height.</summary>
public sealed class LaneViewModel(WorkflowLane lane, Workflow workflow, Action<LaneViewModel> remove, Action changed) : ViewModelBase
{
    public WorkflowLane Lane { get; } = lane;

    public string Name
    {
        get => Lane.Name;
        set
        {
            Lane.Name = value;
            changed();
        }
    }

    /// <summary>How tall the lane is; the steps under it move with it.</summary>
    public decimal Height
    {
        get => (decimal)Lane.Height;
        set
        {
            if (workflow.Lanes.IndexOf(Lane) is var index and >= 0 && (double)value != Lane.Height)
            {
                DiagramLayout.ResizeLane(workflow, index, (double)value);
                changed();
            }
        }
    }

    public RelayCommand RemoveCommand => field ??= new(() => remove(this));

    /// <summary>Shows the height again, after the lane was dragged on the canvas or kept from shrinking.</summary>
    public void Refresh() => OnPropertyChanged(nameof(Height));
}

public sealed class SampleViewModel(NamedInput sample, Action<SampleViewModel> remove, Action changed, Action renamed, Action inputChanged) : ViewModelBase
{
    public NamedInput Sample { get; } = sample;

    public string Name
    {
        get => Sample.Name;
        set
        {
            Sample.Name = value.Trim();
            changed();
            renamed();
        }
    }

    public string Input
    {
        get => Sample.Input;
        set
        {
            Sample.Input = value;
            changed();
            inputChanged();
        }
    }

    public RelayCommand RemoveCommand => field ??= new(() => remove(this));

    public RelayCommand FormatCommand => field ??= new(() =>
    {
        if (Api.RestSender.TryIndentJson(Input) is { } formatted)
        {
            Input = formatted;
            OnPropertyChanged(nameof(Input));
        }
    });
}

/// <summary>A global variable of the workflow being designed.</summary>
public sealed class GlobalViewModel(GlobalVariable global, Action<GlobalViewModel> remove, Action changed) : ViewModelBase
{
    public GlobalVariable Global { get; } = global;

    public string Name
    {
        get => Global.Name;
        set
        {
            Global.Name = value.Trim();
            changed();
        }
    }

    public string Value
    {
        get => Global.Value;
        set
        {
            Global.Value = value;
            changed();
        }
    }

    public bool Required
    {
        get => Global.Required;
        set
        {
            Global.Required = value;
            changed();
        }
    }

    public RelayCommand RemoveCommand => field ??= new(() => remove(this));
}

/// <summary>A folder, or a workflow in its folder, in the tree on the left.</summary>
public sealed partial class WorkflowTreeNode(string path, WorkflowItemViewModel? item) : ViewModelBase
{
    /// <summary>For a folder, its path, such as Orders/Returns; for a workflow, the folder it is in.</summary>
    public string Path { get; } = path;

    /// <summary>The workflow, or null for a folder.</summary>
    public WorkflowItemViewModel? Item { get; } = item;

    public bool IsFolder => Item is null;

    public bool IsWorkflow => Item is not null;

    public string Name => Item?.Name ?? (Path.Contains('/') ? Path[(Path.LastIndexOf('/') + 1)..] : Path);

    public string Detail => Item?.Detail ?? "";

    public Avalonia.Media.Geometry Icon => IsFolder ? Core.Icons.Folder : Core.Icons.Workflow;

    public ObservableCollection<WorkflowTreeNode> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;
}

/// <summary>A workflow in the list.</summary>
public sealed class WorkflowItemViewModel(Workflow workflow) : ViewModelBase
{
    public Workflow Workflow { get; } = workflow;

    public string Name => Workflow.Name;

    public string Detail => Workflow.Kind.Text() + (Workflow.Triggers.Rest ? " · REST" : "") + (Workflow.Triggers.Mqtt ? " · MQTT" : "")
        + (Workflow.Triggers.Scheduled ? " · " + Strings.RunTrigger_Schedule : "");

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Detail));
    }
}

public sealed record PaletteGroup(string Title, IReadOnlyList<PaletteItem> Items);

/// <summary>A step type on the palette.</summary>
public sealed record PaletteItem(StepType Type)
{
    public string Text => Type.Text();

    public string Tip => Type.Tip();
}
