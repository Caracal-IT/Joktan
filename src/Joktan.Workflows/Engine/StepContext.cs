using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Engine;

/// <summary>What a step has to work with: the run's data, the app, and the run's pause and kill.</summary>
public sealed class StepContext
{
    private readonly ActiveRun _active;

    internal StepContext(WorkflowEngine engine, ActiveRun active, CancellationToken cancel)
    {
        Engine = engine;
        _active = active;
        Cancel = cancel;
    }

    public WorkflowEngine Engine { get; }

    public WorkflowRun Run => _active.Run;

    /// <summary>Set when the run is killed or runs out of time.</summary>
    public CancellationToken Cancel { get; }

    public IWorkflowServices Services => Engine.Services;

    /// <summary>Whether the run is being stopped only because Joktan is closing, to carry on next time.</summary>
    public bool IsShuttingDown => _active.ShuttingDown;

    public JsonObject Data => Run.Data;

    public JsonObject Vars => Data[Templates.Vars] as JsonObject ?? (JsonObject)(Data[Templates.Vars] = new JsonObject());

    public string Render(string text) => Templates.Render(text, Data);

    public JsonNode? Value(string text) => Templates.Value(text, Data);

    public void Log(string text, bool isError = false)
    {
        Engine.AddLog(Run, text, isError);
        Engine.Changed(Run);
    }

    /// <summary>Marks the run as waiting, rather than running, for as long as the action takes.</summary>
    public async Task<T> WaitingAsync<T>(Func<Task<T>> action)
    {
        Engine.SetWaiting(_active, true);
        try
        {
            return await action();
        }
        finally
        {
            if (!Run.Status.IsFinished())
                Engine.SetWaiting(_active, false);
        }
    }

