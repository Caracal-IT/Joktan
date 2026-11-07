using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Core;
using Joktan.Workflows.Model;
using Joktan.Workflows.Steps;
using Microsoft.Extensions.Logging;

namespace Joktan.Workflows.Engine;

/// <summary>
/// Runs workflows: as many at once as the settings allow, with the rest queued. A run can be paused, which takes
/// effect between steps and in a delay, resumed, killed, and answered when a step asks for input. Long runs are
/// written down after every step and carry on after a restart; short ones are stopped at the time limit.
/// </summary>
public sealed class WorkflowEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly List<WorkflowRun> _runs = [];
    private readonly Dictionary<string, ActiveRun> _active = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _now;
    private readonly ILogger _log = AppLog.For("Workflows");
    private readonly MqttTriggers _triggers;
    private readonly ScheduleTriggers _schedules;
    private bool _disposed;

    // The highest serial number among the runs read at start, which a lost counter must not fall below.
    private long _highestSerial;

    /// <param name="post">Runs an action where the engine's work is done, such as on the UI thread; directly when not given.</param>
    public WorkflowEngine(WorkflowLibrary library, WorkflowSettings settings, IWorkflowServices services, RunStore store,
        Action<Action>? post = null, Func<DateTime>? now = null)
    {
        Library = library;
        Settings = settings;
        Services = services;
        Store = store;
        Post = post ?? (action => action());
        _now = now ?? (() => DateTime.Now);
        Scripts = new ScriptRunner(settings);
        _triggers = new MqttTriggers(this);
        _schedules = new ScheduleTriggers(this);
    }

    public WorkflowLibrary Library { get; }

    public WorkflowSettings Settings { get; }

    public IWorkflowServices Services { get; }

    public RunStore Store { get; }

    /// <summary>The pages of Workflows / Pages that user input steps show; none, kept in memory, unless given.</summary>
    public PageLibrary Pages { get; init; } = new(null);

    internal Action<Action> Post { get; }

    /// <summary>Runs script steps; the designer checks code with it too.</summary>
    public ScriptRunner Scripts { get; }

    /// <summary>Every run kept, oldest first.</summary>
    public IReadOnlyList<WorkflowRun> Runs
    {
        get
        {
            lock (_gate)
                return _runs.ToList();
        }
    }

    /// <summary>Raised when anything about a run changes: its status, or a step started or finished.</summary>
    public event Action<WorkflowRun>? RunChanged;

    /// <summary>Raised when a run is added or removed.</summary>
    public event Action? RunsChanged;

    /// <summary>Raised when a question nobody answered in time is escalated, with the question as it now is.</summary>
    public event Action<WorkflowRun, PendingInput>? Escalated;

    /// <summary>Why MQTT triggers are not listening, by workflow, such as a client that does not exist.</summary>
    public IReadOnlyDictionary<string, string> TriggerProblems => _triggers.Problems;

    /// <summary>Why a schedule started no run, by workflow, such as a sample input that is not JSON.</summary>
    public IReadOnlyDictionary<string, string> ScheduleProblems => _schedules.Problems;

    /// <summary>When a workflow's schedule starts it next, or null when it is not scheduled or has no more runs.</summary>
    public DateTime? NextScheduledRun(string workflowId) => _schedules.NextRun(workflowId);

    /// <summary>Whether a workflow's schedule is running, which it is not until the workflows are restored, or with schedules off.</summary>
    public bool IsScheduleActive(string workflowId) => _schedules.IsActive(workflowId);

    /// <summary>Starts the scheduled runs that are due; the engine does so every second by itself.</summary>
    public void CheckSchedules() => _schedules.Check();

    public DateTime Now => _now();

    /// <summary>A run by its id, or by its serial number, with or without its #.</summary>
    public WorkflowRun? Find(string runId)
    {
        var serial = long.TryParse(runId.TrimStart('#'), out var number) ? number : -1;
        lock (_gate)
            return _runs.FirstOrDefault(r => r.Id == runId) ?? _runs.FirstOrDefault(r => r.Serial == serial);
    }

    /// <summary>Reads the runs kept on disk and carries on the long ones that were under way, then starts listening for MQTT triggers.</summary>
    public void Restore()
    {
        foreach (var run in Store.LoadAll())
        {
            _highestSerial = Math.Max(_highestSerial, run.Serial);
            if (!run.Status.IsFinished())
            {
                // The step under way when Joktan closed did not finish; a long run tries it again.
                if (run.Steps.LastOrDefault() is { Status: StepStatus.Running } cut)
                {
                    cut.Status = StepStatus.Failed;
                    cut.Error = "Joktan closed while this step was running.";
                    cut.Ended = cut.Started;
                }

                if (run.Kind == WorkflowKind.Short)
                {
                    run.Status = RunStatus.Failed;
                    run.Error = "Joktan closed while it was running.";
                    run.Interrupted = true;
                    run.Ended ??= _now();
                    Store.Save(run);
                }
                else
                {
                    // Carried on from the step it was in, unless it was paused or the settings say to wait.
                    var paused = run.Status == RunStatus.Paused || !Settings.ResumeLongRuns;
                    run.PendingInput = null;
                    run.Status = paused ? RunStatus.Paused : RunStatus.Queued;
                    AddLog(run, paused ? "Joktan started again; the run is paused." : "Joktan started again; the run carries on.");
                    lock (_gate)
                        _active[run.Id] = new ActiveRun(run) { PauseRequested = paused };
                }
            }

            lock (_gate)
                _runs.Add(run);
        }

        RunsChanged?.Invoke();
        StartQueued();
        _triggers.Refresh();
        _schedules.Refresh();
    }

    /// <summary>Starts a run of a workflow, or queues it if as many as allowed are running. Its steps run in the background.</summary>
    /// <param name="request">For a webhook, the request, which the run reads as <c>{{run.request}}</c>.</param>
    public WorkflowRun Start(Workflow workflow, JsonNode? input, RunTrigger trigger, string detail = "", string? parentRunId = null, JsonObject? request = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (WorkflowCheck.Problems(workflow).FirstOrDefault(p => p.IsBlocking) is { } problem)
            throw new WorkflowException($"{workflow.Name} cannot run: {problem.Text}");

        var now = _now();
        var start = workflow.Steps.First(s => s.Type == StepType.Start);
        var id = $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var serial = Store.NextSerial(Math.Max(_highestSerial, Runs.Select(r => r.Serial).DefaultIfEmpty().Max()));
        var run = new WorkflowRun
        {
            Id = id,
            Serial = serial,
            WorkflowId = workflow.Id,
            WorkflowName = workflow.Name,
            Kind = workflow.Kind,
            Trigger = trigger,
            TriggerDetail = detail,
            Status = RunStatus.Queued,
            Started = now,
            ParentRunId = parentRunId,
            Workflow = Copy(workflow),
            CurrentStep = start.Id,
            Data = new JsonObject
            {
                [Templates.Input] = input?.DeepClone() ?? new JsonObject(),
                [Templates.Vars] = new JsonObject(),
                [Templates.Last] = null,
                [Templates.Run] = new JsonObject { ["id"] = id, ["serial"] = serial, ["workflow"] = workflow.Name, ["started"] = now.ToString("O") },
            },
        };
        if (trigger == RunTrigger.Mqtt)
            ((JsonObject)run.Data[Templates.Run]!)["topic"] = detail;
        if (request is not null)
            ((JsonObject)run.Data[Templates.Run]!)["request"] = request.DeepClone();
        SetGlobals(workflow, run.Data);
        AddLog(run, $"Run #{serial} started {Describe(trigger, detail)}.");

        lock (_gate)
        {
            _runs.Add(run);
            _active[run.Id] = new ActiveRun(run);
        }

        _log.LogInformation("Workflow {Workflow} started, run #{Serial} ({Run})", workflow.Name, serial, run.Id);
        RunsChanged?.Invoke();
        StartQueued();
        return run;
    }

    /// <summary>
    /// Starts one branch of a parallel step: a run of the same workflow, as the run had it, from the branch's first step,
    /// with a copy of its data. It stops at the join the branches meet at.
    /// </summary>
    internal WorkflowRun StartBranch(WorkflowRun parent, string branch, string firstStep)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var now = _now();
        var id = $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var serial = Store.NextSerial(Math.Max(_highestSerial, Runs.Select(r => r.Serial).DefaultIfEmpty().Max()));
        var data = (JsonObject)parent.Data.DeepClone();
        var info = data[Templates.Run] as JsonObject ?? [];
        info["id"] = id;
        info["serial"] = serial;
        info["started"] = now.ToString("O");
        info["branch"] = branch;
        info["parent"] = parent.Id;
        data[Templates.Run] = info;
        var run = new WorkflowRun
        {
            Id = id,
            Serial = serial,
            WorkflowId = parent.WorkflowId,
            WorkflowName = parent.WorkflowName,
            Kind = parent.Kind,
            Trigger = RunTrigger.Branch,
            TriggerDetail = parent.Id,
            Status = RunStatus.Queued,
            Started = now,
            ParentRunId = parent.Id,
            Branch = branch,
            ForkVars = (JsonObject?)data[Templates.Vars]?.DeepClone(),
            Workflow = Copy(parent.Workflow),
            CurrentStep = firstStep,
            Data = data,
        };
        AddLog(run, $"Run #{serial} started as branch {branch} of run #{parent.Serial}.");

        lock (_gate)
        {
            _runs.Add(run);
            _active[run.Id] = new ActiveRun(run);
        }

        RunsChanged?.Invoke();
        StartQueued();
        return run;
    }

    /// <summary>Sets the workflow's global variables from the run's input, refusing the run when a required one comes out empty.</summary>
    public static void SetGlobals(Workflow workflow, JsonObject data)
    {
        var vars = (JsonObject)data[Templates.Vars]!;
        foreach (var global in workflow.Globals.Where(g => g.Name.Trim().Length > 0))
        {
            var value = Templates.Value(global.Value, data);
            if (global.Required && Conditions.Compare(value, CompareOperator.IsEmpty, null))
                throw new WorkflowException($"The input has nothing for {global.Name.Trim()}, which {workflow.Name} needs: {global.Value.Trim()} is empty.");
            vars[global.Name.Trim()] = value;
        }
    }

    /// <summary>Starts a workflow found by id or name.</summary>
    public WorkflowRun Start(string workflow, JsonNode? input, RunTrigger trigger, string detail = "", string? parentRunId = null)
        => Start(Library.Find(workflow) ?? throw new WorkflowException($"There is no workflow called {workflow}."), input, trigger, detail, parentRunId);

    /// <summary>Completes when the run has finished, one way or another.</summary>
    public Task<WorkflowRun> WhenFinished(string runId)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(runId, out var active))
                return active.Finished.Task;
            return _runs.FirstOrDefault(r => r.Id == runId) is { } run
                ? Task.FromResult(run)
                : Task.FromException<WorkflowRun>(new WorkflowException($"There is no run {runId}."));
        }
    }

    /// <summary>Pauses a run: it stops before its next step, or at once in a delay.</summary>
    public bool Pause(string runId)
    {
        if (Active(runId) is not { } active || active.PauseRequested || active.Run.Status == RunStatus.WaitingForInput)
            return false;
        active.PauseRequested = true;
        active.StatusBeforePause = active.Run.Status == RunStatus.Waiting ? RunStatus.Waiting : RunStatus.Running;
        active.Pausing.Cancel();
        active.Run.Status = RunStatus.Paused;
        AddLog(active.Run, "Paused.");
        Changed(active.Run);
        return true;
    }

    public bool Resume(string runId)
    {
        if (Active(runId) is not { PauseRequested: true } active)
            return false;
        active.PauseRequested = false;
        active.Pausing = new CancellationTokenSource();
        AddLog(active.Run, "Resumed.");
        if (active.Executing is null)
        {
            active.Run.Status = RunStatus.Queued;
            Changed(active.Run);
            StartQueued();
        }
        else
        {
            active.Run.Status = active.StatusBeforePause;
            Changed(active.Run);
            active.Resumed.TrySetResult();
            active.Resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return true;
    }

    /// <summary>Stops a run at once. A workflow it is waiting on is killed too.</summary>
    public bool Kill(string runId)
    {
        if (Active(runId) is not { } active)
            return false;
        if (active.Executing is null)
        {
            Finish(active, RunStatus.Killed, null);
            return true;
        }

        active.Kill.Cancel();
        return true;
    }

    /// <summary>Answers the question of a user input step, by field name; fields not given keep the values offered.</summary>
    /// <param name="by">Who answered, such as a user signed in to the website, for the run's log; null when it is not known.</param>
    public void Answer(string runId, IReadOnlyDictionary<string, string> values, string? by = null)
    {
        if (Active(runId) is not { Answer: { } answer, Run.PendingInput: { } question } active)
            throw new WorkflowException("That run is not waiting for input.");
        var unknown = values.Keys.Where(k => question.Fields.All(f => f.Name != k)).ToList();
        if (unknown.Count > 0)
            throw new WorkflowException($"The question has no field called {string.Join(", ", unknown)}.");

        var result = new JsonObject();
        foreach (var field in question.Fields)
            result[field.Name] = Templates.Parse(values.TryGetValue(field.Name, out var value) ? value : field.Default);
        if (by is not null)
            AddLog(active.Run, $"Answered by {by}.");
        active.Answer = null;
        answer.TrySetResult(result);
    }

    /// <summary>Removes a finished run from the list and the disk.</summary>
    public bool Remove(string runId)
    {
        lock (_gate)
        {
            if (_runs.FirstOrDefault(r => r.Id == runId) is not { } run || !run.Status.IsFinished())
                return false;
            _runs.Remove(run);
        }

        Store.Delete(runId);
        RunsChanged?.Invoke();
        return true;
    }

    /// <summary>Removes every finished run.</summary>
    public int ClearFinished()
    {
        List<WorkflowRun> finished;
        lock (_gate)
        {
            finished = _runs.Where(r => r.Status.IsFinished()).ToList();
            _runs.RemoveAll(r => r.Status.IsFinished());
        }

        foreach (var run in finished)
            Store.Delete(run.Id);
        RunsChanged?.Invoke();
        return finished.Count;
    }

    /// <summary>Listens again for the MQTT messages, and follows the schedules, that start workflows, after a workflow or a setting changed.</summary>
    public void RefreshTriggers()
    {
        _triggers.Refresh();
        _schedules.Refresh();
    }

    /// <summary>Stops the runs under way. Long ones are written down as they are, to carry on next time.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _triggers.Dispose();
        _schedules.Dispose();
        List<ActiveRun> active;
        lock (_gate)
            active = _active.Values.ToList();
        foreach (var run in active)
        {
            run.ShuttingDown = true;
            if (run.Run.Kind == WorkflowKind.Long)
                Store.Save(run.Run);
            run.Kill.Cancel();
        }
    }

    internal void AddLog(WorkflowRun run, string text, bool isError = false)
    {
        run.Log.Add(new RunLogEntry { Time = _now(), Text = text, IsError = isError });
        if (run.Log.Count > 1000)
            run.Log.RemoveAt(0);
    }

    internal void Changed(WorkflowRun run) => RunChanged?.Invoke(run);

    internal void RaiseEscalated(WorkflowRun run, PendingInput question) => Escalated?.Invoke(run, question);

    internal void SetWaiting(ActiveRun active, bool waiting)
    {
        if (active.PauseRequested)
            active.StatusBeforePause = waiting ? RunStatus.Waiting : RunStatus.Running;
        else
            active.Run.Status = waiting ? RunStatus.Waiting : RunStatus.Running;
        Changed(active.Run);

        // A run that only waits does not hold back the queue.
        if (waiting)
            StartQueued();
    }

    internal static Workflow Copy(Workflow workflow)
        => JsonSerializer.Deserialize<Workflow>(JsonSerializer.Serialize(workflow))!;

    private ActiveRun? Active(string runId)
    {
        lock (_gate)
            return _active.GetValueOrDefault(runId);
    }

    private static string Describe(RunTrigger trigger, string detail) => trigger switch
    {
        RunTrigger.Manual => "by hand",
        RunTrigger.Rest => "from the REST API",
        RunTrigger.Mqtt => $"by a message on {detail}",
        RunTrigger.Workflow => $"by run {detail}",
        RunTrigger.Schedule => $"on its schedule, for {detail}",
        RunTrigger.Webhook => $"by a request to {detail}",
        RunTrigger.Error => $"because run {detail} failed",
        RunTrigger.Branch => $"as a branch of run {detail}",
        _ => trigger.ToString(),
    } + (trigger is RunTrigger.Manual or RunTrigger.Rest && detail.Length > 0 ? $" ({detail})" : "");

    // Starts queued runs while fewer than the most allowed are running.
    private void StartQueued()
    {
        if (_disposed)
            return;
        while (true)
        {
            ActiveRun next;
            lock (_gate)
            {
                var running = _active.Values.Count(a => a.Executing is not null && a.Run.Status == RunStatus.Running);
                if (running >= Settings.MaxRunning)
                    return;
                var queued = _active.Values.Where(a => a.Executing is null && !a.PauseRequested && a.Run.Status == RunStatus.Queued)
                    .OrderBy(a => a.Run.Started).FirstOrDefault();
                if (queued is null)
                    return;
                next = queued;
                next.Run.Status = RunStatus.Running;
                next.Executing = Task.CompletedTask;
            }

            Changed(next.Run);
            next.Executing = ExecuteAsync(next);
        }
    }

    private async Task ExecuteAsync(ActiveRun active)
    {
        // Lets Start return first, so its caller has the run before any step happens.
        await Task.Yield();

        var run = active.Run;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(active.Kill.Token);

        // A short run's time is counted while it runs: not while it waits in the queue or is paused.
        if (run.Kind == WorkflowKind.Short)
        {
            active.Limit = limit;
            active.TimeLeft ??= TimeSpan.FromSeconds(Settings.ShortTimeLimitSeconds);
            active.StartClock(_now());
        }

        var cancel = limit.Token;
        var context = new StepContext(this, active, cancel);
        try
        {
            while (true)
            {
                await context.WaitWhilePausedAsync();
                var step = run.Workflow.Steps.FirstOrDefault(s => s.Id == run.CurrentStep)
                           ?? throw new WorkflowException($"The workflow has no step {run.CurrentStep}.");

                // A branch stops where the branches meet; its parallel step goes on from there.
                if (run.Branch is not null && step.Type == StepType.Join)
                {
                    run.JoinedAt = step.Id;
                    run.Output = JsonText.Clone(run.Data[Templates.Last]);
                    AddLog(run, $"Reached {step.Name}, where the branches meet.");
                    Finish(active, RunStatus.Completed, null);
                    return;
                }

                if (++run.StepCount > Settings.MaxSteps)
                    throw new WorkflowException($"It took more than the {Settings.MaxSteps} steps a run may take, so it may be going round a loop with no way out. The limit is on Settings / Workflow Settings.");
                var record = new StepRecord { StepId = step.Id, Name = step.Name, Type = step.Type, Status = StepStatus.Running, Started = _now() };
                run.Steps.Add(record);
                if (run.Steps.Count > WorkflowRun.KeptSteps)
                    run.Steps.RemoveRange(0, run.Steps.Count - WorkflowRun.KeptSteps);
                Changed(run);

                string output;
                string? nextStep = null;
                try
                {
                    var result = await StepRunner.RunAsync(step, context);
                    output = result.Output;
                    nextStep = result.NextStep;
                    record.Status = StepStatus.Done;
                    record.Result = result.Value?.DeepClone();
                    if (result.KeepsResult)
                    {
                        run.Data[Templates.Last] = result.Value?.DeepClone();
                        if (step.ResultVariable.Trim() is { Length: > 0 } variable)
                            context.Vars[variable] = result.Value?.DeepClone();
                    }
                }
                catch (Exception e) when (!cancel.IsCancellationRequested && e is not OperationCanceledException)
                {
                    output = e is StepFailure failure ? failure.Output : StepOutputs.Error;
                    record.Status = StepStatus.Failed;
                    record.Error = e is StepFailure or WorkflowException ? e.Message : $"{e.GetType().Name}: {e.Message}";
                    AddLog(run, $"{step.Name}: {record.Error}", isError: true);
                }

                record.Output = output;
                record.Ended = _now();
                run.WaitUntil = null;
                run.Deadline = null;
                run.EscalateAt = null;
                run.WaitingOn = null;
                run.PendingInput = null;

                // A step that failed is tried again as many times as it asks, after its delay.
                if (record.Status == StepStatus.Failed && output == StepOutputs.Error && run.Attempt < step.Retries)
                {
                    run.Attempt++;
                    var delay = Durations.TryParse(Templates.Render(step.RetryDelay, run.Data), out var wait) ? wait : TimeSpan.FromSeconds(5);
                    AddLog(run, $"{step.Name}: trying again in {Durations.Text(delay)}, retry {run.Attempt} of {step.Retries}.");
                    await context.DelayUntilAsync(_now() + delay);
                    run.WaitUntil = null;
                    continue;
                }

                run.Attempt = 0;

                if (step.Type == StepType.End)
                {
                    run.Output = record.Result;
                    Finish(active, RunStatus.Completed, null);
                    return;
                }

                if (step.Type == StepType.Fail && record.Status == StepStatus.Done)
                {
                    // Marked as failed, so the run shows where it failed.
                    var reason = Templates.AsText(record.Result) is { Length: > 0 } text ? text : $"{step.Name} failed the run.";
                    record.Status = StepStatus.Failed;
                    record.Error = reason;
                    record.Output = null;
                    Finish(active, RunStatus.Failed, reason);
                    return;
                }

                var next = record.Status == StepStatus.Done && nextStep is not null
                    ? nextStep
                    : run.Workflow.Links.FirstOrDefault(l => l.From == step.Id && l.Output == output)?.To;
                if (next is null)
                {
                    // A step with nothing after it ends the run; one that failed with nowhere to go fails it.
                    if (record.Status == StepStatus.Failed)
                        Finish(active, RunStatus.Failed, $"{step.Name}: {record.Error}");
                    else
                    {
                        run.Output = JsonText.Clone(run.Data[Templates.Last]);
                        Finish(active, RunStatus.Completed, null);
                    }

                    return;
                }

                run.CurrentStep = next;
                if (run.Kind == WorkflowKind.Long)
                    Store.Save(run);
                Changed(run);
            }
        }
        catch (OperationCanceledException) when (active.ShuttingDown)
        {
            // Long runs were written down as they were, to carry on when Joktan starts again. A short one is
            // cut off, and marked so, so that a long run waiting on it starts it again.
            if (run.Kind == WorkflowKind.Short)
            {
                run.Interrupted = true;
                Finish(active, RunStatus.Failed, "Joktan closed while it was running.");
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (active.Kill.IsCancellationRequested)
                Finish(active, RunStatus.Killed, null);
            else
                Finish(active, RunStatus.Failed, $"It ran for longer than the {Durations.Text(TimeSpan.FromSeconds(Settings.ShortTimeLimitSeconds))} short workflows are allowed. Make it a long workflow, or change the limit on Settings / Workflow Settings.");
        }
        catch (WorkflowException e)
        {
            Finish(active, RunStatus.Failed, e.Message);
        }
        finally
        {
            active.Limit = null;
        }
    }

    private void Finish(ActiveRun active, RunStatus status, string? error)
    {
        var run = active.Run;
        if (run.Status.IsFinished())
            return;
        run.Status = status;
        run.Error = error;
        run.Ended = _now();
        run.PendingInput = null;
        run.WaitUntil = null;
        run.Deadline = null;
        run.EscalateAt = null;
        run.WaitingOn = null;
        if (run.Steps.LastOrDefault() is { Status: StepStatus.Running } last)
        {
            last.Status = StepStatus.Failed;
            last.Error = status == RunStatus.Killed ? "Killed." : error;
            last.Ended = run.Ended;
        }

        AddLog(run, status switch
        {
            RunStatus.Completed => "Completed.",
            RunStatus.Killed => "Killed.",
            _ => $"Failed: {error}",
        }, status == RunStatus.Failed);
        _log.Log(status == RunStatus.Failed ? LogLevel.Warning : LogLevel.Information,
            "Workflow {Workflow}, run #{Serial} ({Run}): {Status} {Error}", run.WorkflowName, run.Serial, run.Id, status, error ?? "");

        lock (_gate)
            _active.Remove(run.Id);
        Store.Save(run);
        Changed(run);
        Prune();
        active.Finished.TrySetResult(run);
        StartQueued();
        if (status == RunStatus.Failed)
            StartErrorWorkflow(run);
    }

    /// <summary>
    /// Starts the error workflow of a run that failed: its workflow's own, or the one on Settings / Workflow Settings.
    /// Not for a branch, whose run fails in turn, nor a run cut off by Joktan closing, nor a run of an error workflow,
    /// so that one failing never starts another.
    /// </summary>
    private void StartErrorWorkflow(WorkflowRun run)
    {
        if (_disposed || run.Branch is not null || run.Interrupted || run.Trigger == RunTrigger.Error)
            return;
        var id = run.Workflow.ErrorWorkflow.Trim() is { Length: > 0 } own ? own : Settings.ErrorWorkflow.Trim();
        if (id.Length == 0 || id == run.WorkflowId)
            return;
        if (Library.Find(id) is not { } handler)
        {
            _log.LogWarning("The error workflow {ErrorWorkflow} of {Workflow} is not there any more", id, run.WorkflowName);
            return;
        }

        var failed = run.Steps.LastOrDefault(s => s.Status == StepStatus.Failed);
        var input = new JsonObject
        {
            ["workflow"] = new JsonObject { ["id"] = run.WorkflowId, ["name"] = run.WorkflowName },
            ["run"] = new JsonObject
            {
                ["id"] = run.Id, ["serial"] = run.Serial, ["trigger"] = run.Trigger.ToString(),
                ["started"] = run.Started.ToString("O"), ["ended"] = run.Ended?.ToString("O"),
            },
            ["error"] = run.Error,
            ["step"] = failed is null ? null : new JsonObject { ["id"] = failed.StepId, ["name"] = failed.Name, ["error"] = failed.Error },
            ["input"] = run.Data[Templates.Input]?.DeepClone(),
        };
        try
        {
            var started = Start(handler, input, RunTrigger.Error, run.Id);
            AddLog(run, $"Started the error workflow {handler.Name}, run #{started.Serial}.");
            Store.Save(run);
            Changed(run);
        }
        catch (WorkflowException e)
        {
            AddLog(run, $"The error workflow {handler.Name} could not start: {e.Message}", isError: true);
            Store.Save(run);
        }
    }

    // Keeps the number of finished runs the settings ask for, dropping the oldest.
    private void Prune()
    {
        List<WorkflowRun> dropped;
        lock (_gate)
        {
            var finished = _runs.Where(r => r.Status.IsFinished()).ToList();
            dropped = finished.Take(Math.Max(0, finished.Count - Settings.KeepRuns)).ToList();
            foreach (var run in dropped)
                _runs.Remove(run);
        }

        foreach (var run in dropped)
            Store.Delete(run.Id);
        if (dropped.Count > 0)
            RunsChanged?.Invoke();
    }
}

