using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;

namespace Joktan.TopicDashboards.ViewModels;

public sealed record GraphKindChoice(GraphKind Kind, string Name, string Description);

public sealed record SpanChoice(int Minutes, string Name);

public sealed record DecimalsChoice(int Places, string Name);

public sealed record ColorChoice(int Slot, string Name);

/// <summary>What the graph editor is making: a new graph, or more of one already on the dashboard.</summary>
public sealed record GraphTarget(GraphCardViewModel? Card, string Name);

/// <summary>
/// The graph editor: subscribes to a graph's topics while it is open, offers the numbers that arrive,
/// and previews the graph as it is set up.
/// </summary>
public partial class GraphEditorViewModel : ViewModelBase
{
    private const string SubscriptionOwner = "dashboard-editor";

    private readonly TopicDashboardsViewModel _page;
    private readonly ConcurrentQueue<ReceivedMessage> _incoming = new();
    private readonly Dictionary<(string Topic, string Path), GraphValueChoiceViewModel> _rows = [];
    private readonly HashSet<string> _picked = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer;

    // What the topic explorer started this editor with, if it did.
    private readonly MqttClientViewModel? _seedClient;
    private readonly string? _seedTopic;
    private readonly IReadOnlyList<ReceivedMessage> _seedHistory = [];
    private readonly IReadOnlyList<string> _seedPaths = [];

    private GraphCardViewModel? _card;
    private MqttConnection? _listeningOn;
    private volatile HashSet<string> _listeningTo = new(StringComparer.Ordinal);
    private string? _listenError;
    private bool _loaded;
    private bool _stopped;

