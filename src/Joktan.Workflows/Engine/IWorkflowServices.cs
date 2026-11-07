using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Api;

namespace Joktan.Workflows.Engine;

/// <summary>An MQTT message a workflow received.</summary>
public sealed record MqttArrival(string Topic, byte[] Payload, bool Retain, DateTime Time)
{
    public string Text => Encoding.UTF8.GetString(Payload);

    /// <summary>The message as a workflow sees it: its topic, and its payload as JSON or text.</summary>
    public JsonObject ToJson() => new()
    {
        ["topic"] = Topic,
        ["payload"] = Templates.Parse(Text),
        ["retain"] = Retain,
        ["time"] = Time.ToString("O"),
    };
}

/// <summary>A folder message of an MQTT client, as a workflow sends it.</summary>
public sealed record SavedMessage(string File, string Topic, string Body, int Qos, bool Retain, bool Gzip);

/// <summary>The parts of the app that steps use: the MQTT clients and the API Client's saved requests.</summary>
public interface IWorkflowServices
{
    /// <summary>The names of the clients on Dev Tools / Brokers / MQTT.</summary>
    IReadOnlyList<string> MqttClients { get; }

    /// <summary>Raised when clients are added, removed or connect, so that MQTT triggers can listen again.</summary>
    event Action? MqttClientsChanged;

    /// <summary>The files of a client's folder messages.</summary>
    IReadOnlyList<string> FolderMessages(string client);

    /// <summary>A client's folder message, with its varying fields filled in, or null if there is none.</summary>
    SavedMessage? ReadFolderMessage(string client, string file);

    /// <summary>Publishes from a connected client; gives why it could not, or null.</summary>
    Task<string?> PublishAsync(string client, string topic, byte[] payload, int qos, bool retain);

    /// <summary>Listens on a client for messages matching a filter until the listener is disposed; gives why it could not instead.</summary>
    Task<(IDisposable? Listener, string? Error)> ListenAsync(string client, string filter, Action<MqttArrival> received);

    /// <summary>The paths of the requests saved in the API Client, within its folder.</summary>
    IReadOnlyList<string> SavedRequests();

    ApiRequest? ReadRequest(string path);

    /// <summary>The variables of the API Client's environment in use.</summary>
    ApiVariables Variables();
}

/// <summary>Why a step did not work, and the output the run leaves the step by.</summary>
public sealed class StepFailure(string message, string output = Model.StepOutputs.Error) : Exception(message)
{
    public string Output { get; } = output;
}

/// <summary>Why a run cannot be started or changed.</summary>
public sealed class WorkflowException(string message) : Exception(message);

public static class JsonText
{
    /// <summary>Compact JSON for a value, or "null".</summary>
    public static string Of(JsonNode? node) => node?.ToJsonString(Core.JsonFormats.Compact) ?? "null";

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    /// <summary>A value of any type as JSON, as C# scripts may give back anything.</summary>
    public static JsonNode? From(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        JsonElement element => JsonNode.Parse(element.GetRawText()),
        string text => JsonValue.Create(text),
        _ => JsonSerializer.SerializeToNode(value, value.GetType()),
    };
}
