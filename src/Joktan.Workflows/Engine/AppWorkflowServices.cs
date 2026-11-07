using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Api;
using Joktan.Api.ViewModels;
using Joktan.Core;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Workflows.Engine;

/// <summary>The steps' way into the app: the clients of Dev Tools / Brokers / MQTT and the requests of the API Client.</summary>
public sealed class AppWorkflowServices : IWorkflowServices
{
    private readonly MqttViewModel _mqtt;
    private readonly ApiClientViewModel _api;
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

    public AppWorkflowServices(MqttViewModel mqtt, ApiClientViewModel api)
    {
        _mqtt = mqtt;
        _api = api;
        // Subscriptions are made again on every connect by the connection itself, so only the list of clients matters.
        mqtt.Clients.CollectionChanged += (_, _) => MqttClientsChanged?.Invoke();
    }

    public IReadOnlyList<string> MqttClients => _mqtt.Clients.Select(c => c.Name).ToList();

    public event Action? MqttClientsChanged;

    public IReadOnlyList<string> FolderMessages(string client)
        => _mqtt.Find(client)?.FolderPath is { Length: > 0 } folder
            ? FolderPublisher.Read(folder).Messages.Select(m => Relative(folder, m.FilePath)).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList()
            : [];

    public SavedMessage? ReadFolderMessage(string client, string file)
    {
        if (_mqtt.Find(client)?.FolderPath is not { Length: > 0 } folder)
            return null;
        var message = FolderPublisher.Read(folder).Messages.FirstOrDefault(m => Relative(folder, m.FilePath) == file || m.FileName == file);
        if (message is not { IsValid: true })
            return null;

        // Varying fields change as they do when the client sends the message, with counters of the workflows' own.
        var settings = message.Settings;
        var now = DateTimeOffset.Now;
        var body = settings.ChangesBody
            ? VaryingFields.Apply(message.Body!, settings.Arrays, settings.Fields, _counters, now).ToJsonString(JsonFormats.Compact)
            : message.Text;
        var topic = TopicTemplate.Resolve(message.Topic, settings.TopicFields, _counters, now);
        return new SavedMessage(Relative(folder, message.FilePath), topic, body, settings.Qos, settings.Retain, settings.Gzip);
    }

    public async Task<string?> PublishAsync(string client, string topic, byte[] payload, int qos, bool retain)
    {
        if (_mqtt.Find(client) is not { } found)
            return $"there is no client called {client}";
        if (!found.Connection.IsConnected)
            return $"{client} is not connected";
        var (_, error) = await found.Connection.PublishAsync(topic, payload, qos, retain);
        return error;
    }

    public async Task<(IDisposable? Listener, string? Error)> ListenAsync(string client, string filter, Action<MqttArrival> received)
    {
        if (_mqtt.Find(client) is not { } found)
            return (null, $"there is no client called {client}");
        var listener = new Listener(found.Connection, filter, received);
        if (await listener.StartAsync() is { } error)
        {
            listener.Dispose();
            return (null, error);
        }

        return (listener, null);
    }

    public IReadOnlyList<string> SavedRequests()
    {
        var folder = _api.Folder;
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*" + ApiWorkspace.RequestExtension, SearchOption.AllDirectories)
            .Where(f => !Relative(folder, f).Split('/').Any(part => part.StartsWith('.')))
            .Select(f => Relative(folder, f)[..^ApiWorkspace.RequestExtension.Length])
            .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public ApiRequest? ReadRequest(string path)
    {
        var full = Path.GetFullPath(Path.Combine(_api.Folder, path.Replace('/', Path.DirectorySeparatorChar) + ApiWorkspace.RequestExtension));
        return full.StartsWith(Path.GetFullPath(_api.Folder), StringComparison.Ordinal) ? ApiWorkspace.TryRead(full) : null;
    }

    public ApiVariables Variables() => _api.CurrentVariables();

    private static string Relative(string folder, string path) => Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>Messages matching one filter, on one connection, for as long as it is not disposed.</summary>
    private sealed class Listener(MqttConnection connection, string filter, Action<MqttArrival> received) : IDisposable
    {
        private readonly string _owner = "workflow:" + Guid.NewGuid().ToString("N");
        private bool _disposed;

        public async Task<string?> StartAsync()
        {
            connection.MessageReceived += OnMessage;
            return await connection.SetSubscriptionsAsync(_owner, [filter]);
        }

        private void OnMessage(ReceivedMessage message)
        {
            if (!_disposed && TopicRules.Matches(filter, message.Topic))
                received(new MqttArrival(message.Topic, message.Payload, message.Retain, message.Time));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            connection.MessageReceived -= OnMessage;
            _ = connection.SetSubscriptionsAsync(_owner, []);
        }
    }
}
