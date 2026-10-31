using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;

namespace Joktan.TopicDashboards.ViewModels;

/// <summary>
/// Dashboard / Topic Dashboards: dashboards of graphs, drawn from MQTT topics, one tab each. Specified
/// in docs/features/dashboards/spec.md.
/// </summary>
public partial class TopicDashboardsViewModel : PageViewModel
{
    private const int MaxMessagesPerFlush = 5000;
    private const string FirstDashboardName = "Main";

    private readonly Dictionary<MqttClientViewModel, ClientFeed> _feeds = [];
    private readonly DispatcherTimer _flushTimer;
    private readonly DispatcherTimer _tickTimer;
    private DashboardStore _store;

    public TopicDashboardsViewModel(MqttViewModel mqtt, DashboardStore? store = null)
        : base("Topic Dashboards", "Dashboard / Topic Dashboards")
    {
        Mqtt = mqtt;
        _store = store ?? DashboardStore.Load();
        Open(_store);

        foreach (var client in mqtt.Clients)
            AddFeed(client);
        mqtt.Clients.CollectionChanged += OnClientsChanged;

        _flushTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, OnFlushTick);
        _tickTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick);
        _flushTimer.Start();
        _tickTimer.Start();

        GraphsChanged(save: false);
    }

    /// <summary>The MQTT page, which owns the clients and their connections.</summary>
    public MqttViewModel Mqtt { get; }

    public ObservableCollection<DashboardTabViewModel> Dashboards { get; } = [];

    [ObservableProperty]
    public partial DashboardTabViewModel? SelectedDashboard { get; set; }

    /// <summary>Whether the tabs show their buttons to rename, colour and remove them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditModeText))]
    public partial bool IsEditing { get; set; }

    /// <summary>The mode the button beside Add goes to.</summary>
    public string EditModeText => IsEditing ? "View" : "Edit";

    /// <summary>Why a subscription failed, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubscriptionError))]
    public partial string? SubscriptionError { get; private set; }

    public bool HasSubscriptionError => SubscriptionError is not null;

    /// <summary>Why the dashboards are not being saved, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    public partial string? SaveError { get; private set; }

    public bool HasSaveError => SaveError is not null;

    public string Folder => _store.Folder;

    public string DefaultFolder => DashboardStore.DefaultFolder;

    [ObservableProperty]
    public partial bool IsChoosingFolder { get; set; }

    /// <summary>The folder typed or picked in the folder band, used once applied.</summary>
    [ObservableProperty]
    public partial string FolderText { get; set; } = "";

    /// <summary>Why the folder typed or picked could not be used.</summary>
    [ObservableProperty]
    public partial string? FolderMessage { get; set; }

    /// <summary>What happened when the folder was last changed.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>Asks the user for a folder. Set by the view, which owns the system dialog.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Shows the graph editor. Set by the view.</summary>
    public Func<GraphEditorViewModel, Task>? ShowEditor { get; set; }

    public MqttClientViewModel? FindClient(string name) => Mqtt.Find(name);

    /// <summary>A new graph on a topic seen in the topic explorer, with the messages already received on it.</summary>
    /// <param name="history">Oldest first.</param>
    /// <param name="paths">The numbers to start with.</param>
    public GraphEditorViewModel NewEditor(MqttClientViewModel client, string topic, IEnumerable<ReceivedMessage> history, IEnumerable<string> paths)
        => new(this, client, topic, history, paths);

    /// <summary>Why a graph draws nothing, or null.</summary>
    public string? StatusOf(GraphCardViewModel card)
    {
        if (FindClient(card.Profile.Client) is not { } client)
            return $"There is no MQTT client called {card.Profile.Client}";
        if (card.HasReadings)
            return null;

        var topics = card.Profile.Topics.ToList();
        var source = topics.Count == 1 ? topics[0] : $"its {topics.Count} topics";
        return client.IsConnected
            ? $"Waiting for a message on {source}"
            : $"Connect {client.Name} to receive {source}";
    }

    public void AddGraph(DashboardTabViewModel dashboard)
        => _ = ShowEditor?.Invoke(new GraphEditorViewModel(this, dashboard));

    public void EditGraph(DashboardTabViewModel dashboard, GraphCardViewModel card)
        => _ = ShowEditor?.Invoke(new GraphEditorViewModel(this, dashboard, card));

    /// <summary>A new dashboard with a name no other has.</summary>
    public DashboardTabViewModel CreateDashboard(string? name = null)
    {
        var profile = new DashboardProfile { Name = name ?? UnusedName() };
        _store.Dashboards.Add(profile);
        var dashboard = new DashboardTabViewModel(profile, this);
        Dashboards.Add(dashboard);
        return dashboard;
    }

    /// <summary>Removes a dashboard and its graphs. There is always one on show, so the last is replaced with an empty one.</summary>
    public void Remove(DashboardTabViewModel dashboard)
    {
        if (Dashboards.Count == 1)
            CreateDashboard(FirstDashboardName);

        // Chosen first, as the tabs select the first one themselves when the selected one goes.
        if (SelectedDashboard == dashboard)
        {
            var index = Dashboards.IndexOf(dashboard);
            SelectedDashboard = Dashboards[index < Dashboards.Count - 1 ? index + 1 : index - 1];
        }

        Dashboards.Remove(dashboard);
        _store.Dashboards.Remove(dashboard.Profile);
        GraphsChanged();
    }

    /// <summary>Applies what the graph editor was left with.</summary>
    public void Commit(DashboardTabViewModel dashboard, GraphCardViewModel? card, GraphProfile profile, GraphData data)
    {
        data.Keep(profile.Values.Select(v => (v.Topic, v.Path)).ToHashSet());
        if (card is null)
        {
            dashboard.Add(profile, data);
        }
        else
        {
            var index = dashboard.Profile.Graphs.IndexOf(card.Profile);
            if (index >= 0)
                dashboard.Profile.Graphs[index] = profile;
            card.Apply(profile, data);
            GraphsChanged();
        }

        SelectedDashboard = dashboard;
    }

    /// <summary>Subscribes to what the graphs now need, and says how each stands.</summary>
    public void GraphsChanged() => GraphsChanged(save: true);

    public void Save()
    {
        _store.Selected = SelectedDashboard?.Name ?? "";
        SaveError = UnavailableMessage();
        _store.Save();
    }

    /// <summary>Writes anything not yet on disk. Called when the app exits.</summary>
    public void SaveState()
    {
        Save();
        _store.FlushAsync().GetAwaiter().GetResult();
    }

    [RelayCommand]
    private void ToggleFolder()
    {
        IsChoosingFolder = !IsChoosingFolder;
        FolderText = Folder;
        FolderMessage = null;
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
            FolderText = folder;
    }

    [RelayCommand]
    private void UseDefaultFolder() => FolderText = DefaultFolder;

    /// <summary>
    /// Keeps dashboards in the folder typed or picked. A folder that already holds dashboards is opened
    /// as it is; otherwise the dashboards open now are copied into it, and stay in the old folder as well.
    /// </summary>
    [RelayCommand]
    private async Task ApplyFolderAsync()
    {
        FolderMessage = null;
        var folder = FolderText.Trim();
        if (folder.Length == 0)
            folder = DefaultFolder;
        if (folder.StartsWith('~'))
            folder = System.IO.Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder[1..].TrimStart('/', '\\'));
        if (!System.IO.Path.IsPathFullyQualified(folder))
        {
            FolderMessage = "Give the whole path of the folder, such as one picked with Browse.";
            return;
        }

        folder = System.IO.Path.GetFullPath(folder);

        // The same folder again is how one that was missing is tried once more.
        if (DashboardStore.SameFolder(folder, Folder) && !_store.IsUnavailable)
        {
            IsChoosingFolder = false;
            return;
        }

        IsEditing = false;
        Save();
        var previous = Folder;
        if (DashboardStore.HasDashboards(folder))
        {
            await _store.FlushAsync();
            DashboardStore.SaveChosenFolder(folder);
            Open(DashboardStore.Load(folder));
            GraphsChanged(save: false);
            Message = $"Opened the dashboards already in {folder}.";
        }
        else
        {
            DashboardStore copy;
            try
            {
                copy = await _store.CopyToAsync(folder);
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                FolderMessage = $"Could not copy the dashboards to {folder}: {e.Message}";
                return;
            }

            // The same dashboards, so the graphs keep what they have drawn.
            DashboardStore.SaveChosenFolder(folder);
            UseStore(copy);
            Message = $"The dashboards were copied to {folder}. The originals are still in {previous}.";
        }

        IsChoosingFolder = false;
    }

    private void UseStore(DashboardStore store)
    {
        _store.Failed -= OnWriteFailed;
        _store = store;
        _store.Failed += OnWriteFailed;
        SaveError = UnavailableMessage();
        OnPropertyChanged(nameof(Folder));
    }

    private void Open(DashboardStore store)
    {
        UseStore(store);
        if (store.Dashboards.Count == 0)
            store.Dashboards.Add(new DashboardProfile { Name = FirstDashboardName });

        // Taken first, as the tabs select the first one themselves as they fill, which is saved.
        var selected = store.Selected;
        var dashboards = store.Dashboards.Select(p => new DashboardTabViewModel(p, this)).ToList();
        SelectedDashboard = null;
        Dashboards.Clear();
        foreach (var dashboard in dashboards)
            Dashboards.Add(dashboard);
        SelectedDashboard = dashboards.FirstOrDefault(d => d.Name == selected) ?? dashboards[0];
    }

    private string? UnavailableMessage() => _store.IsUnavailable
        ? $"The dashboards folder {Folder} cannot be read, so changes are not being saved. Bring it back and press Folder, then Use folder, or choose another."
        : null;

    private void OnWriteFailed(string path, Exception e)
        => Dispatcher.UIThread.Post(() => SaveError = $"Could not write {path}: {e.Message}");

    private void GraphsChanged(bool save)
    {
        if (save)
            Save();
        foreach (var dashboard in Dashboards)
            dashboard.UpdateClients();
        _ = SubscribeAsync();
    }

    private async Task SubscribeAsync()
    {
        string? error = null;
        var graphs = Dashboards.SelectMany(d => d.Graphs).Select(g => g.Profile).ToList();
        foreach (var feed in _feeds.Values.ToList())
        {
            var topics = graphs
                .Where(g => string.Equals(g.Client, feed.Name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(g => g.Topics)
                .Where(t => TopicRules.Validate(t) is null)
                .ToHashSet(StringComparer.Ordinal);
            error ??= await feed.SetTopicsAsync(topics);
        }

        SubscriptionError = error;
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (value)
            return;

        foreach (var dashboard in Dashboards)
            dashboard.EndEditing();
    }

    partial void OnSelectedDashboardChanged(DashboardTabViewModel? value)
    {
        // Set while the first ones are loaded, before there is anything new to save.
        if (value is not null && value.Name != _store.Selected && _feeds.Count > 0)
            Save();
    }

    private string UnusedName()
    {
        for (var i = Dashboards.Count + 1; ; i++)
        {
            var name = $"Dashboard {i}";
            if (Dashboards.All(d => !d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return name;
        }
    }

    [RelayCommand]
    private void ToggleEditing() => IsEditing = !IsEditing;

    [RelayCommand]
    private void AddDashboard() => SelectedDashboard = CreateDashboard();

    private void AddFeed(MqttClientViewModel client)
    {
        _feeds[client] = new ClientFeed(client);
        client.PropertyChanged += OnClientChanged;
    }

    private void OnClientChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MqttClientViewModel client)
            return;

        if (e.PropertyName == nameof(MqttClientViewModel.IsConnected))
        {
            foreach (var dashboard in Dashboards)
                dashboard.UpdateClients();
        }
        else if (e.PropertyName == nameof(MqttClientViewModel.Name) && _feeds.TryGetValue(client, out var feed) && feed.Name != client.Name)
        {
            // Graphs know their client by name, so they follow it when it is renamed.
            foreach (var graph in Dashboards.SelectMany(d => d.Graphs).Select(g => g.Profile))
            {
                if (string.Equals(graph.Client, feed.Name, StringComparison.OrdinalIgnoreCase))
                    graph.Client = client.Name;
            }

            feed.Name = client.Name;
            GraphsChanged();
        }
    }

    // A client that is removed leaves its graphs waiting, in case one of that name comes back.
    private void OnClientsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var client in _feeds.Keys.Where(c => !Mqtt.Clients.Contains(c)).ToList())
        {
            client.PropertyChanged -= OnClientChanged;
            _feeds[client].Dispose();
            _feeds.Remove(client);
        }

        foreach (var client in Mqtt.Clients.Where(c => !_feeds.ContainsKey(c)))
            AddFeed(client);

        GraphsChanged(save: false);
    }

    private void OnFlushTick(object? sender, EventArgs e)
    {
        var routes = new Dictionary<(string Client, string Topic), List<GraphCardViewModel>>();
        foreach (var feed in _feeds.Values)
        {
            for (var taken = 0; taken < MaxMessagesPerFlush && feed.TryTake(out var message); taken++)
            {
                if (routes.Count == 0)
                    BuildRoutes(routes);
                if (routes.TryGetValue((feed.Name.ToUpperInvariant(), message.Topic), out var cards))
                {
                    foreach (var card in cards)
                    {
                        card.Receive(message);
                        if (card.HasStatus)
                            card.Status = StatusOf(card);
                    }
                }
            }
        }
    }

    private void BuildRoutes(Dictionary<(string Client, string Topic), List<GraphCardViewModel>> routes)
    {
        foreach (var card in Dashboards.SelectMany(d => d.Graphs))
        {
            foreach (var topic in card.Profile.Topics)
            {
                var key = (card.Profile.Client.ToUpperInvariant(), topic);
                if (!routes.TryGetValue(key, out var cards))
                    routes[key] = cards = [];
                cards.Add(card);
            }
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        foreach (var dashboard in Dashboards)
        {
            var visible = dashboard == SelectedDashboard;
            foreach (var card in dashboard.Graphs)
                card.Tick(now, visible);
        }
    }
}