    private GraphEditorViewModel(TopicDashboardsViewModel page, DashboardTabViewModel? dashboard, bool _)
    {
        _page = page;
        Clients = page.Mqtt.Clients;
        Dashboards = page.Dashboards;
        SelectedDashboard = dashboard ?? page.SelectedDashboard;
        SelectedClient = page.Mqtt.SelectedClient ?? page.Mqtt.Clients.FirstOrDefault();
        SelectedKind = Kinds[0];
        SelectedSpan = Spans[1];
        SelectedDecimals = DecimalChoices[0];
        Preview = new GraphCardViewModel(new GraphProfile(), Data);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, OnTick);
        _timer.Start();
    }

    /// <summary>A new graph, starting from nothing.</summary>
    public GraphEditorViewModel(TopicDashboardsViewModel page, DashboardTabViewModel dashboard)
        : this(page, dashboard, false)
    {
        Load(null);
    }

    /// <summary>A topic seen in the topic explorer, for a new graph or one on the dashboard.</summary>
    /// <param name="history">The messages already received on it, oldest first.</param>
    public GraphEditorViewModel(TopicDashboardsViewModel page, MqttClientViewModel client, string topic,
        IEnumerable<ReceivedMessage> history, IEnumerable<string> paths)
        : this(page, null, false)
    {
        _seedClient = client;
        _seedTopic = topic;
        _seedHistory = history.ToList();
        _seedPaths = paths.ToList();
        UpdateTargets();
        Load(null);
    }

    /// <summary>A graph already on a dashboard.</summary>
    public GraphEditorViewModel(TopicDashboardsViewModel page, DashboardTabViewModel dashboard, GraphCardViewModel card)
        : this(page, dashboard, false)
    {
        Load(card);
    }

    /// <summary>Saved or not, once the window can close.</summary>
    public event Action<bool>? CloseRequested;

    public IReadOnlyList<MqttClientViewModel> Clients { get; }

    public IReadOnlyList<DashboardTabViewModel> Dashboards { get; }

    public GraphData Data { get; private set; } = new();

    /// <summary>The topics of the graph, each subscribed to while the editor is open.</summary>
    public ObservableCollection<string> Topics { get; } = [];

    public bool HasSeveralTopics => Topics.Count > 1;

    public bool HasTopics => Topics.Count > 0;

    /// <summary>Whether the editor started from a topic in the topic explorer, which can join a graph already there.</summary>
    public bool CanChooseTarget => _seedTopic is not null;

    /// <summary>What can be made from the topic explorer's topic on the dashboard chosen.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GraphTarget> Targets { get; private set; } = [];

    [ObservableProperty]
    public partial GraphTarget? SelectedTarget { get; set; }

    /// <summary>The graph as it would look.</summary>
    public GraphCardViewModel Preview { get; }

    public bool IsNew => _card is null;

    /// <summary>Whether the dashboard can be chosen, which is not so for a graph already on one, unless the choice is which graph.</summary>
    public bool CanChooseDashboard => IsNew || CanChooseTarget;

    public string WindowTitle => IsNew ? "Add graph" : CanChooseTarget ? "Add to graph" : "Edit graph";

    public string SaveText => IsNew ? "Add to dashboard" : CanChooseTarget ? "Add to graph" : "Save";

    public IReadOnlyList<GraphKindChoice> Kinds { get; } =
    [
        new(GraphKind.Line, "Line", "How values moved over time. Use it to compare numbers on one scale."),
        new(GraphKind.Area, "Area", "A line filled down to zero, for amounts."),
        new(GraphKind.Bars, "Bars", "One number: its recent readings. Several numbers: the latest of each, side by side."),
        new(GraphKind.Gauge, "Gauge", "The latest value on a dial, best with a fixed scale."),
        new(GraphKind.Value, "Number", "The latest value in large type, with its trend below."),
    ];

    public IReadOnlyList<SpanChoice> Spans { get; } =
    [
        new(1, "1 minute"),
        new(5, "5 minutes"),
        new(15, "15 minutes"),
        new(30, "30 minutes"),
        new(60, "1 hour"),
        new(360, "6 hours"),
        new(1440, "24 hours"),
    ];

    public IReadOnlyList<DecimalsChoice> DecimalChoices { get; } =
    [
        new(-1, "Automatic"),
        new(0, "0"),
        new(1, "1"),
        new(2, "2"),
        new(3, "3"),
    ];

    public static IReadOnlyList<ColorChoice> ColorChoices { get; } = GraphColors.Names.Select((name, slot) => new ColorChoice(slot, name)).ToList();

    public IReadOnlyList<int> Sizes { get; } = Enumerable.Range(1, GraphProfile.MaxHeight).ToList();

    public ObservableCollection<GraphValueChoiceViewModel> Values { get; } = [];

    [ObservableProperty]
    public partial MqttClientViewModel? SelectedClient { get; set; }

    /// <summary>A topic typed in, to add to the graph.</summary>
    [ObservableProperty]
    public partial string NewTopic { get; set; } = "";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindDescription))]
    public partial GraphKindChoice SelectedKind { get; set; }

    public string KindDescription => SelectedKind.Description;

    [ObservableProperty]
    public partial string Unit { get; set; } = "";

    [ObservableProperty]
    public partial SpanChoice SelectedSpan { get; set; }

    [ObservableProperty]
    public partial DecimalsChoice SelectedDecimals { get; set; }

    /// <summary>Whether the scale stays between the numbers given, rather than fitting the values.</summary>
    [ObservableProperty]
    public partial bool UseFixedScale { get; set; }

    [ObservableProperty]
    public partial string MinimumText { get; set; } = "";

    [ObservableProperty]
    public partial string MaximumText { get; set; } = "";

    [ObservableProperty]
    public partial int WidthSpan { get; set; } = 1;

    [ObservableProperty]
    public partial int HeightSpan { get; set; } = 1;

    [ObservableProperty]
    public partial DashboardTabViewModel? SelectedDashboard { get; set; }

    /// <summary>Whether the graph goes on a new dashboard, rather than the one selected.</summary>
    [ObservableProperty]
    public partial bool IsNewDashboard { get; set; }

    [ObservableProperty]
    public partial string NewDashboardName { get; set; } = "";

    /// <summary>A path to add by hand, for a topic with nothing arriving on it yet.</summary>
    [ObservableProperty]
    public partial string ManualPath { get; set; } = "";

    /// <summary>The topic a path added by hand is on.</summary>
    [ObservableProperty]
    public partial string? ManualTopic { get; set; }

    public bool IsListening => _listeningOn is not null && _listeningTo.Count > 0;

    [ObservableProperty]
    public partial string ListenStatus { get; private set; } = "";

    public bool HasNoNumbers => Values.Count == 0;

    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>Adds the topic typed in, and subscribes to it for as long as the editor is open.</summary>
    [RelayCommand]
    private async Task AddTopicAsync()
    {
        if (TryAddNewTopic() is { } problem)
        {
            Error = problem;
            return;
        }

        await ListenAsync();
    }

    // Returns what is wrong with the topic typed in, or null.
    private string? TryAddNewTopic()
    {
        var topic = NewTopic.Trim();
        if (TopicRules.Validate(topic) is { } problem)
            return $"{problem}. Each topic is one exact topic, so wildcards cannot be used.";

        Error = null;
        NewTopic = "";
        if (Topics.Contains(topic))
            return null;

        AddTopicEntry(topic);
        if (Name.Trim().Length == 0)
            Name = LastLevel(topic);
        UpdatePreview();
        return null;
    }

    private void AddTopicEntry(string topic)
    {
        if (Topics.Contains(topic))
            return;

        Topics.Add(topic);
        ManualTopic = topic;
        NotifyTopicsChanged();
    }

    [RelayCommand]
    private async Task RemoveTopicAsync(string topic)
    {
        Topics.Remove(topic);
        _picked.Remove(topic);
        Data.Remove(topic);
        foreach (var key in _rows.Keys.Where(k => k.Topic == topic).ToList())
            _rows.Remove(key);

        if (ManualTopic == topic)
            ManualTopic = Topics.LastOrDefault();
        NotifyTopicsChanged();
        UpdateRows();
        UpdatePreview();
        await ListenAsync();
    }

    private void NotifyTopicsChanged()
    {
        OnPropertyChanged(nameof(HasSeveralTopics));
        OnPropertyChanged(nameof(HasTopics));
    }

    // Subscribes to the topics listed, on the client chosen, and to nothing else.
    private async Task ListenAsync()
    {
        if (SelectedClient is not { } client || _stopped)
            return;

        var connection = client.Connection;
        if (_listeningOn != connection)
            await StopListeningAsync();

        var topics = Topics.ToHashSet(StringComparer.Ordinal);
        _listeningTo = topics;
        if (_listeningOn is null)
        {
            _listeningOn = connection;
            connection.MessageReceived += OnMessageReceived;
        }

        _listenError = await connection.SetSubscriptionsAsync(SubscriptionOwner, topics);
        UpdateListenStatus();
    }

    private void UpdateListenStatus()
    {
        ListenStatus = SelectedClient is not { } client || Topics.Count == 0 ? ""
            : _listenError is { } error ? error
            : !client.IsConnected ? $"{client.Name} is not connected. Numbers appear below once it is connected and messages arrive."
            : Topics.Count == 1 ? $"Listening on {Topics[0]}. Numbers appear below as messages arrive."
            : $"Listening on {Topics.Count} topics. Numbers appear below as messages arrive.";
    }

    [RelayCommand]
    private void AddManualPath()
    {
        if (ManualTopic is not { } topic)
        {
            Error = "Add a topic first.";
            return;
        }

        var row = Row(topic, ManualPath.Trim());
        if (!row.IsChosen && Chosen().Count() >= GraphProfile.MaxValues)
        {
            Error = $"A graph can draw at most {GraphProfile.MaxValues} numbers.";
            return;
        }

        Choose(row);
        _picked.Add(topic);
        Error = null;
        ManualPath = "";
        UpdateRows();
        UpdatePreview();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private async Task SaveAsync()
    {
        // A topic typed in but not yet added is taken to be meant.
        if (NewTopic.Trim().Length > 0 && TryAddNewTopic() is { } topicProblem)
        {
            Error = topicProblem;
            return;
        }

        if (Check() is { } problem)
        {
            Error = problem;
            return;
        }

        // Whatever has just arrived goes on the graph too.
        while (_incoming.TryDequeue(out var message))
            Data.Add(message);

        var dashboard = IsNewDashboard ? _page.CreateDashboard(NewDashboardName.Trim()) : SelectedDashboard!;
        var profile = BuildProfile();
        _page.Commit(dashboard, _card, profile, Data);
        AddedTo = _card is null ? dashboard.DisplayName : $"{profile.Name} on {dashboard.DisplayName}";
        await StopAsync();
        CloseRequested?.Invoke(true);
    }

    /// <summary>Where the graph went, once saved.</summary>
    public string? AddedTo { get; private set; }

    /// <summary>Stops listening. Called when the window closes, however that happens.</summary>
    public async Task StopAsync()
    {
        if (_stopped)
            return;

        _stopped = true;
        _timer.Stop();
        await StopListeningAsync();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loaded && e.PropertyName is nameof(Name) or nameof(Unit) or nameof(SelectedKind) or nameof(SelectedSpan) or nameof(SelectedDecimals)
            or nameof(UseFixedScale) or nameof(MinimumText) or nameof(MaximumText))
            UpdatePreview();
    }

    // What was received came through the old client, so it starts again. The topics stay.
    partial void OnSelectedClientChanged(MqttClientViewModel? value)
    {
        if (!_loaded)
            return;

        Data.Clear();
        foreach (var key in _rows.Where(r => !r.Value.IsChosen).Select(r => r.Key).ToList())
            _rows.Remove(key);

        UpdateRows();
        UpdatePreview();
        _ = ListenAsync();
    }

    partial void OnSelectedDashboardChanged(DashboardTabViewModel? value) => UpdateTargets();

    partial void OnIsNewDashboardChanged(bool value) => UpdateTargets();

    partial void OnSelectedTargetChanged(GraphTarget? value)
    {
        if (_loaded && value is not null && value.Card != _card)
            Load(value.Card);
    }

    // Graphs already on the dashboard, that use the same client, are offered as well as a new one.
    private void UpdateTargets()
    {
        if (_seedTopic is null)
            return;

        var graphs = IsNewDashboard ? [] : SelectedDashboard?.Graphs
            .Where(g => string.Equals(g.Profile.Client, _seedClient?.Name, StringComparison.OrdinalIgnoreCase))
            .Select(g => new GraphTarget(g, g.Name)) ?? [];
        Targets = graphs.Prepend(new GraphTarget(null, "A new graph")).ToList();
        SelectedTarget = Targets[0];
    }

    // Sets everything from a graph, or to how a new one starts; then adds the topic explorer's topic.
    private void Load(GraphCardViewModel? card)
    {
        _loaded = false;
        _rows.Clear();
        _picked.Clear();
        Topics.Clear();
        Values.Clear();
        ManualTopic = null;
        Error = null;
        _card = card;
        Data = card?.Data.Copy() ?? new GraphData();

        var graph = card?.Profile ?? new GraphProfile();
        if (card is not null)
            SelectedClient = _page.FindClient(graph.Client) ?? SelectedClient;
        else if (_seedClient is not null)
            SelectedClient = _seedClient;

        Name = graph.Name;
        Unit = graph.Unit;
        SelectedKind = Kinds.First(k => k.Kind == graph.Kind);
        SelectedSpan = Spans.FirstOrDefault(s => s.Minutes == graph.SpanMinutes) ?? Spans[1];
        SelectedDecimals = DecimalChoices.FirstOrDefault(d => d.Places == graph.Decimals) ?? DecimalChoices[0];
        UseFixedScale = graph.Minimum is not null;
        MinimumText = graph.Minimum?.ToString(CultureInfo.CurrentCulture) ?? "";
        MaximumText = graph.Maximum?.ToString(CultureInfo.CurrentCulture) ?? "";
        WidthSpan = graph.Width;
        HeightSpan = graph.Height;

        foreach (var value in graph.Values)
        {
            AddTopicEntry(value.Topic);
            _picked.Add(value.Topic);
            var row = Row(value.Topic, value.Path);
            row.Show(true);
            row.Label = value.Label;
            row.Color = value.Color;
        }

        if (_seedTopic is { } topic)
        {
            // A graph already drawing the topic has these messages, and its own choice of numbers.
            if (!Topics.Contains(topic))
            {
                foreach (var message in _seedHistory)
                    Data.Add(message);

                foreach (var path in _seedPaths)
                {
                    if (Chosen().Count() < GraphProfile.MaxValues)
                    {
                        Choose(Row(topic, path));
                        _picked.Add(topic);
                    }
                }

                AddTopicEntry(topic);
            }

            if (card is null)
                Name = LastLevel(topic);
        }

        UpdateRows();
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(CanChooseDashboard));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(SaveText));
        _loaded = true;
        UpdatePreview();
        if (Topics.Count > 0)
            _ = ListenAsync();
    }

    private async Task StopListeningAsync()
    {
        _listenError = null;
        var connection = _listeningOn;
        _listeningOn = null;
        _listeningTo = new HashSet<string>(StringComparer.Ordinal);
        if (connection is null)
            return;

        connection.MessageReceived -= OnMessageReceived;
        await connection.SetSubscriptionsAsync(SubscriptionOwner, []);
    }

    // Arrives on the MQTT client's thread.
    private void OnMessageReceived(ReceivedMessage message)
    {
        if (_listeningTo.Contains(message.Topic))
            _incoming.Enqueue(message);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        UpdateListenStatus();
        var added = false;
        while (_incoming.TryDequeue(out var message))
        {
            // A topic removed a moment ago may still have messages on their way.
            if (Topics.Contains(message.Topic))
                added |= Data.Add(message);
        }

        if (added)
        {
            UpdateRows();
            UpdatePreview();
        }
        else
        {
            Preview.Tick(DateTime.Now, visible: true);
        }
    }

    private GraphValueChoiceViewModel Row(string topic, string path)
    {
        if (!_rows.TryGetValue((topic, path), out var row))
            _rows[(topic, path)] = row = new GraphValueChoiceViewModel(topic, path, UnusedColor(), OnToggled, UpdatePreview);

        return row;
    }

    private int UnusedColor()
    {
        var used = Chosen().Select(r => r.Color).ToHashSet();
        return Enumerable.Range(0, GraphColors.Count).First(c => !used.Contains(c) || used.Count >= GraphColors.Count);
    }

    // A number keeps its colour unless another took it while it was not ticked.
    private void Choose(GraphValueChoiceViewModel row)
    {
        if (Chosen().Any(r => r != row && r.Color == row.Color))
            row.Color = UnusedColor();
        row.Show(true);
    }

    private void OnToggled(GraphValueChoiceViewModel row)
    {
        Error = null;
        _picked.Add(row.Topic);
        if (row.IsChosen && Chosen().Count() > GraphProfile.MaxValues)
        {
            row.Show(false);
            Error = $"A graph can draw at most {GraphProfile.MaxValues} numbers.";
            return;
        }

        if (row.IsChosen)
        {
            row.Show(false);
            Choose(row);
        }

        UpdatePreview();
    }

    private IEnumerable<GraphValueChoiceViewModel> Chosen() => _rows.Values.Where(r => r.IsChosen);

    // Lists the numbers received, and those chosen that have not arrived, topic by topic in a steady order.
    private void UpdateRows()
    {
        var order = new List<GraphValueChoiceViewModel>();
        foreach (var topic in Topics)
        {
            var paths = Data.OrderedPaths(topic).ToList();
            foreach (var path in paths)
            {
                var row = Row(topic, path);
                var samples = Data.Series(topic, path).Samples;
                row.LatestText = samples.Count > 0 ? samples[^1].Value.ToString("0.###") : "";
                order.Add(row);
            }

            order.AddRange(_rows.Values.Where(r => r.Topic == topic && !paths.Contains(r.Path)));

            // The first number to arrive on a topic is drawn, so that there is something to see at once.
            if (!_picked.Contains(topic)
                && paths.Where(p => !JsonNumbers.IsOneItem(p)).Select(p => (string?)p).FirstOrDefault() is { } first
                && Chosen().Count() < GraphProfile.MaxValues)
            {
                _picked.Add(topic);
                Choose(Row(topic, first));
            }
        }

        if (!Values.SequenceEqual(order))
        {
            Values.Clear();
            foreach (var row in order)
                Values.Add(row);
            OnPropertyChanged(nameof(HasNoNumbers));
        }
    }

    private void UpdatePreview()
    {
        if (!_loaded)
            return;

        Preview.Apply(BuildProfile(), Data);
        Preview.Status = Preview.Series.Count == 0 ? "Tick a number below to draw it"
            : !Preview.HasReadings ? "Waiting for a message"
            : null;
    }

    private GraphProfile BuildProfile()
    {
        var hasScale = TryParse(MinimumText, out var minimum) & TryParse(MaximumText, out var maximum) && minimum < maximum;
        return new GraphProfile
        {
            Name = Name.Trim(),
            Client = SelectedClient?.Name ?? "",
            Kind = SelectedKind.Kind,
            Unit = Unit.Trim(),
            SpanMinutes = SelectedSpan.Minutes,
            Decimals = SelectedDecimals.Places,
            Minimum = UseFixedScale && hasScale ? minimum : null,
            Maximum = UseFixedScale && hasScale ? maximum : null,
            Width = WidthSpan,
            Height = HeightSpan,
            Values = Values.Where(r => r.IsChosen)
                .Select(r => new GraphValue { Topic = r.Topic, Path = r.Path, Label = r.Label.Trim(), Color = r.Color })
                .ToList(),
        };
    }

    private string? Check()
    {
        if (Name.Trim().Length == 0)
            return "Enter a name for the graph.";
        if (SelectedClient is null)
            return "Choose a client.";
        if (Topics.Count == 0)
            return "Add a topic to draw.";
        if (!Values.Any(r => r.IsChosen))
            return "Tick at least one number to draw.";

        // A graph only keeps the topics it draws, so this one would quietly go.
        if (Topics.FirstOrDefault(t => !Chosen().Any(r => r.Topic == t)) is { } unused)
            return $"Nothing is ticked on {unused}. Tick a number on it, add one by its path, or remove the topic.";
        if (UseFixedScale && !(TryParse(MinimumText, out var minimum) & TryParse(MaximumText, out var maximum) && minimum < maximum))
            return "A fixed scale needs a minimum below its maximum.";
        if (CanChooseDashboard && IsNewDashboard && NewDashboardName.Trim().Length == 0)
            return "Enter a name for the new dashboard.";
        if (CanChooseDashboard && !IsNewDashboard && SelectedDashboard is null)
            return "Choose a dashboard.";
        return null;
    }

    private static bool TryParse(string text, out double value)
        => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);

    private static string LastLevel(string topic) => topic[(topic.LastIndexOf('/') + 1)..];
}
