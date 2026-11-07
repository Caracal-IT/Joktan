using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Joktan.Api;
using Joktan.Data.Postgres;
using Joktan.Mqtt;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Tests;

/// <summary>MQTT clients and saved requests that live in the test: what is published is kept, and messages are delivered by hand.</summary>
public sealed class FakeWorkflowServices : IWorkflowServices
{
    private readonly List<(string Client, string Filter, Action<MqttArrival> Received)> _listeners = [];

    public List<string> Clients { get; } = ["Local"];

    public Dictionary<string, SavedMessage> Messages { get; } = [];

    public Dictionary<string, ApiRequest> Requests { get; } = [];

    public List<ApiPair> Environment { get; } = [];

    public ConcurrentQueue<(string Client, string Topic, string Payload, int Qos, bool Retain)> Published { get; } = new();

    public string? PublishError { get; set; }

    public IReadOnlyList<string> MqttClients => Clients;

    public event Action? MqttClientsChanged;

    public int Listening
    {
        get
        {
            lock (_listeners)
                return _listeners.Count;
        }
    }

    public void ClientsChanged() => MqttClientsChanged?.Invoke();

    public IReadOnlyList<string> FolderMessages(string client) => Messages.Keys.ToList();

    public SavedMessage? ReadFolderMessage(string client, string file) => Messages.GetValueOrDefault(file);

    public Task<string?> PublishAsync(string client, string topic, byte[] payload, int qos, bool retain)
    {
        if (PublishError is not null)
            return Task.FromResult<string?>(PublishError);
        var text = Gzip.LooksZipped(payload) ? Encoding.UTF8.GetString(Gzip.TryDecompress(payload)!) : Encoding.UTF8.GetString(payload);
        Published.Enqueue((client, topic, text, qos, retain));
        return Task.FromResult<string?>(null);
    }

    public Task<(IDisposable? Listener, string? Error)> ListenAsync(string client, string filter, Action<MqttArrival> received)
    {
        if (!Clients.Contains(client))
            return Task.FromResult<(IDisposable?, string?)>((null, $"there is no client called {client}"));
        var entry = (client, filter, received);
        lock (_listeners)
            _listeners.Add(entry);
        return Task.FromResult<(IDisposable?, string?)>((new Remove(() =>
        {
            lock (_listeners)
                _listeners.Remove(entry);
        }), null));
    }

    /// <summary>Delivers a message to whoever listens for its topic.</summary>
    public void Deliver(string topic, string payload, string client = "Local")
    {
        List<Action<MqttArrival>> receivers;
        lock (_listeners)
            receivers = _listeners.Where(l => l.Client == client && TopicRules.Matches(l.Filter, topic)).Select(l => l.Received).ToList();
        foreach (var receive in receivers)
            receive(new MqttArrival(topic, Encoding.UTF8.GetBytes(payload), false, DateTime.Now));
    }

    public IReadOnlyList<string> SavedRequests() => Requests.Keys.ToList();

    public ApiRequest? ReadRequest(string path) => Requests.GetValueOrDefault(path);

    public ApiVariables Variables() => new(Environment);

    /// <summary>The databases offered, by id.</summary>
    public Dictionary<string, (string Name, FakeDatabase Database)> Databases { get; } = [];

    public DatabaseConnections? Connections { get; set; }

    IReadOnlyList<DatabaseChoice> IWorkflowServices.Databases()
        => Databases.Select(d => new DatabaseChoice(d.Key, d.Value.Name, $"fake://{d.Key}")).ToList();

    public (IDatabaseConnection? Connection, string? Error) OpenDatabase(string id)
    {
        if (!Databases.TryGetValue(id, out var found))
            return (null, $"There is no database {id}.");
        found.Database.Opened++;
        return (found.Database, null);
    }

    /// <summary>Offers a database, and gives it.</summary>
    public FakeDatabase AddDatabase(string id = "db1", string name = "Shop")
    {
        var database = new FakeDatabase();
        Databases[id] = (name, database);
        return database;
    }

    private sealed class Remove(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}

/// <summary>Builds a workflow by chaining steps, each linked from the one before by its next output.</summary>
public sealed class Flow
{
    private int _count;

    public Flow(string name = "Test", WorkflowKind kind = WorkflowKind.Short)
    {
        Workflow = new Workflow { Id = "wf-" + name.ToLowerInvariant().Replace(' ', '-'), Name = name, Kind = kind };
        Last = Add(new WorkflowStep { Type = StepType.Start, Name = "Start" });
    }

    public Workflow Workflow { get; }

    public WorkflowStep Last { get; private set; }

    /// <summary>Adds a step without linking it.</summary>
    public WorkflowStep Add(WorkflowStep step)
    {
        step.Id = step.Id.Length > 0 ? step.Id : $"s{++_count}";
        step.Name = step.Name.Length > 0 ? step.Name : $"{step.Type} {_count}";
        Workflow.Steps.Add(step);
        return step;
    }

    /// <summary>Adds a step linked from the last one's output.</summary>
    public Flow Then(WorkflowStep step, string output = StepOutputs.Next)
    {
        var from = Last;
        Add(step);
        Link(from, step, output);
        Last = step;
        return this;
    }

    public Flow Link(WorkflowStep from, WorkflowStep to, string output = StepOutputs.Next)
    {
        Workflow.Links.Add(new WorkflowLink { From = from.Id, To = to.Id, Output = output });
        return this;
    }

    public static WorkflowStep Set(string variable, string value, bool merge = false)
        => new() { Type = StepType.SetVariable, Variable = variable, Value = value, MergeValue = merge };

    public static WorkflowStep End(string value = "") => new() { Type = StepType.End, Value = value };

    public static WorkflowStep Log(string message) => new() { Type = StepType.Log, Message = message };

    public static WorkflowStep Delay(string duration) => new() { Type = StepType.Delay, Duration = duration };

    public static WorkflowStep If(string left, CompareOperator op, string right = "")
        => new() { Type = StepType.Condition, Left = left, Operator = op, Right = right };

    public static WorkflowStep CSharp(string code, string result = "")
        => new() { Type = StepType.Script, Language = ScriptLanguage.CSharp, Code = code, ResultVariable = result };
}
