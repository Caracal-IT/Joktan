using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Joktan.Agent.Mcp;
using Joktan.Core;
using Joktan.Mqtt;
using Joktan.Workflows.Controls;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class WorkflowTools
{
    private static readonly ToolParameter RunId = P.Text("run", "The run's id or serial number, from workflow_start or workflow_runs.");

    private static readonly ToolParameter WorkflowName = P.Text("workflow", "The workflow's name or id, from workflow_list.");

    private static readonly ToolParameter PageName = P.Text("page", "The page's name or id, from workflow_pages.");

    // Names in either case, as an agent may write them; enums are written as text by the model's own converters.
    private static readonly JsonSerializerOptions Lenient = new(JsonFormats.Compact) { PropertyNameCaseInsensitive = true };

    // How long workflow_start waits for a run, unless told otherwise.
    private const int DefaultWaitSeconds = 60;

    public static IEnumerable<McpTool> Create(WorkflowEngine engine) =>
    [
        Read("workflow_list", "List the workflows of Workflows / Designer, with whether the REST API may start them.", [], _ =>
            engine.Library.All.Select(w => new
            {
                w.Id, w.Name, w.Description, Folder = engine.Library.FolderOf(w.Id), Kind = w.Kind.ToString(), RestTrigger = w.Triggers.Rest,
                MqttTrigger = w.Triggers.Mqtt ? $"{w.Triggers.MqttClient}: {w.Triggers.MqttTopic}" : null,
                Schedule = w.Triggers.Scheduled ? MessageSettings.ScheduleToJson(w.Triggers.Schedule) : null,
                Webhook = w.Triggers.Webhook ? $"{WebhookTriggers.Root}/{WebhookTriggers.Normalize(w.Triggers.WebhookPath)}" : null,
                ErrorWorkflow = w.ErrorWorkflow.Length > 0 ? engine.Library.Find(w.ErrorWorkflow)?.Name ?? w.ErrorWorkflow : null,
                NextScheduledRun = engine.NextScheduledRun(w.Id),
                Samples = w.Samples.Select(s => new { s.Name, Input = Templates.Parse(s.Input) }).ToList(),
            }).ToList()),
        Read("workflow_get", "Read a workflow's whole definition, as workflow_save takes it: its triggers, samples, globals, steps, links and lanes.", [WorkflowName], args =>
        {
            var workflow = FindWorkflow(engine, args);
            return new { Folder = engine.Library.FolderOf(workflow.Id), Workflow = JsonSerializer.SerializeToNode(workflow, JsonFormats.Compact) };
        }),
        Read("workflow_step_types",
            "Describe what a workflow can hold: each type of step with its outputs and the settings it uses, and the other parts of a workflow. Read it before writing one for workflow_save.",
            [], _ => WorkflowFormat.Describe()),
        Change("workflow_save",
            "Make a workflow on Workflows / Designer, or change one by giving its id: the whole definition, as workflow_get gives it and workflow_step_types describes. " +
            "Steps given no place are laid out. Gives back what the designer would point out, such as steps never reached.",
            [
                P.Json("workflow", "The workflow: Name, Description, Kind (Short or Long), Triggers, Samples, Globals, Steps and Links. Leave Id out for a new one."),
                P.OptText("folder", "The folder to keep it in, such as Demo or Orders/Returns; left where it is unless given, at the top when new."),
            ], args =>
            {
                Workflow workflow;
                try
                {
                    workflow = args.Json("workflow").Deserialize<Workflow>(Lenient) ?? throw new ToolFailure("workflow is required.");
                }
                catch (JsonException e)
                {
                    throw new ToolFailure($"The workflow cannot be read: {e.Message}");
                }

                if (workflow.Name.Trim().Length == 0)
                    throw new ToolFailure("Give the workflow a name.");
                workflow.Name = workflow.Name.Trim();
                if (engine.Library.All.FirstOrDefault(w => w.Id != workflow.Id && string.Equals(w.Name, workflow.Name, StringComparison.OrdinalIgnoreCase)) is { } other)
                    throw new ToolFailure($"There is already a workflow called {other.Name}; give its Id ({other.Id}) to change it, or choose another name.");
                if (workflow.Id.Trim().Length == 0)
                    workflow.Id = WorkflowLibrary.NewId();
                workflow.Triggers ??= new();
                workflow.Triggers.WebhookPath ??= "";
                workflow.ErrorWorkflow ??= "";
                workflow.Samples ??= [];
                workflow.Globals ??= [];
                workflow.Steps ??= [];
                workflow.Links ??= [];
                workflow.Lanes ??= [];
                foreach (var step in workflow.Steps)
                {
                    if (step.Id.Trim().Length == 0)
                        step.Id = WorkflowLibrary.NewId();
                    if (step.Name.Trim().Length == 0)
                        step.Name = step.Type.Text();
                }

                if (workflow.Steps.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1) is { } twice)
                    throw new ToolFailure($"Two steps have the id {twice.Key}; each needs its own.");
                DiagramLayout.Arrange(workflow);
                engine.Library.Save(workflow, args.OptText("folder"));
                return new
                {
                    workflow.Id, workflow.Name, Folder = engine.Library.FolderOf(workflow.Id),
                    Problems = WorkflowCheck.Problems(workflow).Select(p => new { Step = workflow.Steps.FirstOrDefault(s => s.Id == p.StepId)?.Name, p.Text, Blocking = p.IsBlocking })
                        .Concat(References(engine, workflow).Select(p => new { p.Step, p.Text, Blocking = false }))
                        .ToList(),
                };
            }),
        Risky("workflow_delete", "Delete a workflow from Workflows / Designer, and its file. Its runs are kept.", [WorkflowName], args =>
        {
            var workflow = FindWorkflow(engine, args);
            engine.Library.Delete(workflow.Id);
            return "Deleted.";
        }),
        Read("workflow_assignees", "List the roles and the users of the Users page that a user input step's question can be assigned to. Assign a role: one user is bad practice.", [], _ => new
        {
            Roles = engine.Services.Assignees(AssigneeKind.Role).Select(a => new { a.Id, a.Name, a.Detail }).ToList(),
            Users = engine.Services.Assignees(AssigneeKind.User).Select(a => new { a.Id, a.Name, a.Detail }).ToList(),
        }),
        Read("workflow_pages", "List the pages of Workflows / Pages, which user input steps show on the website, with the fields each asks for and the steps that use it.", [], _ =>
            engine.Pages.All.Select(p => new
            {
                p.Id, p.Name, p.Title, Fields = p.Inputs.Select(i => i.Name).ToList(),
                UsedBy = engine.Library.All.SelectMany(w => w.Steps.Where(s => s.PageId == p.Id).Select(s => $"{w.Name} / {s.Name}")).ToList(),
            }).ToList()),
        Read("workflow_page_get", "Read a page of Workflows / Pages, as workflow_page_save takes it.", [PageName], args =>
            JsonSerializer.SerializeToNode(FindPage(engine, args), JsonFormats.Compact)),
        Change("workflow_page_save",
            "Make a page on Workflows / Pages, or change one by giving its id. Give a user input step its Id as PageId to show it on the website. " +
            "Items, top to bottom, each have a Type: Heading, Text or Value (shown, with Text that may use {{paths}} such as {{vars.order.total}}), Divider, " +
            "or TextBox, TextArea, Number, CheckBox, DropDown or Date (filled in, each with a Name that becomes a field of the answer, and Label, Default, Placeholder, Help, Required; " +
            "a DropDown's Options are a list, or one path to a list).",
            [P.Json("page", "The page: Name, Title, Description, SubmitText and Items. Leave Id out for a new one.")], args =>
            {
                WorkflowPage page;
                try
                {
                    page = args.Json("page").Deserialize<WorkflowPage>(Lenient) ?? throw new ToolFailure("page is required.");
                }
                catch (JsonException e)
                {
                    throw new ToolFailure($"The page cannot be read: {e.Message}");
                }

                if (page.Name.Trim().Length == 0)
                    throw new ToolFailure("Give the page a name.");
                page.Name = page.Name.Trim();
                if (engine.Pages.All.FirstOrDefault(p => p.Id != page.Id && string.Equals(p.Name, page.Name, StringComparison.OrdinalIgnoreCase)) is { } other)
                    throw new ToolFailure($"There is already a page called {other.Name}; give its Id ({other.Id}) to change it, or choose another name.");
                if (page.Id.Trim().Length == 0)
                    page.Id = WorkflowLibrary.NewId();
                page.Items ??= [];
                foreach (var item in page.Items)
                {
                    item.Options ??= [];
                    if (item.Id.Trim().Length == 0)
                        item.Id = WorkflowLibrary.NewId();
                }

                engine.Pages.Save(page);
                return new { page.Id, page.Name, Problem = PageBuilderViewModel.ProblemOf(page) };
            }),
        Risky("workflow_page_delete", "Delete a page of Workflows / Pages. Steps that show it go back to their own message and fields.", [PageName], args =>
        {
            engine.Pages.Delete(FindPage(engine, args).Id);
            return "Deleted.";
        }),
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

    private static Workflow FindWorkflow(WorkflowEngine engine, ToolArgs args)
        => engine.Library.Find(args.Text("workflow")) ?? throw new ToolFailure($"There is no workflow called {args.Text("workflow")}. workflow_list lists them.");

    private static WorkflowPage FindPage(WorkflowEngine engine, ToolArgs args)
        => engine.Pages.Find(args.Text("page")) ?? throw new ToolFailure($"There is no page called {args.Text("page")}. workflow_pages lists them.");

    // What a step names that is not there, which the designer shows as an empty choice.
    private static IEnumerable<(string? Step, string Text)> References(WorkflowEngine engine, Workflow workflow)
    {
        var clients = engine.Services.MqttClients;
        if (workflow.Triggers.Mqtt && workflow.Triggers.MqttClient.Trim() is { Length: > 0 } trigger && !clients.Contains(trigger))
            yield return (null, $"The MQTT trigger's client {trigger} is not on Dev Tools / Brokers / MQTT.");
        if (workflow.ErrorWorkflow.Trim() is { Length: > 0 } handler && engine.Library.Find(handler) is null)
            yield return (null, $"There is no workflow {handler} to start when a run fails.");
        var databases = engine.Services.Databases().Select(d => d.Id).ToHashSet();
        foreach (var step in workflow.Steps)
        {
            if (step.Type is StepType.MqttPublish or StepType.MqttWait && step.Client.Trim().Length > 0 && !clients.Contains(step.Client))
                yield return (step.Name, $"There is no MQTT client called {step.Client}.");
            if (step.Type == StepType.MqttPublish && step.MessageFile.Trim().Length > 0 && !engine.Services.FolderMessages(step.Client).Contains(step.MessageFile))
                yield return (step.Name, $"{step.Client} has no folder message {step.MessageFile}; mqtt_folder_messages lists them.");
            if (StepTypes.IsDatabase(step.Type) && !databases.Contains(step.Database))
                yield return (step.Name, "Database must be the id of a database from database_list.");
            if (step.Type == StepType.UserInput && step.PageId.Trim().Length > 0 && engine.Pages.Find(step.PageId) is null)
                yield return (step.Name, $"There is no page {step.PageId}; workflow_pages lists them.");
            if (step.Type == StepType.CallWorkflow && step.Workflow != workflow.Id && engine.Library.Find(step.Workflow) is null)
                yield return (step.Name, $"There is no workflow {step.Workflow} to call.");
        }
    }

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
                question.Id, question.Message, Fields = question.Fields.Select(f => new { f.Name, f.Label, Offered = f.Default }), question.Metadata, question.Until,
                question.Asked, Page = question.Page?.Name,
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
