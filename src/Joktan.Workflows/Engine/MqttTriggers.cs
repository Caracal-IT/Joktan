using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Core;
using Joktan.Mqtt;
using Joktan.Workflows.Model;
using Microsoft.Extensions.Logging;

namespace Joktan.Workflows.Engine;

/// <summary>Listens for the MQTT messages that start workflows, on the clients the workflows name.</summary>
internal sealed class MqttTriggers : IDisposable
{
    private readonly WorkflowEngine _engine;
    private readonly List<IDisposable> _listeners = [];
    private readonly Dictionary<string, string> _problems = new(StringComparer.Ordinal);
    private int _generation;

    public MqttTriggers(WorkflowEngine engine)
    {
        _engine = engine;
        engine.Library.Changed += Refresh;
        engine.Services.MqttClientsChanged += Refresh;
    }

    public IReadOnlyDictionary<string, string> Problems => _problems;

    /// <summary>Listens again from scratch, for the workflows and clients there are now.</summary>
    public void Refresh() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        var generation = ++_generation;
        StopListening();
        _problems.Clear();
        if (!_engine.Settings.MqttTriggers)
            return;

        var wanted = _engine.Library.All.Where(w => w.Triggers.Mqtt).ToList();
        var clients = _engine.Services.MqttClients;
        foreach (var group in wanted.GroupBy(w => (Client: w.Triggers.MqttClient.Trim(), Topic: w.Triggers.MqttTopic.Trim())))
        {
            if (!clients.Contains(group.Key.Client))
            {
                foreach (var workflow in group)
                    _problems[workflow.Id] = $"There is no MQTT client called {group.Key.Client}.";
                continue;
            }

            if (TopicRules.ValidateFilter(group.Key.Topic) is { } invalid)
            {
                foreach (var workflow in group)
                    _problems[workflow.Id] = invalid;
                continue;
            }

            var ids = group.Select(w => w.Id).ToList();
            var (listener, error) = await _engine.Services.ListenAsync(group.Key.Client, group.Key.Topic,
                message => _engine.Post(() => Arrived(ids, message)));

            // A later refresh has started meanwhile; it does the listening now.
            if (generation != _generation)
            {
                listener?.Dispose();
                return;
            }

            if (listener is not null)
                _listeners.Add(listener);
            else
                foreach (var id in ids)
                    _problems[id] = error ?? "It could not listen.";
        }
    }

    private void Arrived(IReadOnlyList<string> workflowIds, MqttArrival message)
    {
        // The payload is the run's input, so it must be JSON.
        JsonNode? input;
        try
        {
            input = JsonNode.Parse(message.Text);
        }
        catch (JsonException e)
        {
            foreach (var id in workflowIds)
                _problems[id] = $"A message on {message.Topic} was not JSON, so it started no run: {e.Message}";
            AppLog.For("Workflows").LogWarning("A message on {Topic} was not JSON, so it started no run", message.Topic);
            return;
        }

        foreach (var id in workflowIds)
        {
            if (_engine.Library.Find(id) is not { Triggers.Mqtt: true } workflow)
                continue;
            try
            {
                _engine.Start(workflow, input?.DeepClone(), RunTrigger.Mqtt, message.Topic);
                _problems.Remove(id);
            }
            catch (Exception e) when (e is WorkflowException or ObjectDisposedException)
            {
                _problems[id] = e.Message;
                AppLog.For("Workflows").LogWarning("A message on {Topic} could not start {Workflow}: {Error}", message.Topic, workflow.Name, e.Message);
            }
        }
    }

    private void StopListening()
    {
        foreach (var listener in _listeners)
            listener.Dispose();
        _listeners.Clear();
    }

    public void Dispose()
    {
        _generation++;
        _engine.Library.Changed -= Refresh;
        _engine.Services.MqttClientsChanged -= Refresh;
        StopListening();
    }
}
