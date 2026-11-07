using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Joktan.Workflows.Model;

[JsonConverter(typeof(JsonStringEnumConverter<StepType>))]
public enum StepType
{
    /// <summary>Where a run begins. Each workflow has one.</summary>
    Start,

    /// <summary>Finishes the run, with an output for whoever waits on it.</summary>
    End,

    /// <summary>Sets a variable to a value or merges JSON into it.</summary>
    SetVariable,

    /// <summary>Goes one way or the other on a comparison.</summary>
    Condition,

    /// <summary>Waits for a while.</summary>
    Delay,

    /// <summary>Sends a REST request: one saved in the API Client, or one written here.</summary>
    Http,

    /// <summary>Calls a gRPC method: one saved in the API Client, or one written here, found by server reflection.</summary>
    Grpc,

    /// <summary>Publishes an MQTT message: a folder message of a client, or one written here.</summary>
    MqttPublish,

    /// <summary>Waits for an MQTT message on a topic.</summary>
    MqttWait,

    /// <summary>Runs C#, JavaScript or Go code.</summary>
    Script,

    /// <summary>Starts another workflow, and waits for its output or carries on at once.</summary>
    CallWorkflow,

    /// <summary>Asks a person for values and waits for the answer.</summary>
    UserInput,

    /// <summary>Writes a line in the run's log.</summary>
    Log,

    /// <summary>Stops the run as failed.</summary>
    Fail,

    /// <summary>Goes round its loop once for each item of a list, or a number of times.</summary>
    ForEach,

    /// <summary>Goes round its loop for as long as a comparison holds.</summary>
    While,

    /// <summary>Goes out by the case its value matches, or by default.</summary>
    Switch,
}

/// <summary>How a step that sends a saved message or request treats its body.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BodyMode>))]
public enum BodyMode
{
    /// <summary>Sent as it was saved.</summary>
    Keep,

    /// <summary>Replaced by the step's body.</summary>
    Replace,

    /// <summary>The step's body, which must be a JSON object, is merged into the saved one.</summary>
    Merge,
}

[JsonConverter(typeof(JsonStringEnumConverter<ScriptLanguage>))]
public enum ScriptLanguage
{
    CSharp,
    Go,
    JavaScript,
}

[JsonConverter(typeof(JsonStringEnumConverter<CompareOperator>))]
public enum CompareOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    Contains,
    StartsWith,
    IsEmpty,
    IsNotEmpty,

    /// <summary>The left side is C# that gives true or false.</summary>
    CSharp,
}

/// <summary>A value a person is asked for by a user input step.</summary>
public sealed class InputField
{
    public string Name { get; set; } = "";

    /// <summary>What the person is shown; the name when empty.</summary>
    public string Label { get; set; } = "";

    /// <summary>The value filled in to start with, which may use <c>{{paths}}</c>.</summary>
    public string Default { get; set; } = "";
}

/// <summary>A header, or other name and value, of a step's request.</summary>
public sealed class StepPair
{
    public string Name { get; set; } = "";

    public string Value { get; set; } = "";
}

/// <summary>
/// One step on the canvas. Like a request of the API Client, it holds the settings of every type, and its
/// <see cref="Type"/> says which are used. Text settings may hold <c>{{paths}}</c> into the run's data, such as
/// <c>{{input.id}}</c> or <c>{{vars.reply.body.name}}</c>.
/// </summary>
public sealed class WorkflowStep
{
    public string Id { get; set; } = "";

    public StepType Type { get; set; }

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>Where the step sits on the canvas, from the top left.</summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>The variable the step's result is kept in; it is always in <c>last</c> as well.</summary>
    public string ResultVariable { get; set; } = "";

    /// <summary>How many more times a step that fails is tried before it goes out by its error output.</summary>
    public int Retries { get; set; }

    /// <summary>How long to wait before each retry, such as 5s.</summary>
    public string RetryDelay { get; set; } = "5s";

    // Set variable

