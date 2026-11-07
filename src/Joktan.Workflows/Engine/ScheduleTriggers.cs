using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Joktan.Core;
using Joktan.Mqtt;
using Joktan.Workflows.Model;
using Microsoft.Extensions.Logging;

namespace Joktan.Workflows.Engine;

/// <summary>The schedules workflows start with.</summary>
public static class WorkflowSchedules
{
    /// <summary>What a schedule is when it is first switched on: every minute.</summary>
    public static readonly MessageSchedule Default = new() { Type = ScheduleType.Interval, Every = 1, Unit = IntervalUnit.Minutes };

    /// <summary>The schedule as it is written down, to tell whether it has changed.</summary>
    public static string Key(MessageSchedule schedule) => MessageSettings.ScheduleToJson(schedule).ToJsonString();
}

/// <summary>
/// Starts workflows on their schedules, the same schedules a folder message on Dev Tools / Brokers / MQTT is sent on.
/// A schedule starts from when Joktan starts or the schedule is switched on or changed; times already past then are
/// skipped rather than run late.
/// </summary>
internal sealed class ScheduleTriggers : IDisposable
{
    private readonly WorkflowEngine _engine;
    private readonly object _gate = new();
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _problems = new(StringComparer.Ordinal);
    private Timer? _timer;
    private bool _disposed;

    public ScheduleTriggers(WorkflowEngine engine)
    {
        _engine = engine;
        engine.Library.Changed += Refresh;
    }

    /// <summary>Why a schedule started no run, by workflow, such as a sample input that is not JSON.</summary>
    public IReadOnlyDictionary<string, string> Problems
    {
        get
        {
            lock (_gate)
                return new Dictionary<string, string>(_problems);
        }
    }

    /// <summary>When the workflow's schedule starts it next, or null when it is not scheduled or has no more runs.</summary>
    public DateTime? NextRun(string workflowId)
    {
        lock (_gate)
            return _slots.GetValueOrDefault(workflowId)?.Next;
    }

    /// <summary>Whether the workflow's schedule is running: switched on, with schedules switched on in the settings.</summary>
    public bool IsActive(string workflowId)
    {
        lock (_gate)
            return _slots.ContainsKey(workflowId);
    }

    /// <summary>Follows the workflows as they are now: an unchanged schedule keeps its place, a new or changed one starts from now.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            var now = _engine.Now;
            var wanted = _engine.Settings.ScheduleTriggers
                ? _engine.Library.All.Where(w => w.Triggers is { Scheduled: true, Schedule.Type: not ScheduleType.Manual }).ToList()
                : [];
            var ids = wanted.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in _slots.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                _slots.Remove(gone);
                _problems.Remove(gone);
            }

            foreach (var workflow in wanted)
            {
                var key = WorkflowSchedules.Key(workflow.Triggers.Schedule);
                if (_slots.TryGetValue(workflow.Id, out var slot) && slot.Key == key)
                    continue;
                _slots[workflow.Id] = new Slot(key, workflow.Triggers.Schedule, workflow.Triggers.Schedule.FirstDue(now));
                _problems.Remove(workflow.Id);
            }

            if (_slots.Count > 0)
                _timer ??= new Timer(_ => _engine.Post(Check), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            else
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }

    /// <summary>Starts the runs that are due, and moves each schedule on to its next time.</summary>
    public void Check()
    {
        var due = new List<(string Id, DateTime At)>();
        lock (_gate)
        {
            if (_disposed)
                return;

            var now = _engine.Now;
            foreach (var (id, slot) in _slots)
            {
                if (slot.Next is not { } next || next > now)
                    continue;

                due.Add((id, next));
                slot.Runs++;
                slot.Next = slot.Runs >= slot.Schedule.MaxSends ? null : slot.Schedule.NextDue(next, now);
            }
        }

        foreach (var (id, at) in due)
            Start(id, at);
    }

    private void Start(string workflowId, DateTime at)
    {
        if (_engine.Library.Find(workflowId) is not { Triggers.Scheduled: true } workflow)
            return;

        string? problem = null;
        try
        {
            var text = workflow.Triggers.ScheduleInput.Trim() is { Length: > 0 } name
                ? workflow.Samples.FirstOrDefault(s => s.Name == name)?.Input
                  ?? throw new WorkflowException($"There is no sample input called {name}.")
                : "{}";
            var input = text.Trim().Length == 0 ? new JsonObject() : JsonNode.Parse(text);
            _engine.Start(workflow, input, RunTrigger.Schedule, at.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch (JsonException e)
        {
            problem = $"The sample input {workflow.Triggers.ScheduleInput.Trim()} is not JSON, so the schedule started no run: {e.Message}";
        }
        catch (Exception e) when (e is WorkflowException or ObjectDisposedException)
        {
            problem = e.Message;
        }

        lock (_gate)
        {
            if (problem is null)
                _problems.Remove(workflowId);
            else
                _problems[workflowId] = problem;
        }

        if (problem is not null)
            AppLog.For("Workflows").LogWarning("The schedule could not start {Workflow}: {Error}", workflow.Name, problem);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            _slots.Clear();
        }

        _engine.Library.Changed -= Refresh;
    }

    private sealed class Slot(string key, MessageSchedule schedule, DateTime? next)
    {
        public string Key { get; } = key;

        public MessageSchedule Schedule { get; } = schedule;

        public DateTime? Next { get; set; } = next;

        public int Runs { get; set; }
    }
}
