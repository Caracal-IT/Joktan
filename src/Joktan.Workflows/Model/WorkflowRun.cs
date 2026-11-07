using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Joktan.Workflows.Model;

[JsonConverter(typeof(JsonStringEnumConverter<RunStatus>))]
public enum RunStatus
{
    /// <summary>Waiting for one of the runs allowed at once to finish.</summary>
    Queued,
    Running,

    /// <summary>In a delay, or waiting for a message or another workflow.</summary>
    Waiting,

    /// <summary>Waiting for a person to answer a user input step.</summary>
    WaitingForInput,
    Paused,
    Completed,
    Failed,
    Killed,
}

/// <summary>What started a run.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RunTrigger>))]
public enum RunTrigger
{
    Manual,
    Rest,
    Mqtt,

    /// <summary>A call workflow step of another run.</summary>
    Workflow,

    /// <summary>The workflow's schedule.</summary>
    Schedule,
}

[JsonConverter(typeof(JsonStringEnumConverter<StepStatus>))]
public enum StepStatus
{
    Running,
    Done,
    Failed,
}

public static class RunStatusExtensions
{
    /// <summary>Whether the run has finished, one way or another, and will not change again.</summary>
    public static bool IsFinished(this RunStatus status) => status is RunStatus.Completed or RunStatus.Failed or RunStatus.Killed;
}

/// <summary>One time a workflow was run: what it was given, the steps it took and how it ended.</summary>
public sealed class WorkflowRun
{
    public string Id { get; set; } = "";

    /// <summary>The run's number: one more than the run started before it, of any workflow, never used twice.</summary>
    public long Serial { get; set; }

    /// <summary>The serial number as it is shown, such as #42.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string SerialText => $"#{Serial}";

    public string WorkflowId { get; set; } = "";

    public string WorkflowName { get; set; } = "";

    public WorkflowKind Kind { get; set; }

    public RunTrigger Trigger { get; set; }

    /// <summary>Such as the topic of the message that started it.</summary>
    public string TriggerDetail { get; set; } = "";

    public RunStatus Status { get; set; }

    public DateTime Started { get; set; }

    public DateTime? Ended { get; set; }

    /// <summary>The run that started this one with a call workflow step, if any.</summary>
    public string? ParentRunId { get; set; }

    /// <summary>The workflow as it was when the run started, so that changing it does not change a run under way.</summary>
    public Workflow Workflow { get; set; } = new();

    /// <summary>input, vars and last, which paths such as <c>{{vars.name}}</c> read.</summary>
    public JsonObject Data { get; set; } = [];

    /// <summary>What an end step gave, for whoever waits on the run.</summary>
    public JsonNode? Output { get; set; }

    /// <summary>Why the run failed.</summary>
    public string? Error { get; set; }

    /// <summary>The step under way, or to run next after a pause or a restart.</summary>
    public string? CurrentStep { get; set; }

    /// <summary>When a delay under way ends, so that one carried over a restart ends when it would have.</summary>
    public DateTime? WaitUntil { get; set; }

    /// <summary>When the time limit of the step under way runs out, so that one carried over a restart keeps its deadline.</summary>
    public DateTime? Deadline { get; set; }

    /// <summary>When the question of the user input step under way is escalated, so that one carried over a restart keeps that time.</summary>
    public DateTime? EscalateAt { get; set; }

    /// <summary>The run a call workflow step under way waits on, so that after a restart it waits on that one rather than starting another.</summary>
    public string? WaitingOn { get; set; }

    /// <summary>How many times the step under way has been tried and failed, for its retries.</summary>
    public int Attempt { get; set; }

    /// <summary>How many steps the run has taken in all, including those no longer kept in <see cref="Steps"/>.</summary>
    public int StepCount { get; set; }

    /// <summary>Whether the run was cut off by Joktan closing, rather than failing of itself.</summary>
    public bool Interrupted { get; set; }

    /// <summary>The question of a user input step that is waiting for an answer.</summary>
    public PendingInput? PendingInput { get; set; }

    /// <summary>Where each loop under way is, by its step, so that a run carried over a restart goes on from there.</summary>
    public Dictionary<string, LoopState> Loops { get; set; } = [];

    /// <summary>The steps taken, the latest <see cref="KeptSteps"/> of them for a run that has looped a long time.</summary>
    public List<StepRecord> Steps { get; set; } = [];

    public const int KeptSteps = 500;

    public List<RunLogEntry> Log { get; set; } = [];
}

/// <summary>One step a run took.</summary>
public sealed class StepRecord
{
    public string StepId { get; set; } = "";

    public string Name { get; set; } = "";

    public StepType Type { get; set; }

    public StepStatus Status { get; set; }

    public DateTime Started { get; set; }

    public DateTime? Ended { get; set; }

    /// <summary>The output it left by, such as next, true or error.</summary>
    public string? Output { get; set; }

    public string? Error { get; set; }

    public JsonNode? Result { get; set; }
}

public sealed class RunLogEntry
{
    public DateTime Time { get; set; }

    public string Text { get; set; } = "";

    public bool IsError { get; set; }
}

/// <summary>How far a for each or while has gone.</summary>
public sealed class LoopState
{
    /// <summary>How many times round it has gone.</summary>
    public int Index { get; set; }

    /// <summary>What a for each goes through, as it was when the loop began.</summary>
    public JsonArray? Items { get; set; }
}

/// <summary>What a user input step asks, and the values it offers to start with.</summary>
public sealed class PendingInput
{
    /// <summary>The question's own id, new each time a step asks, so that an answer meant for one question is never taken by the next.</summary>
    public string Id { get; set; } = "";

    public string StepId { get; set; } = "";

    /// <summary>When the question was asked.</summary>
    public DateTime Asked { get; set; }

    /// <summary>The page of Workflows / Pages the question is shown with on the website, with its paths filled in; null for none.</summary>
    public WorkflowPage? Page { get; set; }

    public string Message { get; set; } = "";

    public List<InputField> Fields { get; set; } = [];

    /// <summary>What goes with the question: always the run's id and serial number, then the step's own metadata.</summary>
    public JsonObject Metadata { get; set; } = [];

    /// <summary>When the question stops waiting, if it has a time limit.</summary>
    public DateTime? Until { get; set; }

    /// <summary>Who is asked: the role or user of the step, or whom it was escalated to.</summary>
    public Assignee AssignedTo { get; set; } = new();

    /// <summary>Who the question goes to if it is not answered by <see cref="EscalateAt"/>; null when it is not escalated.</summary>
    public Assignee? EscalateTo { get; set; }

    public DateTime? EscalateAt { get; set; }

    /// <summary>Whether the question has been escalated, so that it is now for <see cref="EscalateTo"/>.</summary>
    public bool Escalated { get; set; }

    /// <summary>Who was asked first, before the question was escalated.</summary>
    public Assignee? FirstAssignedTo { get; set; }
}
