using System.Collections.Generic;
using System.Linq;
using Joktan.Workflows.Model;

namespace Joktan.Agent.Tools;

/// <summary>What a workflow holds, for an agent to write one for workflow_save without seeing the designer.</summary>
public static class WorkflowFormat
{
    // The settings of a step that each type uses, besides Id, Type, Name, Description, X, Y, ResultVariable and Labels.
    private static IReadOnlyList<string> Settings(StepType type) => type switch
    {
        StepType.Start => ["Starts the run; the workflow's Triggers say what starts it. Exactly one per workflow."],
        StepType.End => ["Value: the run's output, JSON or a path such as {{vars.order}}; empty for the last result."],
        StepType.SetVariable => ["Variable: the name, read as {{vars.<name>}}", "Value: JSON, text or a path", "MergeValue: merge a JSON object into it"],
        StepType.Condition => ["Left, Operator, Right: Operator is Equals, NotEquals, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, Contains, StartsWith, IsEmpty, IsNotEmpty, or CSharp (Left is C# giving true or false)"],
        StepType.Delay => ["Duration: such as 30s, 5m, 2h or 1d, or a path"],
        StepType.Http => ["RequestPath: a request saved in the API Client, or empty to use Method, Url, Headers ([{Name, Value}]) and Body", "BodyMode: Keep, Replace or Merge, for a saved request", "FailOnErrorStatus"],
        StepType.Grpc => ["RequestPath, or Address, Service and Rpc", "Body: the message as JSON", "BodyMode"],
        StepType.MqttPublish => ["Client: an MQTT client's name", "MessageFile: one of the client's folder messages (mqtt_folder_messages), or empty to send Topic and Body", "Topic: empty keeps a folder message's own", "BodyMode: Keep, Replace or Merge (Body, a JSON object, merged into the folder message's)", "Body, Qos, Retain"],
        StepType.MqttWait => ["Client", "Topic: the filter to wait on, with + and #", "Duration: how long before going out by timeout", "Left, Operator, Right: a test on {{message.payload...}} the message must pass; no Left for the first message"],
        StepType.Script => ["Language: CSharp, JavaScript or Go", "Code: C# sees Input, Vars, Last, Log(\"...\"); JavaScript input, vars, last, log(); Go reads {input, vars, last} on stdin and writes JSON. What it returns (Go: prints) is the result.", "Libraries", "WorkingFolder"],
        StepType.CallWorkflow => ["Workflow: the id of the workflow to start", "Input: its input, JSON with paths", "Wait: wait for its output, or carry on at once"],
        StepType.UserInput => ["Message: what the person is asked", "Fields: [{Name, Label, Default}] asked for besides a page's", "PageId: a page of workflow_pages shown on the website; empty for the message and a box for each field", "AssignedTo: {Kind: Role, Id: role name, Name: role name} from workflow_assignees", "Duration: how long it waits before going out by timeout; empty for no limit", "Metadata: [{Name, Value}] sent with the question, constants or paths", "Escalate, EscalateAfter (such as 30m), EscalateTo: {Kind, Id, Name}", "The result is an object of the fields answered."],
        StepType.Log => ["Message: the line written in the run's log, with paths"],
        StepType.Fail => ["Message: why the run failed, with paths"],
        StepType.ForEach => ["Items: a list, an object (its entries as {key, value}), or a number of times; JSON or a path", "ItemVariable, IndexVariable", "Link loop to the steps done each time round, and lead them back to the for each; done goes on after the last."],
        StepType.While => ["Left, Operator, Right: goes round while it holds", "IndexVariable"],
        StepType.Switch => ["Left: the value", "Cases: the values compared with it, in order; each is an output case:<value>, then default"],
        StepType.Parallel => ["Cases: the branches' names; each is an output case:<name>, linked to the branch's first step",
            "Every branch runs at the same time as a run of its own, with a copy of the data, until it reaches a Join; lead them all to the same one",
            "The run goes on from that Join with last = {<branch>: what it gave}, and the variables the branches changed",
            "A branch that fails stops the others, and the step goes out by error"],
        StepType.Join => ["Where a parallel step's branches meet; nothing to set. Link its next to what follows the branches."],
        StepType.DatabaseRead => [..DatabaseCommon, "Filters: [{Column, Operator, Value}], all must pass", "OrderBy, Descending, Limit (0 for all)", "FirstRowOnly"],
        StepType.DatabaseInsert => [..DatabaseCommon, "ManyRows with Items and ItemVariable: a row for each item", "FirstRowOnly"],
        StepType.DatabaseUpdate => [..DatabaseCommon, "Filters: required, each with a value; never every row", "FirstRowOnly"],
        StepType.DatabaseDelete => ["Database, Schema, Table", "Filters: required, each with a value; never every row", "Columns: only to put values of the rows deleted in variables", "FirstRowOnly"],
        StepType.DatabaseUpsert => [..DatabaseCommon, "Columns with Match true: the key, which needs a unique index or primary key", "ManyRows with Items and ItemVariable", "FirstRowOnly"],
        _ => [],
    };

