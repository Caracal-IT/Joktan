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

/// <summary>
/// The graph editor: subscribes to a topic while it is open, offers the numbers that arrive, and
/// previews the graph as it is set up.
/// </summary>
public partial class GraphEditorViewModel : ViewModelBase
{
    private const string SubscriptionOwner = "dashboard-editor";

    private readonly TopicDashboardsViewModel _page;
    private readonly GraphCardViewModel? _card;
    private readonly ConcurrentQueue<ReceivedMessage> _incoming = new();
    private readonly Dictionary<string, GraphValueChoiceViewModel> _rows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer;
    private MqttConnection? _listeningOn;
    private volatile string? _listeningTo;
    private string? _listenError;
    private bool _userChose;
    private bool _loaded;
    private bool _stopped;

    private GraphEditorViewModel(TopicDashboardsViewModel page, DashboardTabViewModel? dashboard, GraphCardViewModel? card, bool _)
    {
        _page = page;
        _card = card;
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
        : this(page, dashboard, null, false)
    {
        _loaded = true;
        UpdatePreview();
    }

    /// <summary>A new graph on a topic seen in the topic explorer.</summary>
    /// <param name="history">The messages already received on it, oldest first.</param>
    public GraphEditorViewModel(TopicDashboardsViewModel page, MqttClientViewModel client, string topic,
        IEnumerable<ReceivedMessage> history, IEnumerable<string> paths)
        : this(page, null, null, false)
    {
        SelectedClient = client;
        Topic = topic;
        Name = LastLevel(topic);
        foreach (var message in history)
            Data.Add(message);

        foreach (var path in paths.Take(GraphProfile.MaxValues))
            Row(path).Show(true);
        UpdateRows();
        _loaded = true;
        UpdatePreview();
        _ = ListenAsync();
    }

    /// <summary>A graph already on a dashboard.</summary>
    public GraphEditorViewModel(TopicDashboardsViewModel page, DashboardTabViewModel dashboard, GraphCardViewModel card)
        : this(page, dashboard, card, false)
    {
        var graph = card.Profile;
        Data = card.Data.Copy();
        SelectedClient = page.FindClient(graph.Client) ?? SelectedClient;
        Topic = graph.Topic;
        Name = graph.Name;
        Unit = graph.Unit;
        SelectedKind = Kinds.First(k => k.Kind == graph.Kind);
        if (Spans.FirstOrDefault(s => s.Minutes == graph.SpanMinutes) is { } span)
            SelectedSpan = span;
        if (DecimalChoices.FirstOrDefault(d => d.Places == graph.Decimals) is { } decimals)
            SelectedDecimals = decimals;
        UseFixedScale = graph.Minimum is not null;
        MinimumText = graph.Minimum?.ToString(CultureInfo.CurrentCulture) ?? "";
        MaximumText = graph.Maximum?.ToString(CultureInfo.CurrentCulture) ?? "";
        WidthSpan = graph.Width;
        HeightSpan = graph.Height;
        foreach (var value in graph.Values)
        {
            var row = Row(value.Path);
            row.Show(true);
            row.Label = value.Label;
            row.Color = value.Color;
        }

        _userChose = true;
        UpdateRows();
        _loaded = true;
        UpdatePreview();
        _ = ListenAsync();
    }

    /// <summary>Saved or not, once the window can close.</summary>
    public event Action<bool>? CloseRequested;

    public IReadOnlyList<MqttClientViewModel> Clients { get; }

    public IReadOnlyList<DashboardTabViewModel> Dashboards { get; }

    public GraphData Data { get; private set; } = new();

    /// <summary>The graph as it would look.</summary>
    public GraphCardViewModel Preview { get; }

    public bool IsNew => _card is null;

    public string WindowTitle => IsNew ? "Add graph" : "Edit graph";

    public string SaveText => IsNew ? "Add to dashboard" : "Save";

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

    [ObservableProperty]
    public partial string Topic { get; set; } = "";

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListenText))]
    [NotifyCanExecuteChangedFor(nameof(ListenCommand))]
    public partial bool IsListening { get; private set; }

    public string ListenText => IsListening ? "Subscribed" : "Subscribe";

    [ObservableProperty]
    public partial string ListenStatus { get; private set; } = "";

    public bool HasNoNumbers => Values.Count == 0;

    [ObservableProperty]
    public partial string? Error { get; private set; }

    private bool CanListen => !IsListening;

    /// <summary>Subscribes to the topic for as long as the editor is open.</summary>
    [RelayCommand(CanExecute = nameof(CanListen))]
    private async Task ListenAsync()
    {
        var topic = Topic.Trim();
        if (SelectedClient is not { } client)
        {
            ListenStatus = "Choose a client first.";
            return;
        }

        if (TopicRules.Validate(topic) is { } problem)
        {
            ListenStatus = $"{problem}. A graph draws one topic, so wildcards cannot be used.";
            return;
        }

        if (Name.Trim().Length == 0)
            Name = LastLevel(topic);

        await StopListeningAsync();
        _listeningOn = client.Connection;
        _listeningOn.MessageReceived += OnMessageReceived;
        _listeningTo = topic;
        IsListening = true;
        _listenError = await _listeningOn.SetSubscriptionsAsync(SubscriptionOwner, [topic]);
        UpdateListenStatus();
    }

    private void UpdateListenStatus()
    {
        if (IsListening && SelectedClient is { } client)
        {
            ListenStatus = _listenError ?? (client.IsConnected
                ? $"Listening on {Topic.Trim()}. Numbers appear below as messages arrive."
                : $"{client.Name} is not connected. Numbers appear below once it is connected and messages arrive.");
        }
    }

    [RelayCommand]
    private void AddManualPath()
    {
        var path = ManualPath.Trim();
        var row = Row(path);
        if (!row.IsChosen)
        {
            if (Chosen().Count() >= GraphProfile.MaxValues)
            {
                Error = $"A graph can draw at most {GraphProfile.MaxValues} numbers.";
                return;
            }

            row.Show(true);
        }

        _userChose = true;
        ManualPath = "";
        UpdateRows();
        UpdatePreview();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Check() is { } problem)
        {
            Error = problem;
            return;
        }

        // Whatever has just arrived goes on the graph too.
        while (_incoming.TryDequeue(out var message))
            Data.Add(message);

        var dashboard = IsNewDashboard ? _page.CreateDashboard(NewDashboardName.Trim()) : SelectedDashboard!;
        _page.Commit(dashboard, _card, BuildProfile(), Data);
        AddedTo = dashboard.DisplayName;
        await StopAsync();
        CloseRequested?.Invoke(true);
    }

    /// <summary>The name of the dashboard the graph went on, once saved.</summary>
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

    partial void OnSelectedClientChanged(MqttClientViewModel? value) => SourceChanged();

    partial void OnTopicChanged(string value) => SourceChanged();

    // What was received belongs to the old topic, so it starts again. Numbers chosen stay, as a
    // similar topic is likely to have the same ones.
    private void SourceChanged()
    {
        if (!_loaded)
            return;

        _ = StopListeningAsync();
        Data.Clear();
        foreach (var row in Values.Where(r => !r.IsChosen).ToList())
        {
            Values.Remove(row);
            _rows.Remove(row.Path);
        }

        OnPropertyChanged(nameof(HasNoNumbers));
        ListenStatus = "";
        UpdatePreview();
    }

    private async Task StopListeningAsync()
    {
        IsListening = false;
        _listenError = null;
        if (_listeningOn is not { } connection)
            return;

        connection.MessageReceived -= OnMessageReceived;
        _listeningOn = null;
        _listeningTo = null;
        await connection.SetSubscriptionsAsync(SubscriptionOwner, []);
    }

    // Arrives on the MQTT client's thread.
    private void OnMessageReceived(ReceivedMessage message)
    {
        if (message.Topic == _listeningTo)
            _incoming.Enqueue(message);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        UpdateListenStatus();
        var added = false;
        while (_incoming.TryDequeue(out var message))
            added |= Data.Add(message);

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

    private GraphValueChoiceViewModel Row(string path)
    {
        if (!_rows.TryGetValue(path, out var row))
        {
            var used = _rows.Values.Select(r => r.Color).ToHashSet();
            var color = Enumerable.Range(0, GraphColors.Count).Where(c => !used.Contains(c)).DefaultIfEmpty(_rows.Count % GraphColors.Count).First();
            _rows[path] = row = new GraphValueChoiceViewModel(path, color, OnToggled, UpdatePreview);
        }

        return row;
    }

    private void OnToggled(GraphValueChoiceViewModel row)
    {
        _userChose = true;
        Error = null;
        if (row.IsChosen && Chosen().Count() > GraphProfile.MaxValues)
        {
            row.Show(false);
            Error = $"A graph can draw at most {GraphProfile.MaxValues} numbers.";
            return;
        }

        UpdatePreview();
    }

    private IEnumerable<GraphValueChoiceViewModel> Chosen() => _rows.Values.Where(r => r.IsChosen);

    // Lists the numbers received, and those chosen that have not arrived, in a steady order.
    private void UpdateRows()
    {
        var order = Data.OrderedPaths().ToList();
        foreach (var path in order)
        {
            var row = Row(path);
            var samples = Data.Series(path).Samples;
            row.LatestText = samples.Count > 0 ? samples[^1].Value.ToString("0.###") : "";
        }

        order.AddRange(_rows.Keys.Where(p => !order.Contains(p)));
        if (!Values.Select(r => r.Path).SequenceEqual(order))
        {
            Values.Clear();
            foreach (var path in order)
                Values.Add(_rows[path]);
            OnPropertyChanged(nameof(HasNoNumbers));
        }

        // The first number to arrive is drawn, so that there is something to see at once.
        if (!_userChose && !Chosen().Any() && order.FirstOrDefault(p => !JsonNumbers.IsOneItem(p)) is { } first)
            _rows[first].Show(true);
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
            Topic = Topic.Trim(),
            Kind = SelectedKind.Kind,
            Unit = Unit.Trim(),
            SpanMinutes = SelectedSpan.Minutes,
            Decimals = SelectedDecimals.Places,
            Minimum = UseFixedScale && hasScale ? minimum : null,
            Maximum = UseFixedScale && hasScale ? maximum : null,
            Width = WidthSpan,
            Height = HeightSpan,
            Values = Values.Where(r => r.IsChosen)
                .Select(r => new GraphValue { Path = r.Path, Label = r.Label.Trim(), Color = r.Color })
                .ToList(),
        };
    }

    private string? Check()
    {
        if (Name.Trim().Length == 0)
            return "Enter a name for the graph.";
        if (SelectedClient is null)
            return "Choose a client.";
        if (TopicRules.Validate(Topic.Trim()) is { } problem)
            return $"{problem}.";
        if (!Values.Any(r => r.IsChosen))
            return "Tick at least one number to draw.";
        if (UseFixedScale && !(TryParse(MinimumText, out var minimum) & TryParse(MaximumText, out var maximum) && minimum < maximum))
            return "A fixed scale needs a minimum below its maximum.";
        if (IsNew && IsNewDashboard && NewDashboardName.Trim().Length == 0)
            return "Enter a name for the new dashboard.";
        if (IsNew && !IsNewDashboard && SelectedDashboard is null)
            return "Choose a dashboard.";
        return null;
    }

    private static bool TryParse(string text, out double value)
        => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);

    private static string LastLevel(string topic) => topic[(topic.LastIndexOf('/') + 1)..];
}
