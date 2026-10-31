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

    private readonly DashboardStore _store;
    private readonly Dictionary<MqttClientViewModel, ClientFeed> _feeds = [];
    private readonly DispatcherTimer _flushTimer;
    private readonly DispatcherTimer _tickTimer;

    public TopicDashboardsViewModel(MqttViewModel mqtt, DashboardStore? store = null)
        : base("Topic Dashboards", "Dashboard / Topic Dashboards")
    {
        Mqtt = mqtt;
        _store = store ?? DashboardStore.Load();
        if (_store.Dashboards.Count == 0)
            _store.Dashboards.Add(new DashboardProfile { Name = FirstDashboardName });

        foreach (var profile in _store.Dashboards)
            Dashboards.Add(new DashboardTabViewModel(profile, this));
        SelectedDashboard = Dashboards.FirstOrDefault(d => d.Name == _store.Selected) ?? Dashboards[0];
        UpdateRemovable();

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

    /// <summary>Whether a dashboard can be removed; the last one cannot.</summary>
    [ObservableProperty]
    public partial bool CanRemoveDashboards { get; private set; }

    /// <summary>Why a subscription failed, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubscriptionError))]
    public partial string? SubscriptionError { get; private set; }

    public bool HasSubscriptionError => SubscriptionError is not null;

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
        return client.IsConnected
            ? $"Waiting for a message on {card.Profile.Topic}"
            : $"Connect {client.Name} to receive {card.Profile.Topic}";
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
        UpdateRemovable();
        return dashboard;
    }

    public void Remove(DashboardTabViewModel dashboard)
    {
        if (Dashboards.Count <= 1)
            return;

        var index = Dashboards.IndexOf(dashboard);
        Dashboards.Remove(dashboard);
        _store.Dashboards.Remove(dashboard.Profile);
        UpdateRemovable();
        if (SelectedDashboard == dashboard || SelectedDashboard is null)
            SelectedDashboard = Dashboards[Math.Min(index, Dashboards.Count - 1)];
        GraphsChanged();
    }

    /// <summary>Applies what the graph editor was left with.</summary>
    public void Commit(DashboardTabViewModel dashboard, GraphCardViewModel? card, GraphProfile profile, GraphData data)
    {
        data.Keep(profile.Values.Select(v => v.Path).ToHashSet(StringComparer.Ordinal));
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
        _store.Save();
    }

    /// <summary>Writes anything not yet on disk. Called when the app exits.</summary>
    public void SaveState()
    {
        Save();
        _store.FlushAsync().GetAwaiter().GetResult();
    }

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
                .Select(g => g.Topic)
                .Where(t => TopicRules.Validate(t) is null)
                .ToHashSet(StringComparer.Ordinal);
            error ??= await feed.SetTopicsAsync(topics);
        }

        SubscriptionError = error;
    }

    partial void OnSelectedDashboardChanged(DashboardTabViewModel? value)
    {
        // Set while the first ones are loaded, before there is anything new to save.
        if (value is not null && value.Name != _store.Selected && _feeds.Count > 0)
            Save();
    }

    private void UpdateRemovable()
    {
        CanRemoveDashboards = Dashboards.Count > 1;
        foreach (var dashboard in Dashboards)
            dashboard.RefreshCanRemove();
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
            var key = (card.Profile.Client.ToUpperInvariant(), card.Profile.Topic);
            if (!routes.TryGetValue(key, out var cards))
                routes[key] = cards = [];
            cards.Add(card);
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