    private static readonly string[] DatabaseCommon =
    [
        "Database: an id from database_list", "Schema (public), Table: database_tables lists them",
        "Columns: [{Column, SetsValue, Value, Variable, AllRows, Match}]; Value may use paths, Variable puts the column's value (or with AllRows a list) in vars",
    ];

    public static object Describe() => new
    {
        Workflow = new Dictionary<string, string>
        {
            ["Name"] = "Unique among workflows.",
            ["Kind"] = "Short (in memory, with a time limit) or Long (saved after every step, survives a restart; use it when people answer questions).",
            ["Triggers"] = "{Rest, Mqtt, MqttClient, MqttTopic (a filter with + and #), Scheduled, Schedule, ScheduleInput, Webhook, WebhookPath (such as orders/new, " +
                           "called as POST /hooks/orders/new on the REST API's server, with the JSON body as the input; the request is {{run.request}}), WebhookNeedsKey, WebhookWait}. An MQTT trigger's message payload, which must be JSON, is the run's input itself, read as {{input.orderId}}, not {{input.payload.orderId}}; its topic is {{run.topic}}.",
            ["ErrorWorkflow"] = "The id of a workflow started when a run fails, with {workflow, run, error, step, input} as its input; empty for the one on Settings / Workflow Settings.",
            ["Samples"] = "[{Name, Input}], inputs as JSON text to start it by hand with.",
            ["Globals"] = "[{Name, Value, Required, Description}], set when a run starts; a value such as {{input.orderId}}.",
            ["Steps"] = "The steps below, each with a unique Id. X and Y are its place on the canvas; leave them out to have them laid out.",
            ["Links"] = "[{From: step id, Output, To: step id}]: which step follows which output.",
            ["Lanes"] = "Optional swimlanes [{Name, Height}] from LanesTop; a step is in the lane its Y puts it in.",
        },
        Paths = "Text settings fill in {{input...}}, {{vars...}}, {{last...}} (the step before's result), {{run.id}}, {{run.serial}} and, for an MQTT trigger, {{run.topic}}, with dots and [index]. A setting that is a path alone keeps its JSON.",
        Common = "Every step: Id, Type, Name, Description, ResultVariable (vars.<name> gets its result, which is always in last too), Retries and RetryDelay, " +
                 "Labels ({output: text} written on its links, such as {\"true\": \"Approved\"}). A step without a link on error makes the run fail when it fails.",
        Steps = StepTypes.All.Select(t => new { Type = t.ToString(), Group = StepTypes.GroupOf(t).ToString(), Does = t.Tip(), Outputs = StepTypes.Outputs(t), Settings = Settings(t) }).ToList(),
    };
}
