using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Controls;

/// <summary>How a step did in a run, which the canvas shows on it.</summary>
public enum StepMark
{
    None,
    Done,
    Failed,
    Running,
    Waiting,
    Input,
}

/// <summary>
/// What a <see cref="WorkflowCanvas"/> draws: a workflow, what is selected, and for a run, how each step did and
/// which links the run followed. When the diagram can be edited, the canvas hands what is done on it to the owner.
/// </summary>
public sealed class WorkflowDiagram
{
    public Workflow? Workflow { get; set; }

    public bool IsEditable { get; init; }

    public string? SelectedStep { get; set; }

    public WorkflowLink? SelectedLink { get; set; }

    /// <summary>How each step did in the run shown, and how many times it ran.</summary>
    public Dictionary<string, (StepMark Mark, int Times)> Marks { get; } = new(StringComparer.Ordinal);

    /// <summary>The links the run followed, as <see cref="KeyOf"/> gives them.</summary>
    public HashSet<string> Taken { get; } = new(StringComparer.Ordinal);

    /// <summary>What is wrong with each step, shown as a warning on it.</summary>
    public Dictionary<string, string> Problems { get; } = new(StringComparer.Ordinal);

    /// <summary>Says what a step does, under its name.</summary>
    public Func<WorkflowStep, string> Summary { get; set; } = step => step.Type.Text();

    /// <summary>Raised when something drawn has changed.</summary>
    public event Action? Changed;

    /// <summary>Raised when the canvas should bring every step into view.</summary>
    public event Action? FitRequested;

    // What the canvas does, for the owner to carry out. Unset on a diagram that cannot be edited.

    /// <summary>A step was dragged to a new place, given its id and top left.</summary>
    public Action<string, Point>? Moved { get; set; }

    /// <summary>An output was linked to a step: from, output, to.</summary>
    public Action<string, string, string>? Linked { get; set; }

    /// <summary>A type was dropped from the palette, at a place on the canvas.</summary>
    public Action<StepType, Point>? Dropped { get; set; }

    /// <summary>A swimlane's edge was dragged, making it taller or shorter.</summary>
    public Action? LanesChanged { get; set; }

    /// <summary>Delete was pressed.</summary>
    public Action? DeleteRequested { get; set; }

    /// <summary>A step or link was chosen, or the empty canvas clicked.</summary>
    public Action? SelectionChanged { get; set; }

    public void Refresh() => Changed?.Invoke();

    public void Fit() => FitRequested?.Invoke();

    public static string KeyOf(WorkflowLink link) => $"{link.From}|{link.Output}|{link.To}";

    /// <summary>Shows how a run went: its steps marked, the links it followed lit.</summary>
    public void Show(WorkflowRun? run)
    {
        Marks.Clear();
        Taken.Clear();
        if (run is null)
        {
            Refresh();
            return;
        }

        foreach (var group in run.Steps.GroupBy(s => s.StepId))
        {
            var last = group.Last();
            var mark = last.Status switch
            {
                StepStatus.Done => StepMark.Done,
                StepStatus.Failed => StepMark.Failed,
                _ when run.Status == RunStatus.WaitingForInput => StepMark.Input,
                _ when run.Status is RunStatus.Waiting or RunStatus.Paused => StepMark.Waiting,
                _ => StepMark.Running,
            };
            Marks[group.Key] = (mark, group.Count());
        }

        // A link was followed where one step was followed by the next by that output.
        for (var i = 0; i + 1 < run.Steps.Count; i++)
        {
            var from = run.Steps[i];
            var to = run.Steps[i + 1];
            if (from.Output is { } output)
                Taken.Add($"{from.StepId}|{output}|{to.StepId}");
        }

        // Paused or queued between steps: the step it goes on with is the one it waits at.
        if (!run.Status.IsFinished() && run.CurrentStep is { } current && run.Steps.LastOrDefault() is not { Status: StepStatus.Running })
        {
            Marks[current] = (StepMark.Waiting, Marks.TryGetValue(current, out var before) ? before.Times : 0);
            if (run.Steps.LastOrDefault() is { Output: { } output } previous)
                Taken.Add($"{previous.StepId}|{output}|{current}");
        }

        Refresh();
    }
}