    public string Variable { get; set; } = "";

    /// <summary>The value: JSON, which may use paths, or text. For a merge, a JSON object. For an end step, the output.</summary>
    public string Value { get; set; } = "";

    public bool MergeValue { get; set; }

    // Condition, and for an MQTT wait the test a message must pass, on <c>{{message.…}}</c>; with no left side, the first is taken

    public string Left { get; set; } = "";

    public CompareOperator Operator { get; set; } = CompareOperator.Equals;

    public string Right { get; set; } = "";

    // Delay, waits and time limits

    /// <summary>How long, such as 30s, 5m, 2h or 1d; a number alone is seconds.</summary>
    public string Duration { get; set; } = "";

    // REST and gRPC

    /// <summary>The path of a request saved in the API Client, within its folder; empty to use the request written here.</summary>
    public string RequestPath { get; set; } = "";

    public string Method { get; set; } = "GET";

    public string Url { get; set; } = "";

    public List<StepPair> Headers { get; set; } = [];

    /// <summary>The gRPC server, for a call written here.</summary>
    public string Address { get; set; } = "";

    public string Service { get; set; } = "";

    public string Rpc { get; set; } = "";

    /// <summary>What a saved request, or a folder message, does with <see cref="Body"/>.</summary>
    public BodyMode BodyMode { get; set; } = BodyMode.Keep;

    /// <summary>The body of a REST request or MQTT message, or the message of a gRPC call.</summary>
    public string Body { get; set; } = "";

    /// <summary>Whether a REST answer that is not a success goes out through the error output.</summary>
    public bool FailOnErrorStatus { get; set; } = true;

    // MQTT

    /// <summary>The client on Dev Tools / Brokers / MQTT that publishes or listens.</summary>
    public string Client { get; set; } = "";

    /// <summary>The file of one of the client's folder messages; empty to send the message written here.</summary>
    public string MessageFile { get; set; } = "";

    /// <summary>The topic to publish to, or the filter to wait on. For a folder message, empty keeps its own.</summary>
    public string Topic { get; set; } = "";

    public int Qos { get; set; }

    public bool Retain { get; set; }

    // Script

    public ScriptLanguage Language { get; set; } = ScriptLanguage.CSharp;

    public string Code { get; set; } = "";

    /// <summary>
    /// What the code uses, one per entry. C#: a NuGet package, such as <c>Newtonsoft.Json 13.0.3</c>, or a .dll file.
    /// JavaScript: a script file or its address. Go: a module, such as <c>github.com/google/uuid@v1.6.0</c>, or
    /// <c>module =&gt; folder</c> for one on this computer.
    /// </summary>
    public List<string> Libraries { get; set; } = [];

    /// <summary>The folder the code works in, which may use paths; empty for the one on Settings / Workflow Settings.</summary>
    public string WorkingFolder { get; set; } = "";

    // Call workflow

    /// <summary>The id of the workflow to start.</summary>
    public string Workflow { get; set; } = "";

    /// <summary>The input for it, as JSON, which may use paths.</summary>
    public string Input { get; set; } = "";

    /// <summary>Whether to wait for its output; otherwise the run carries on as soon as it has started.</summary>
    public bool Wait { get; set; } = true;

    // User input

    /// <summary>What the person is asked, or the line logged, or why the run failed.</summary>
    public string Message { get; set; } = "";

    public List<InputField> Fields { get; set; } = [];

    // For each, while and switch

    /// <summary>What a for each goes through: a list, an object's entries, or a number of times; JSON or a path.</summary>
    public string Items { get; set; } = "";

    /// <summary>The variable holding the item of each time round a for each.</summary>
    public string ItemVariable { get; set; } = "item";

    /// <summary>The variable counting the times round a loop, from 0; empty for none.</summary>
    public string IndexVariable { get; set; } = "index";

    /// <summary>The values a switch compares its value with, in order; each is an output. They may be JSON or text.</summary>
    public List<string> Cases { get; set; } = [];
}
