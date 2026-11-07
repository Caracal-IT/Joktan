using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Joktan.Mqtt;
using Joktan.Workflows.Engine;

namespace Joktan.Workflows.Model;

/// <summary>How a workflow is run: short ones finish while you wait, long ones may take days.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkflowKind>))]
public enum WorkflowKind
{
    /// <summary>Kept in memory, stopped after the time limit on Settings / Workflow Settings, and not carried over a restart.</summary>
    Short,

    /// <summary>Written down after every step, with no time limit, and carried on after Joktan starts again.</summary>
    Long,
}

/// <summary>
/// A workflow, kept as a <c>.workflow.json</c> file of its own: steps placed on a canvas, and the links between
/// their outputs that say which step comes next.
/// </summary>
public sealed class Workflow
{
    /// <summary>What the file and the calls know the workflow by; it never changes, unlike the name.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public WorkflowKind Kind { get; set; } = WorkflowKind.Short;

    public WorkflowTriggers Triggers { get; set; } = new();

    /// <summary>Inputs to start a run by hand with, each with a name to choose it by.</summary>
    public List<NamedInput> Samples { get; set; } = [];

    /// <summary>The one sample input of workflows saved before there could be several; read, and made the first sample.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SampleInput { get; set; }

    /// <summary>The workflow's global variables, set in order when a run starts, before its first step.</summary>
    public List<GlobalVariable> Globals { get; set; } = [];

    public List<WorkflowStep> Steps { get; set; } = [];

    public List<WorkflowLink> Links { get; set; } = [];

    /// <summary>
    /// The id of the workflow started when a run of this one fails, with what failed as its input; empty for the one on
    /// Settings / Workflow Settings.
    /// </summary>
    public string ErrorWorkflow { get; set; } = "";

    /// <summary>Optional swimlanes: bands across the canvas, one under another from <see cref="LanesTop"/>.</summary>
    public List<WorkflowLane> Lanes { get; set; } = [];

    /// <summary>Where on the canvas the first swimlane starts.</summary>
    public double LanesTop { get; set; }
}

/// <summary>A band across the canvas with a name, such as who does the steps in it; a step is in the lane it sits in.</summary>
public sealed class WorkflowLane
{
    public string Name { get; set; } = "";

    public double Height { get; set; } = 200;
}

/// <summary>An input to start a run by hand with, as JSON, and a name to choose it by.</summary>
public sealed class NamedInput
{
    public string Name { get; set; } = "";

    public string Input { get; set; } = "{}";
}

/// <summary>
/// A variable of the whole workflow, which every step reads as <c>{{vars.name}}</c>. Its value is set when a run
/// starts: fixed JSON or text, or taken from the input, such as <c>{{input.order.id}}</c>.
/// </summary>
public sealed class GlobalVariable
{
    public string Name { get; set; } = "";

    /// <summary>JSON or text, which may use paths into the input; a path alone keeps its JSON.</summary>
    public string Value { get; set; } = "";

    /// <summary>Whether a run is refused when the value comes out empty, as when the input lacks what it names.</summary>
    public bool Required { get; set; }

    public string Description { get; set; } = "";
}

/// <summary>What can start a workflow, besides starting it by hand, which is always possible.</summary>
public sealed class WorkflowTriggers
{
    /// <summary>Whether the workflow_start call of the REST API and the agent may start it.</summary>
    public bool Rest { get; set; }

    /// <summary>Whether a message on <see cref="MqttTopic"/> at <see cref="MqttClient"/>'s broker starts it.</summary>
    public bool Mqtt { get; set; }

    /// <summary>The name of the client on Dev Tools / Brokers / MQTT that listens.</summary>
    public string MqttClient { get; set; } = "";

    /// <summary>A topic filter, which may have + and # wildcards.</summary>
    public string MqttTopic { get; set; } = "";

    /// <summary>Whether the workflow is started on <see cref="Schedule"/>.</summary>
    public bool Scheduled { get; set; }

    /// <summary>When the workflow starts: once, at an interval or daily, as a folder message on Dev Tools / Brokers / MQTT is sent.</summary>
    [JsonConverter(typeof(ScheduleJsonConverter))]
    public MessageSchedule Schedule { get; set; } = WorkflowSchedules.Default;

    /// <summary>The name of the sample input a scheduled run starts with; empty for <c>{}</c>.</summary>
    public string ScheduleInput { get; set; } = "";

    /// <summary>Whether a request to <see cref="WebhookPath"/> below /hooks on the REST API's server starts the workflow.</summary>
    public bool Webhook { get; set; }

    /// <summary>The webhook's path below /hooks, such as orders/new; matched without regard to case.</summary>
    public string WebhookPath { get; set; } = "";

    /// <summary>Whether a request needs an API key of Settings / REST API, as the calls do. Off lets anyone who can reach the server start it.</summary>
    public bool WebhookNeedsKey { get; set; } = true;

    /// <summary>Whether the webhook answers when the run has finished, with its output, rather than at once with the run.</summary>
    public bool WebhookWait { get; set; }
}

/// <summary>Writes a schedule as a folder message's settings file does, as <c>{"type": "interval", "every": 5, ...}</c>.</summary>
public sealed class ScheduleJsonConverter : JsonConverter<MessageSchedule>
{
    public override MessageSchedule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader);
        try
        {
            return node is null ? WorkflowSchedules.Default : MessageSettings.ParseSchedule(node);
        }
        catch (FormatException e)
        {
            throw new JsonException(e.Message, e);
        }
    }

    public override void Write(Utf8JsonWriter writer, MessageSchedule value, JsonSerializerOptions options)
        => MessageSettings.ScheduleToJson(value).WriteTo(writer, options);
}

/// <summary>A link from one step's output to the step that follows it.</summary>
public sealed class WorkflowLink
{
    public string From { get; set; } = "";

    /// <summary>Which of the step's outputs, such as next, true, false or error.</summary>
    public string Output { get; set; } = StepOutputs.Next;

    public string To { get; set; } = "";
}

/// <summary>The outputs a step can have.</summary>
public static class StepOutputs
{
    public const string Next = "next";
    public const string True = "true";
    public const string False = "false";
    public const string Error = "error";
    public const string Timeout = "timeout";
    public const string Loop = "loop";
    public const string Done = "done";
    public const string Default = "default";

    /// <summary>What a switch's output for a case starts with, before the case's value.</summary>
    public const string CasePrefix = "case:";

    public static string Case(string value) => CasePrefix + value;
}