/// <summary>What the engine keeps about a run that has not finished.</summary>
internal sealed class ActiveRun(WorkflowRun run)
{
    public WorkflowRun Run { get; } = run;

    /// <summary>The steps being run, or null while the run waits in the queue or paused after a restart.</summary>
    public Task? Executing { get; set; }

    public CancellationTokenSource Kill { get; } = new();

    /// <summary>Cancelled when a pause is asked for, which interrupts a delay.</summary>
    public CancellationTokenSource Pausing { get; set; } = new();

    public TaskCompletionSource Resumed { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool PauseRequested { get; set; }

    /// <summary>What the run goes back to when resumed.</summary>
    public RunStatus StatusBeforePause { get; set; } = RunStatus.Running;

    public bool ShuttingDown { get; set; }

    /// <summary>For a short run, what is cancelled when its time runs out, and how much time it has left.</summary>
    public CancellationTokenSource? Limit { get; set; }

    public TimeSpan? TimeLeft { get; set; }

    private DateTime? _clockStarted;

    public void StartClock(DateTime now)
    {
        if (Limit is null || TimeLeft is not { } left)
            return;
        _clockStarted = now;
        Limit.CancelAfter(left > TimeSpan.Zero ? left : TimeSpan.Zero);
    }

    public void StopClock(DateTime now)
    {
        if (Limit is null || _clockStarted is not { } started)
            return;
        TimeLeft -= now - started;
        _clockStarted = null;
        Limit.CancelAfter(Timeout.InfiniteTimeSpan);
    }

    /// <summary>Set while a user input step waits for its answer.</summary>
    public TaskCompletionSource<JsonObject>? Answer { get; set; }

    public TaskCompletionSource<WorkflowRun> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
