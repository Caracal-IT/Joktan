using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Workflows.Controls;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Resources;

namespace Joktan.Workflows.ViewModels;

/// <summary>Which runs the list shows.</summary>
public enum RunFilter
{
    All,
    UnderWay,
    NeedsAnswer,
    Failed,
    Finished,
}

/// <summary>
/// Workflows / Runs: every run, newest first, and the one selected drawn on its workflow with the steps it took,
/// where it went wrong, and buttons to pause, resume, kill or answer it.
/// </summary>
public sealed partial class WorkflowRunsViewModel : PageViewModel
{
    private readonly WorkflowEngine _engine;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public WorkflowRunsViewModel(WorkflowEngine engine) : base(Strings.Runs_Title, Strings.Runs_Subtitle)
    {
        _engine = engine;
        Diagram.Summary = step => StepSummary.Of(step, engine.Library, Diagram.Workflow);
        engine.RunsChanged += () => Dispatcher.UIThread.Post(Sync);
        engine.RunChanged += run => Dispatcher.UIThread.Post(() => Changed(run));
        engine.Library.Changed += () => Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(WorkflowChoices)));
        _clock.Tick += (_, _) =>
        {
            foreach (var item in Runs.Where(r => !r.Run.Status.IsFinished()))
                item.Tick();
        };
        _clock.Start();
        Sync();
    }

    public ObservableCollection<RunItemViewModel> Runs { get; } = [];

    public WorkflowDiagram Diagram { get; } = new();

    public IReadOnlyList<RunFilter> Filters { get; } = Enum.GetValues<RunFilter>();

    [ObservableProperty]
    public partial RunFilter Filter { get; set; }

    partial void OnFilterChanged(RunFilter value) => Sync();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(HasNoSelection))]
    public partial RunItemViewModel? SelectedRun { get; set; }

    public bool HasSelection => SelectedRun is not null;

    public bool HasNoSelection => SelectedRun is null;

    public bool HasNoRuns => Runs.Count == 0;

    [ObservableProperty]
    public partial string? Error { get; set; }

    // ---- Starting a run by hand ----

    public IReadOnlyList<WorkflowChoice> WorkflowChoices => _engine.Library.All.Select(w => new WorkflowChoice(w.Id, w.Name)).ToList();

    [ObservableProperty]
    public partial WorkflowChoice? StartWorkflow { get; set; }

    [ObservableProperty]
    public partial string StartInput { get; set; } = "{}";

    [ObservableProperty]
    public partial bool IsStarting { get; set; }

    /// <summary>The samples of the workflow chosen, then typing the input in.</summary>
    public IReadOnlyList<string> StartSampleChoices => StartWorkflow is not null && _engine.Library.Find(StartWorkflow.Id) is { } workflow
        ? [.. workflow.Samples.Select(s => s.Name).Where(n => n.Length > 0).Distinct(), Strings.Designer_TypeItIn]
        : [Strings.Designer_TypeItIn];

    /// <summary>The sample chosen, which fills in the input; it can still be changed before starting.</summary>
    [ObservableProperty]
    public partial string? StartSample { get; set; }

    partial void OnStartWorkflowChanged(WorkflowChoice? value)
    {
        OnPropertyChanged(nameof(StartSampleChoices));
        StartSample = StartSampleChoices[0];
    }

    partial void OnStartSampleChanged(string? value)
    {
        if (StartWorkflow is not null && _engine.Library.Find(StartWorkflow.Id)?.Samples.FirstOrDefault(s => s.Name == value) is { } sample)
            StartInput = sample.Input;
    }

    [RelayCommand]
    private void ToggleStart()
    {
        IsStarting = !IsStarting;
        StartWorkflow ??= WorkflowChoices.FirstOrDefault();
    }

    [RelayCommand]
    private void Start()
    {
        Error = null;
        if (StartWorkflow is not { } choice)
        {
            Error = Strings.Runs_ChooseAWorkflow;
            return;
        }

        JsonNode? input;
        try
        {
            input = StartInput.Trim().Length == 0 ? new JsonObject() : JsonNode.Parse(StartInput);
        }
        catch (JsonException e)
        {
            Error = string.Format(Strings.Runs_TheInputIsNotJson, e.Message);
            return;
        }

        try
        {
            var run = _engine.Start(choice.Id, input, RunTrigger.Manual);
            IsStarting = false;
            Select(run.Id);
        }
        catch (WorkflowException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Shows a run, whatever the filter.</summary>
    public void Select(string runId)
    {
        if (Runs.All(r => r.Run.Id != runId))
        {
            Filter = RunFilter.All;
            Sync();
        }

        SelectedRun = Runs.FirstOrDefault(r => r.Run.Id == runId);
    }

    // ---- What can be done to the run selected ----

    [RelayCommand]
    private void Pause() => Act(id => _engine.Pause(id));

    [RelayCommand]
    private void Resume() => Act(id => _engine.Resume(id));

    [RelayCommand]
    private void Kill() => Act(id => _engine.Kill(id));

    [RelayCommand]
    private void Remove()
    {
        if (SelectedRun is not { } item)
            return;
        var index = Runs.IndexOf(item);
        if (_engine.Remove(item.Run.Id))
            SelectedRun = Runs.Count == 0 ? null : Runs[Math.Clamp(index, 0, Runs.Count - 1)];
    }

    /// <summary>Starts the workflow again with the same input.</summary>
    [RelayCommand]
    private void RunAgain()
    {
        if (SelectedRun?.Run is not { } run)
            return;
        Error = null;
        try
        {
            var again = _engine.Start(run.WorkflowId, run.Data[Templates.Input]?.DeepClone(), RunTrigger.Manual);
            Select(again.Id);
        }
        catch (WorkflowException e)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private void ClearFinished()
    {
        _engine.ClearFinished();
        if (SelectedRun is { } selected && selected.Run.Status.IsFinished())
            SelectedRun = Runs.FirstOrDefault();
    }

    [RelayCommand]
    private void SubmitAnswer()
    {
        if (SelectedRun is not { } item)
            return;
        Error = null;
        try
        {
            _engine.Answer(item.Run.Id, item.Answers.ToDictionary(a => a.Name, a => a.Value));
        }
        catch (WorkflowException e)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private void Fit() => Diagram.Fit();

    partial void OnSelectedRunChanged(RunItemViewModel? value)
    {
        Error = null;
        Diagram.Workflow = value?.Run.Workflow;
        Diagram.SelectedStep = null;
        Diagram.Show(value?.Run);
        Diagram.Fit();
        value?.Refresh();
    }

    private void Act(Func<string, bool> action)
    {
        if (SelectedRun is { } item)
        {
            action(item.Run.Id);
            item.Refresh();
        }
    }

    private void Changed(WorkflowRun run)
    {
        var item = Runs.FirstOrDefault(r => r.Run == run);
        if (item is null)
        {
            if (Matches(run))
                Sync();
            return;
        }

        item.Refresh();
        if (item == SelectedRun)
            Diagram.Show(run);
    }

    // The list follows the engine's runs, newest first, keeping its items so that the selection stays.
    private void Sync()
    {
        var wanted = _engine.Runs.Where(Matches).Reverse().ToList();
        foreach (var item in Runs.Where(i => !wanted.Contains(i.Run)).ToList())
            Runs.Remove(item);
        for (var i = 0; i < wanted.Count; i++)
        {
            var existing = Runs.FirstOrDefault(r => r.Run == wanted[i]);
            if (existing is null)
                Runs.Insert(i, new RunItemViewModel(wanted[i], _engine));
            else if (Runs.IndexOf(existing) is var at && at != i)
                Runs.Move(at, i);
        }

        OnPropertyChanged(nameof(HasNoRuns));
        if (SelectedRun is not null && !Runs.Contains(SelectedRun))
            SelectedRun = null;
        SelectedRun ??= Runs.FirstOrDefault();
    }

    private bool Matches(WorkflowRun run) => Filter switch
    {
        RunFilter.UnderWay => !run.Status.IsFinished(),
        RunFilter.NeedsAnswer => run.Status == RunStatus.WaitingForInput,
        RunFilter.Failed => run.Status is RunStatus.Failed or RunStatus.Killed,
        RunFilter.Finished => run.Status.IsFinished(),
        _ => true,
    };

    public static string Text(RunFilter filter) => filter switch
    {
        RunFilter.UnderWay => Strings.RunFilter_UnderWay,
        RunFilter.NeedsAnswer => Strings.RunFilter_NeedsAnswer,
        RunFilter.Failed => Strings.RunFilter_Failed,
        RunFilter.Finished => Strings.RunFilter_Finished,
        _ => Strings.RunFilter_All,
    };

    public static readonly Avalonia.Data.Converters.FuncValueConverter<RunFilter, string> FilterConverter = new(Text);
}

/// <summary>A run in the list, and all about it when it is selected.</summary>
public sealed partial class RunItemViewModel(WorkflowRun run, WorkflowEngine engine) : ViewModelBase
{
    public WorkflowRun Run { get; } = run;

    public string Name => Run.WorkflowName;

    public string SerialText => Run.SerialText;

    public RunStatus Status => Run.Status;

    public string StatusText => Run.Status.Text();

    public bool IsOk => Run.Status == RunStatus.Completed;

    public bool IsFailed => Run.Status is RunStatus.Failed or RunStatus.Killed;

    public bool IsPending => !Run.Status.IsFinished();

    public string StartedText => Run.Started.ToString(Run.Started.Date == DateTime.Today ? "HH:mm:ss" : "yyyy-MM-dd HH:mm");

    public string TriggerText => Run.Trigger.Text() + (Run.TriggerDetail.Length > 0 && Run.Trigger is RunTrigger.Mqtt or RunTrigger.Schedule ? $" · {Run.TriggerDetail}" : "");

    public string KindText => Run.Kind.Text();

    public string DurationText => Durations.Text(((Run.Ended ?? engine.Now) - Run.Started) is { } span && span > TimeSpan.Zero ? span : TimeSpan.Zero);

    public string Detail => $"{StartedText} · {TriggerText} · {DurationText}";

    public bool CanPause => Run.Status is RunStatus.Running or RunStatus.Waiting or RunStatus.Queued;

    public bool CanResume => Run.Status == RunStatus.Paused;

    public bool CanKill => !Run.Status.IsFinished();

    public bool CanRemove => Run.Status.IsFinished();

    public bool CanRunAgain => Run.Status.IsFinished() && engine.Library.Find(Run.WorkflowId) is not null;

    public string? Error => Run.Error;

    public bool HasError => Run.Error is not null;

    public string? ParentText => Run.ParentRunId is { } parent
        ? string.Format(Strings.Runs_StartedByRun, engine.Find(parent)?.SerialText ?? parent)
        : null;

    public string InputText => Pretty(Run.Data[Templates.Input]);

    public string VariablesText => Pretty(Run.Data[Templates.Vars]);

    public string OutputText => Run.Status == RunStatus.Completed ? Pretty(Run.Output) : "";

    public bool HasOutput => Run.Status == RunStatus.Completed;

    public ObservableCollection<StepRecordViewModel> Steps { get; } = [];

    public ObservableCollection<RunLogEntry> Log { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedStepResult), nameof(SelectedStepError), nameof(HasSelectedStepError))]
    public partial StepRecordViewModel? SelectedStep { get; set; }

    public string SelectedStepResult => SelectedStep is { } step ? Pretty(step.Record.Result) : "";

    public string? SelectedStepError => SelectedStep?.Record.Error;

    public bool HasSelectedStepError => SelectedStepError is not null;

    // ---- The question of a user input step ----

    public bool IsWaitingForInput => Run.Status == RunStatus.WaitingForInput && Run.PendingInput is not null;

    public string Question => Run.PendingInput?.Message ?? "";

    public string? AnswerBy => Run.PendingInput?.Until is { } until ? string.Format(Strings.Runs_AnswerBy, until.ToString("HH:mm:ss")) : null;

    public ObservableCollection<AnswerViewModel> Answers { get; } = [];

    public void Tick() => OnPropertyChanged(nameof(DurationText));

    public void Refresh()
    {
        OnPropertyChanged(string.Empty);

        // Steps are only added, and only the last can change.
        for (var i = 0; i < Run.Steps.Count; i++)
        {
            if (i < Steps.Count)
                Steps[i].Refresh();
            else
                Steps.Add(new StepRecordViewModel(Run.Steps[i]));
        }

        while (Steps.Count > Run.Steps.Count)
            Steps.RemoveAt(Steps.Count - 1);

        foreach (var entry in Run.Log.Skip(Log.Count))
            Log.Add(entry);

        // The answers are made afresh only for a new question, so what is being typed stays.
        var question = Run.PendingInput;
        if (question is null)
            Answers.Clear();
        else if (Answers.Count != question.Fields.Count || Answers.Select(a => a.Name).Where((n, i) => n != question.Fields[i].Name).Any() || Answers.Count == 0)
        {
            Answers.Clear();
            foreach (var field in question.Fields)
                Answers.Add(new AnswerViewModel(field.Name, field.Label, field.Default));
        }

        OnPropertyChanged(nameof(SelectedStepResult));
    }

    private static string Pretty(JsonNode? node) => node is null ? "null" : node.ToJsonString(JsonFormats.Indented);
}

/// <summary>A step a run took, in its list of steps.</summary>
public sealed class StepRecordViewModel(StepRecord record) : ViewModelBase
{
    public StepRecord Record { get; } = record;

    public string Name => Record.Name;

    public string TypeText => Record.Type.Text();

    public bool IsDone => Record.Status == StepStatus.Done;

    public bool IsFailed => Record.Status == StepStatus.Failed;

    public bool IsRunning => Record.Status == StepStatus.Running;

    public string OutputText => Record.Output is { } output ? "→ " + StepTypes.OutputText(output) : "";

    public string TimeText => Record.Started.ToString("HH:mm:ss.fff");

    public string DurationText => Record.Ended is { } ended ? Durations.Text(ended - Record.Started) : "";

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>A value typed in answer to a user input step.</summary>
public sealed partial class AnswerViewModel(string name, string label, string value) : ViewModelBase
{
    public string Name { get; } = name;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial string Value { get; set; } = value;
}
