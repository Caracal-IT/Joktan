using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Joktan.Api;
using Joktan.Api.ViewModels;
using Joktan.Containers.ViewModels;
using Joktan.Core;
using Joktan.Data.Postgres;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;
using Microsoft.Extensions.Logging;

namespace Joktan.Workflows.Engine;

/// <summary>Opens a database: host, port, username, password and database.</summary>
public delegate IDatabaseConnection DatabaseOpener(string host, int port, string username, string password, string database);

/// <summary>
/// The steps' way into the app: the clients of Dev Tools / Brokers / MQTT, the requests of the API Client,
/// and the databases of Data / Database and Settings / Workflow Settings.
/// </summary>
public sealed class AppWorkflowServices : IWorkflowServices
{
    // Longer than a connect may take, so that one another part of the app started is waited for.
    private static readonly TimeSpan ConnectWait = TimeSpan.FromSeconds(15);

    private readonly MqttViewModel _mqtt;
    private readonly ApiClientViewModel _api;
    private readonly PostgresDatabasesViewModel? _databases;
    private readonly DatabaseOpener _open;
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

    /// <param name="databases">The databases on Data / Database.</param>
    /// <param name="connections">The connections on Settings / Workflow Settings.</param>
    /// <param name="open">Opens a database; over the network otherwise.</param>
    public AppWorkflowServices(MqttViewModel mqtt, ApiClientViewModel api, PostgresDatabasesViewModel? databases = null,
        DatabaseConnections? connections = null, DatabaseOpener? open = null)
    {
        _mqtt = mqtt;
        _api = api;
        _databases = databases;
        Connections = connections;
        _open = open ?? ((host, port, username, password, database) => new PostgresConnection(host, port, username, password, database));
        // Subscriptions are made again on every connect by the connection itself, so only the list of clients matters.
        mqtt.Clients.CollectionChanged += (_, _) => MqttClientsChanged?.Invoke();
    }

    /// <summary>The roles and users of the Users page, which a user input step can be assigned to.</summary>
    public Func<Model.AssigneeKind, IReadOnlyList<AssigneeChoice>>? People { get; init; }

    public IReadOnlyList<AssigneeChoice> Assignees(Model.AssigneeKind kind) => People?.Invoke(kind) ?? [];

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
        return new SavedMessage(topic, body, settings.Qos, settings.Retain, settings.Gzip);
    }

    public async Task<string?> PublishAsync(string client, string topic, byte[] payload, int qos, bool retain)
    {
        if (_mqtt.Find(client) is not { } found)
            return $"there is no client called {client}";
        if (await ConnectAsync(found) is { } failed)
            return failed;
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

        // The connection subscribes again on every connect, so the listener hears what comes once the client
        // is connected later, by hand or when its broker comes back.
        if (await ConnectAsync(found) is { } failed)
            AppLog.For("Workflows").LogWarning("Listening on {Filter} waits for {Client}: {Error}", filter, client, failed);
        return (listener, null);
    }

    /// <summary>Connects a client that is not connected, as its Connect button does; gives why it could not, or null.</summary>
    private static async Task<string?> ConnectAsync(MqttClientViewModel client)
    {
        var connection = client.Connection;
        if (connection.IsConnected)
            return null;

        // The client's page is changed by connecting, so it is done on the UI thread.
        var problem = await OnUiThread(() =>
        {
            if (connection.State is ConnectionState.Connecting or ConnectionState.Reconnecting || client.ConnectCommand.IsRunning)
                return null;
            if (!client.ConnectCommand.CanExecute(null))
                return $"{client.Name} cannot connect: its address or port is not valid";
            client.ConnectCommand.Execute(null);
            return null;
        });
        if (problem is not null)
            return problem;

        await WhileConnectingAsync(connection);
        return connection.IsConnected
            ? null
            : $"{client.Name} could not connect{(connection.FailureReason is { Length: > 0 } reason ? ": " + reason : "")}";
    }

    private static async Task WhileConnectingAsync(MqttConnection connection)
    {
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed()
        {
            if (connection.State is not (ConnectionState.Connecting or ConnectionState.Reconnecting))
                settled.TrySetResult();
        }

        connection.StateChanged += Changed;
        try
        {
            Changed();
            await settled.Task.WaitAsync(ConnectWait);
        }
        catch (TimeoutException)
        {
            // Still trying; the caller says it is not connected.
        }
        finally
        {
            connection.StateChanged -= Changed;
        }
    }

    private static Task<T> OnUiThread<T>(Func<T> action)
        => Dispatcher.UIThread.CheckAccess() ? Task.FromResult(action()) : Dispatcher.UIThread.InvokeAsync(action).GetTask();

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

    public DatabaseConnections? Connections { get; }

    public IReadOnlyList<DatabaseChoice> Databases()
        => (_databases?.Items.Select(r => new DatabaseChoice(r.Database.Id, r.Name, r.ConnectionText)) ?? [])
            .Concat(Connections?.All.Select(c => new DatabaseChoice(c.Id, c.Name, c.ConnectionText)) ?? [])
            .ToList();

    public (IDatabaseConnection? Connection, string? Error) OpenDatabase(string id)
    {
        if (_databases?.Items.FirstOrDefault(r => r.Database.Id == id)?.Database is { } database)
        {
            return _databases.PasswordOf(database) is { Length: > 0 } password
                ? (_open("localhost", database.Port, database.Username, password, database.Database), null)
                : (null, $"No password is kept for the database {database.Name} on Data / Database.");
        }

        if (Connections?.Find(id) is { } connection)
            return (_open(connection.Host, connection.Port, connection.Username, Connections.PasswordOf(connection) ?? "", connection.Database), null);
        return (null, "The database is not on Data / Database or Settings / Workflow Settings any more; choose another.");
    }

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
