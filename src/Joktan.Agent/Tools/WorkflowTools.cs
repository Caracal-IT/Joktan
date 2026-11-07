using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Mqtt;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class WorkflowTools
{
    private static readonly ToolParameter RunId = P.Text("run", "The run's id or serial number, from workflow_start or workflow_runs.");

    // How long workflow_start waits for a run, unless told otherwise.
    private const int DefaultWaitSeconds = 60;

    public static IEnumerable<McpTool> Create(WorkflowEngine engine) =>
    [
        Read("workflow_list", "List the workflows of Workflows / Designer, with whether the REST API may start them.", [], _ =>
            engine.Library.All.Select(w => new
            {
                w.Id, w.Name, w.Description, Kind = w.Kind.ToString(), RestTrigger = w.Triggers.Rest,
                MqttTrigger = w.Triggers.Mqtt ? $"{w.Triggers.MqttClient}: {w.Triggers.MqttTopic}" : null,
                Schedule = w.Triggers.Scheduled ? MessageSettings.ScheduleToJson(w.Triggers.Schedule) : null,
                NextScheduledRun = engine.NextScheduledRun(w.Id),
                Samples = w.Samples.Select(s => new { s.Name, Input = Templates.Parse(s.Input) }).ToList(),
            }).ToList()),
        RiskyAsync("workflow_start",
            "Start a workflow whose REST trigger is switched on. With wait, answer when it has finished, with its output, or when the wait runs out, with the run as it is then.",
            [
                P.Text("workflow", "The workflow's name or id."),
                new ToolParameter("input", "object", "The run's input, as a JSON object.", false),
                P.OptFlag("wait", "Whether to wait for the run to finish. Off unless given."),
                P.OptNumber("wait_seconds", $"How long to wait, {DefaultWaitSeconds} unless given."),
            ], async args =>
            {
                var workflow = engine.Library.Find(args.Text("workflow")) ?? throw new ToolFailure($"There is no workflow called {args.Text("workflow")}. workflow_list lists them.");
                if (!workflow.Triggers.Rest)
                    throw new ToolFailure($"{workflow.Name} cannot be started from outside: switch on its REST trigger on Workflows / Designer.");
                WorkflowRun run;
                try
                {
                    run = engine.Start(workflow, Input(args), RunTrigger.Rest);
                }
                catch (WorkflowException e)
                {
                    throw new ToolFailure(e.Message);
                }

                if (args.Flag("wait") != true)
                    return Describe(run, false);

                var wait = TimeSpan.FromSeconds(Math.Clamp(args.Whole("wait_seconds") ?? DefaultWaitSeconds, 1, 3600));
                try
                {
                    await engine.WhenFinished(run.Id).WaitAsync(wait);
                }
                catch (TimeoutException)
                {
                    // Given back as it is, still under way.
                }

                return Describe(run, false);
            }),
        Read("workflow_runs", "List runs, newest first.",
            [
                P.OptText("workflow", "Only the runs of this workflow, by name or id."),
                P.OptChoice("status", "Only runs with this status.", Enum.GetNames<RunStatus>()),
                P.OptNumber("limit", "How many; 20 unless given."),
            ], args =>
            {
                var runs = engine.Runs.AsEnumerable().Reverse();
                if (args.OptText("workflow") is { } name)
                {
                    var workflow = engine.Library.Find(name);
                    runs = runs.Where(r => r.WorkflowId == workflow?.Id || string.Equals(r.WorkflowName, name, StringComparison.OrdinalIgnoreCase));
                }

                if (args.OptText("status") is { } status)
                    runs = runs.Where(r => r.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase));
                return runs.Take(args.Whole("limit") ?? 20).Select(r => Describe(r, false)).ToList();
            }),
        Read("workflow_run", "Read a run: its status, the steps it took, its variables, its output or why it failed, and any question waiting for an answer.", [RunId], args =>
            Describe(Find(engine, args), true)),
        Change("workflow_pause", "Pause a run. It stops before its next step, or at once in a delay.", [RunId], args =>
            engine.Pause(Find(engine, args).Id) ? "Paused." : throw new ToolFailure("It cannot be paused now.")),
        Change("workflow_resume", "Resume a paused run.", [RunId], args =>
            engine.Resume(Find(engine, args).Id) ? "Resumed." : throw new ToolFailure("It is not paused.")),
        Risky("workflow_kill", "Stop a run at once, and any workflow it is waiting on.", [RunId], args =>
            engine.Kill(Find(engine, args).Id) ? "Killed." : throw new ToolFailure("It has already finished.")),
        Risky("workflow_answer", "Answer the question of a run waiting for input. Fields not given keep the values offered.",
            [RunId, P.Map("values", "The answer: an object of field names and values.")], args =>
            {
                var run = Find(engine, args);
                try
                {
                    engine.Answer(run.Id, args.Map("values").ToDictionary(p => p.Key, p => p.Value));
                }
                catch (WorkflowException e)
                {
                    throw new ToolFailure(e.Message);
                }

                return "Answered.";
            }),
    ];

    private static WorkflowRun Find(WorkflowEngine engine, ToolArgs args)
        => engine.Find(args.Text("run")) ?? throw new ToolFailure($"There is no run {args.Text("run")}. workflow_runs lists them.");

    // An object as it was sent, or JSON in text, as a query gives it.
    private static JsonNode? Input(ToolArgs args)
    {
        if (args.Raw.ValueKind != JsonValueKind.Object || !args.Raw.TryGetProperty("input", out var input) || input.ValueKind == JsonValueKind.Null)
            return new JsonObject();
        if (input.ValueKind != JsonValueKind.String)
            return JsonNode.Parse(input.GetRawText());
        try
        {
            return JsonNode.Parse(input.GetString()!);
        }
        catch (JsonException)
        {
            throw new ToolFailure("input must be a JSON object.");
        }
    }

    private static object Describe(WorkflowRun run, bool full) => new
    {
        run.Id,
        run.Serial,
        Workflow = run.WorkflowName,
        Status = run.Status.ToString(),
        Trigger = run.Trigger.ToString(),
        run.Started,
        run.Ended,
        run.Output,
        run.Error,
        run.ParentRunId,
        Question = run.PendingInput is { } question
            ? new
            {
                question.Message, Fields = question.Fields.Select(f => new { f.Name, f.Label, Offered = f.Default }), question.Metadata, question.Until,
                AssignedTo = new { Kind = question.AssignedTo.Kind.ToString(), question.AssignedTo.Id, question.AssignedTo.Name },
                question.Escalated, question.EscalateAt,
                EscalateTo = question.EscalateTo is { } to ? new { Kind = to.Kind.ToString(), to.Id, to.Name } : null,
            }
            : null,
        Steps = full ? run.Steps.Select(s => new { s.Name, Type = s.Type.ToString(), Status = s.Status.ToString(), s.Output, s.Error, s.Started, s.Ended }) : null,
        Variables = full ? run.Data[Templates.Vars] : null,
        Log = full ? run.Log.TakeLast(50).Select(l => $"{l.Time:HH:mm:ss} {l.Text}") : null,
    };
}