    /// <summary>Waits until a time, standing still while the run is paused, so the time left is kept for after.</summary>
    public async Task DelayUntilAsync(DateTime until)
    {
        Run.WaitUntil = until;
        if (Run.Kind == WorkflowKind.Long)
            Engine.Store.Save(Run);
        Engine.Changed(Run);
        await WaitingAsync(async () =>
        {
            while (true)
            {
                Cancel.ThrowIfCancellationRequested();
                var left = until - Engine.Now;
                if (left <= TimeSpan.Zero)
                    return true;
                if (_active.PauseRequested)
                {
                    // The time left is counted from the resume.
                    var started = Engine.Now;
                    await WaitWhilePausedAsync();
                    until += Engine.Now - started;
                    Run.WaitUntil = until;
                    continue;
                }

                using var wake = CancellationTokenSource.CreateLinkedTokenSource(Cancel, _active.Pausing.Token);
                try
                {
                    // In slices, so that a clock given by a test is followed too.
                    await Task.Delay(left < TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1), wake.Token);
                }
                catch (OperationCanceledException) when (!Cancel.IsCancellationRequested)
                {
                    // Paused; the loop waits for the resume.
                }
            }
        });
    }

    /// <summary>Waits while the run is paused.</summary>
    public async Task WaitWhilePausedAsync()
    {
        if (!_active.PauseRequested)
            return;

        // The time limit of a short run stands still while it is paused.
        _active.StopClock(Engine.Now);
        try
        {
            while (_active.PauseRequested)
            {
                Cancel.ThrowIfCancellationRequested();
                if (Run.Kind == WorkflowKind.Long)
                    Engine.Store.Save(Run);
                await _active.Resumed.Task.WaitAsync(Cancel);
            }
        }
        finally
        {
            _active.StartClock(Engine.Now);
        }
    }

    /// <summary>When the step's time limit runs out: kept from before a restart, or the limit from now.</summary>
    public DateTime DeadlineAfter(TimeSpan limit)
    {
        Run.Deadline ??= Engine.Now + limit;
        if (Run.Kind == WorkflowKind.Long)
            Engine.Store.Save(Run);
        return Run.Deadline.Value;
    }

    /// <summary>When the question of a user input step is escalated: kept from before a restart, or the wait from now.</summary>
    public DateTime EscalationAfter(TimeSpan wait)
    {
        Run.EscalateAt ??= Engine.Now + wait;
        if (Run.Kind == WorkflowKind.Long)
            Engine.Store.Save(Run);
        return Run.EscalateAt.Value;
    }

    /// <summary>
    /// Asks a person for values, and waits for the answer, or until a time if one is given. A question with
    /// a time to escalate goes to whom it is escalated to then, and carries on waiting.
    /// </summary>
    public async Task<JsonObject?> AskAsync(PendingInput question)
    {
        var answer = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _active.Answer = answer;

        // One carried over a restart after its time is escalated already, without saying so again.
        if (question is { EscalateAt: { } at, EscalateTo: { } to, Escalated: false } && at <= Engine.Now)
        {
            question.FirstAssignedTo = question.AssignedTo;
            question.AssignedTo = to;
            question.Escalated = true;
            question.Metadata["assignedTo"] = AssigneeJson(to);
        }

        Run.PendingInput = question;
        Run.Status = RunStatus.WaitingForInput;
        if (Run.Kind == WorkflowKind.Long)
            Engine.Store.Save(Run);
        Log($"Waiting for an answer from {Describe(question.AssignedTo)}: {question.Message}");
        try
        {
            while (true)
            {
                var escalateAt = question.Escalated ? null : question.EscalateAt;
                DateTime? wake = (question.Until, escalateAt) switch
                {
                    ({ } until, { } escalate) => until < escalate ? until : escalate,
                    ({ } until, null) => until,
                    (null, { } escalate) => escalate,
                    _ => null,
                };
                if (wake is null)
                    return await answer.Task.WaitAsync(Cancel);

                var left = wake.Value - Engine.Now;
                if (left > TimeSpan.Zero)
                {
                    try
                    {
                        // In slices, so that a clock given by a test is followed too.
                        return await answer.Task.WaitAsync(left < TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1), Cancel);
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }
                }

                if (question.Until is { } limit && limit <= Engine.Now)
                    return null;
                Escalate(question);
            }
        }
        finally
        {
            _active.Answer = null;
            Run.PendingInput = null;
            if (!Run.Status.IsFinished())
            {
                Run.Status = _active.PauseRequested ? RunStatus.Paused : RunStatus.Running;
                Engine.Changed(Run);
            }
        }
    }

    private void Escalate(PendingInput question)
    {
        if (question.EscalateTo is not { } to)
        {
            question.EscalateAt = null;
            return;
        }

        question.FirstAssignedTo = question.AssignedTo;
        question.AssignedTo = to;
        question.Escalated = true;
        question.Metadata["assignedTo"] = AssigneeJson(to);
        if (Run.Kind == WorkflowKind.Long)
            Engine.Store.Save(Run);
        Log($"Not answered in time; escalated to {Describe(to)}.");
        Engine.RaiseEscalated(Run, question);
    }

    /// <summary>Who a question is for, for the log, such as "the role Approver" or "the user Ann Lee".</summary>
    public static string Describe(Assignee assignee) => !assignee.IsSet
        ? "anyone"
        : assignee.Kind == AssigneeKind.Role ? $"the role {Name(assignee)}" : $"the user {Name(assignee)}";

    private static string Name(Assignee assignee) => assignee.Name.Trim().Length > 0 ? assignee.Name.Trim() : assignee.Id.Trim();

    /// <summary>Who a question is for, as it goes with the question's metadata.</summary>
    public static JsonObject AssigneeJson(Assignee assignee) => new()
    {
        ["kind"] = assignee.Kind == AssigneeKind.Role ? "role" : "user",
        ["id"] = assignee.Id.Trim(),
        ["name"] = Name(assignee),
    };

    /// <summary>The run's step with an id, from the workflow as it was when the run started.</summary>
    public WorkflowStep? Step(string id) => Run.Workflow.Steps.FirstOrDefault(s => s.Id == id);
}
