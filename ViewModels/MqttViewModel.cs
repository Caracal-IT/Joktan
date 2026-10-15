using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Mqtt;

namespace Joktan.ViewModels;

/// <summary>
/// Dev Tools / Connectivity / MQTT: publishes test messages to an MQTT broker, typed in by hand or
/// read from a folder of JSON files. Specified in docs/features/dev-tools/connectivity/mqtt/spec.md.
/// </summary>
public partial class MqttViewModel : PageViewModel
{
    private const int MaxLogEntries = 500;

    private readonly MqttSettings _settings = MqttSettings.Load();
    private readonly CredentialStore _credentials = CredentialStore.Create();
    private readonly MqttConnection _connection = new();
    private readonly FolderPublisher _publisher;
    private readonly DispatcherTimer _runTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _rescanTimer;
    private FileSystemWatcher? _watcher;
    private ConnectionState _loggedState;
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);
    private bool _ticking;
    private bool _loading = true;

    public MqttViewModel()
        : base("MQTT", "Dev Tools / Connectivity / MQTT")
    {
        _publisher = new FolderPublisher(_connection, Log);
        _runTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, OnRunTick);
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, OnSaveTick);
        _rescanTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, OnRescanTick);
        _runTimer.Stop();
        _saveTimer.Stop();
        _rescanTimer.Stop();

        if (_settings.ClientId.Length == 0)
        {
            _settings.ClientId = $"joktan-{Guid.NewGuid():N}"[..15];
            _settings.Save();
        }

        Host = _settings.Host;
        Port = _settings.Port;
        Username = _settings.Username;
        ClientId = _settings.ClientId;
        ManualTopic = _settings.ManualTopic;
        ManualBody = _settings.ManualBody;
        ManualQos = _settings.ManualQos;
        ManualRetain = _settings.ManualRetain;
        FolderPath = _settings.Folder;
        if (Username.Length > 0)
            Password = _credentials.Read(CredentialStore.AccountFor(Username, Host, Port)) ?? "";
        _loading = false;

        _connection.StateChanged += () => Dispatcher.UIThread.Post(OnConnectionChanged);
        LoadFolder();
        Watch();
    }

    /// <summary>The saved settings, shared with the topic explorer. Call <see cref="SaveSettings"/> after changing them.</summary>
    public MqttSettings Settings => _settings;

    /// <summary>The broker connection, shared with the topic explorer.</summary>
    public MqttConnection Connection => _connection;

    /// <summary>Asks the user for a folder. Set by the view, which owns the system dialog.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Shows the settings editor and returns whether it was saved. Set by the view.</summary>
    public Func<MessageSettingsViewModel, Task<bool>>? EditSettings { get; set; }

    /// <summary>Every message in the folder, in list order.</summary>
    public ObservableCollection<FolderMessageRowViewModel> Rows { get; } = [];

    /// <summary>The same messages, arranged under their groups for display.</summary>
    public ObservableCollection<MessageGroupViewModel> Groups { get; } = [];

    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    // Broker

    [ObservableProperty]
    public partial string Host { get; set; } = "";

    [ObservableProperty]
    public partial string Port { get; set; } = "1883";

    [ObservableProperty]
    public partial string Username { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial string ClientId { get; set; } = "";

    public ConnectionState State => _connection.State;

    public string StateText => State == ConnectionState.Failed
        ? $"Failed: {_connection.FailureReason}"
        : State.ToString();

    public bool IsConnected => State == ConnectionState.Connected;

    public bool IsPending => State is ConnectionState.Connecting or ConnectionState.Reconnecting;

    public bool IsFailed => State == ConnectionState.Failed;

    public bool CanEditBroker => State is ConnectionState.Disconnected or ConnectionState.Failed;

    public bool ShowDisconnect => !CanEditBroker;

    public string DisconnectText => State == ConnectionState.Connecting ? "Cancel" : "Disconnect";

    private bool IsBrokerValid
        => Host.Trim().Length > 0 && !Host.Trim().Any(char.IsWhiteSpace) && ParsedPort is not null;

    private int? ParsedPort
        => int.TryParse(Port.Trim(), out var port) && port is >= 1 and <= 65535 ? port : null;

    private bool CanConnect => CanEditBroker && IsBrokerValid;

    // Manual message

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManualTopicError))]
    public partial string ManualTopic { get; set; } = "";

    [ObservableProperty]
    public partial string ManualBody { get; set; } = "";

    [ObservableProperty]
    public partial int ManualQos { get; set; }

    [ObservableProperty]
    public partial bool ManualRetain { get; set; }

    /// <summary>Shown under the topic box; an empty topic is not reported as an error.</summary>
    public string? ManualTopicError => ManualTopic.Length == 0 ? null : TopicRules.Validate(ManualTopic);

    private bool CanSendManual => IsConnected && TopicRules.Validate(ManualTopic) is null;

    // Folder messages

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderText))]
    public partial string? FolderPath { get; set; }

    [ObservableProperty]
    public partial string? FolderError { get; set; }

    [ObservableProperty]
    public partial string FolderSummary { get; set; } = "";

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>The group names in use, offered when a message is edited.</summary>
    public IReadOnlyList<string> GroupNames { get; private set; } = [];

    public string FolderText => string.IsNullOrEmpty(FolderPath) ? "No folder chosen" : FolderPath;


    // Activity

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedBody), nameof(HasSelectedBody))]
    public partial ActivityEntry? SelectedEntry { get; set; }

    public string? SelectedBody => SelectedEntry?.Body;

    public bool HasSelectedBody => SelectedEntry?.Body is not null;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading)
            return;

        switch (e.PropertyName)
        {
            case nameof(Host) or nameof(Port) or nameof(Username) or nameof(ManualTopic) or nameof(ManualBody)
                or nameof(ManualQos) or nameof(ManualRetain):
                _saveTimer.Stop();
                _saveTimer.Start();
                UpdateCommands();
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (ParsedPort is not { } port)
            return;

        var username = Username.Trim();
        await _connection.ConnectAsync(new BrokerEndpoint(
            Host.Trim(), port, ClientId,
            username.Length > 0 ? username : null,
            username.Length > 0 ? Password : null));

        if (_connection.IsConnected)
            RememberPassword();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (IsRunning)
            Stop();
        await _connection.DisconnectAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSendManual))]
    private async Task SendManualAsync()
    {
        var now = DateTime.Now;
        var payload = Encoding.UTF8.GetBytes(ManualBody);
        var (ok, error) = await _connection.PublishAsync(ManualTopic, payload, ManualQos, ManualRetain);

        if (ok)
        {
            MqttMetrics.RecordSent(manual: true);
            Log(new ActivityEntry(now, "Sent", ManualTopic, "Manual", $"{payload.Length:N0} bytes", ManualBody));
        }
        else
        {
            MqttMetrics.RecordFailed();
            Log(new ActivityEntry(now, "Failed", ManualTopic, "Manual", error ?? "Unknown error"));
        }
    }

    /// <summary>Lays the manual body out as indented JSON, if it is JSON.</summary>
    public void FormatManualBody()
    {
        try
        {
            if (JsonNode.Parse(ManualBody) is { } node)
                ManualBody = node.ToJsonString(JsonFormats.Indented);
        }
        catch (JsonException)
        {
            // Not JSON: the body is sent as typed.
        }
    }

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        if (PickFolder is null || await PickFolder() is not { } path)
            return;

        FolderPath = path;
        _settings.Folder = path;
        _settings.Save();
        LoadFolder();
        Watch();
    }

    [RelayCommand]
    private void Rescan() => LoadFolder();

    // Schedules follow the connection: they start when it is made and stop when it is given up.
    private void Start()
    {
        if (IsRunning)
            return;

        _publisher.Start(DateTime.Now);
        IsRunning = true;
        _runTimer.Start();
        RefreshRows();
    }

    private void Stop()
    {
        if (!IsRunning)
            return;

        _runTimer.Stop();
        _publisher.Stop();
        IsRunning = false;
        RefreshRows();
    }

    [RelayCommand]
    private void ToggleGroup(MessageGroupViewModel? group)
    {
        if (group is null)
            return;

        group.IsExpanded = !group.IsExpanded;
        if (group.IsExpanded)
            _collapsedGroups.Remove(group.Path);
        else
            _collapsedGroups.Add(group.Path);
        group.UpdateVisibility();
    }

    private void SetEnabled(FolderMessageRowViewModel row, bool enabled)
    {
        Write(row.Message, enabled);
        LoadFolder();
    }

    private void SetGroupEnabled(MessageGroupViewModel group, bool enabled)
    {
        foreach (var row in group.AllRows.Where(r => r.CanSwitch && r.IsOn != enabled).ToList())
            Write(row.Message, enabled);
        LoadFolder();
    }

    // The switch is the message's "enabled" setting, so it is kept in the settings file.
    private void Write(FolderMessage message, bool enabled)
    {
        try
        {
            File.WriteAllText(
                MessageSettings.PathFor(message.FilePath),
                (message.Settings with { Enabled = enabled }).ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log(new ActivityEntry(DateTime.Now, "Failed", message.Topic, message.FileName, $"Settings not saved: {e.Message}"));
        }
    }

    private bool CanSendNow(FolderMessageRowViewModel? row) => IsConnected && row is { IsInvalid: false };

    [RelayCommand(CanExecute = nameof(CanSendNow))]
    private async Task SendNowAsync(FolderMessageRowViewModel? row)
    {
        if (row is null)
            return;

        await _publisher.SendNowAsync(row.Message, DateTime.Now);
        row.Refresh();
    }

    private static bool CanEdit(FolderMessageRowViewModel? row) => row?.Message.Body is not null;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditAsync(FolderMessageRowViewModel? row)
    {
        if (row is null || EditSettings is null)
            return;

        if (await EditSettings(new MessageSettingsViewModel(row.Message, GroupNames)))
            LoadFolder();
    }

    [RelayCommand]
    private void ClearLog()
    {
        SelectedEntry = null;
        Entries.Clear();
    }

    private void LoadFolder()
    {
        _publisher.Load(FolderPath, DateTime.Now);
        FolderError = _publisher.FolderError;

        Rows.Clear();
        foreach (var message in _publisher.Messages)
            Rows.Add(new FolderMessageRowViewModel(message, SetEnabled));

        // Named groups and their sub-groups in alphabetical order, then the messages that have no group.
        Groups.Clear();
        foreach (var group in MessageGroupViewModel.Build(Rows, path => !_collapsedGroups.Contains(path), SetGroupEnabled))
            Groups.Add(group);

        GroupNames = Groups.Where(g => g.Path.Length > 0).Select(g => g.Path).ToList();

        var invalid = Rows.Count(r => r.IsInvalid);
        var on = Rows.Count(r => r.IsOn);
        FolderSummary = string.IsNullOrEmpty(FolderPath) || FolderError is not null ? ""
            : Rows.Count == 0 ? "No .json files found in this folder."
            : $"{Rows.Count} messages, {on} switched on" + (invalid == 0 ? "" : $", {invalid} invalid");
        UpdateCommands();
    }

    private void Watch()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (string.IsNullOrEmpty(FolderPath) || !Directory.Exists(FolderPath))
            return;

        try
        {
            var watcher = new FileSystemWatcher(FolderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            watcher.Created += OnFolderChanged;
            watcher.Changed += OnFolderChanged;
            watcher.Deleted += OnFolderChanged;
            watcher.Renamed += OnFolderChanged;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Without a watcher the folder is still read on Rescan.
        }
    }

    // Changes arrive in bursts on a background thread; wait for them to settle, then scan once.
    private void OnFolderChanged(object sender, FileSystemEventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            _rescanTimer.Stop();
            _rescanTimer.Start();
        });

    private void OnRescanTick(object? sender, EventArgs e)
    {
        _rescanTimer.Stop();
        LoadFolder();
    }

    private async void OnRunTick(object? sender, EventArgs e)
    {
        if (_ticking)
            return;

        _ticking = true;
        try
        {
            if (await _publisher.TickAsync(DateTime.Now))
                RefreshRows();
        }
        finally
        {
            _ticking = false;
        }
    }

    private void OnSaveTick(object? sender, EventArgs e) => SaveSettings();

    /// <summary>
    /// Writes the settings to disk now. Edits are otherwise saved shortly after typing stops, so
    /// this is called on exit to keep anything typed in the last moment.
    /// </summary>
    public void SaveSettings()
    {
        _saveTimer.Stop();
        _settings.Host = Host;
        _settings.Port = Port;
        _settings.Username = Username;
        _settings.ManualTopic = ManualTopic;
        _settings.ManualBody = ManualBody;
        _settings.ManualQos = ManualQos;
        _settings.ManualRetain = ManualRetain;
        _settings.Save();
    }

    private void OnConnectionChanged()
    {
        var state = _connection.State;
        if (state != _loggedState)
        {
            _loggedState = state;
            var now = DateTime.Now;
            switch (state)
            {
                case ConnectionState.Connected:
                    Log(new ActivityEntry(now, "Connected", Detail: $"{Host.Trim()}:{Port.Trim()}"));
                    break;
                case ConnectionState.Reconnecting:
                    Log(new ActivityEntry(now, "Reconnecting", Detail: "The connection was lost"));
                    break;
                case ConnectionState.Disconnected:
                    Log(new ActivityEntry(now, "Disconnected"));
                    break;
                case ConnectionState.Failed:
                    Log(new ActivityEntry(now, "Failed", Detail: _connection.FailureReason ?? ""));
                    break;
            }
        }

        if (state == ConnectionState.Connected)
            Start();
        else if (state is ConnectionState.Disconnected or ConnectionState.Failed)
            Stop();

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(CanEditBroker));
        OnPropertyChanged(nameof(ShowDisconnect));
        OnPropertyChanged(nameof(DisconnectText));
        UpdateCommands();
    }

    // The password goes to the operating system's credential store, never to the settings file.
    private void RememberPassword()
    {
        var username = Username.Trim();
        if (username.Length == 0)
            return;

        var account = CredentialStore.AccountFor(username, Host.Trim(), Port.Trim());
        if (Password.Length == 0)
            _credentials.Delete(account);
        else
            _credentials.Write(account, Password);
    }

    private void Log(ActivityEntry entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > MaxLogEntries)
            Entries.RemoveAt(Entries.Count - 1);
    }

    private void RefreshRows()
    {
        foreach (var row in Rows)
            row.Refresh();
    }

    /// <summary>For the message a row shows, after the folder has been read again.</summary>
    public FolderMessageRowViewModel? RowFor(string fileName)
        => Rows.FirstOrDefault(r => r.File == fileName);

    private void UpdateCommands()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        SendManualCommand.NotifyCanExecuteChanged();
        SendNowCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value) => UpdateCommands();
}
