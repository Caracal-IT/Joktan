using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Mqtt;

namespace Joktan.ViewModels;

/// <summary>
/// One tab of the topic explorer: subscribes to a broker through one client and shows every topic
/// that carries messages as a tree, with the latest value of each. Every tab listens on its own,
/// whether it is showing or not.
/// </summary>
public partial class ClientExplorerViewModel : ViewModelBase, IDisposable
{
    private const string DefaultFilters = "#";
    private const int MaxMessagesPerFlush = 5000;

    private readonly MqttConnection _connection;
    private readonly Action _save;
    private readonly ConcurrentQueue<ReceivedMessage> _incoming = new();
    private readonly Dictionary<string, TopicNodeViewModel> _rootIndex = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _flushTimer;
    private bool _activated;
    private int _topicCount;
    private long _messageCount;

    /// <param name="save">Saves every client's settings, after this tab's filters have changed.</param>
    public ClientExplorerViewModel(MqttClientViewModel client, Action save)
    {
        Client = client;
        _save = save;
        _connection = client.Connection;
        _connection.MessageReceived += OnMessageReceived;
        client.PropertyChanged += OnClientChanged;
        Filters = client.Profile.ExplorerFilters is { Length: > 0 } saved ? saved : DefaultFilters;

        _flushTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, OnFlushTick);
        _flushTimer.Stop();
        UpdateSummary();
    }

    /// <summary>The client this tab listens through; its settings and connection belong to the MQTT page.</summary>
    public MqttClientViewModel Client { get; }

    public ObservableCollection<TopicNodeViewModel> Roots { get; } = [];

    /// <summary>Topic filters to subscribe to, separated by commas.</summary>
    [ObservableProperty]
    public partial string Filters { get; set; } = DefaultFilters;

    [ObservableProperty]
    public partial string? FilterError { get; set; }

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    /// <summary>While paused, messages that arrive are discarded.</summary>
    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ShownMessage), nameof(HasShownMessage))]
    [NotifyCanExecuteChangedFor(nameof(ClearRetainedCommand))]
    public partial TopicNodeViewModel? SelectedNode { get; set; }

    /// <summary>A message picked from the history; null shows the latest one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownMessage), nameof(HasShownMessage))]
    public partial ReceivedMessage? SelectedHistoryMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PublishTopicError))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    public partial string PublishTopic { get; set; } = "";

    [ObservableProperty]
    public partial string PublishBody { get; set; } = "";

    [ObservableProperty]
    public partial int PublishQos { get; set; }

    [ObservableProperty]
    public partial bool PublishRetain { get; set; }

    [ObservableProperty]
    public partial string PublishStatus { get; set; } = "";

    public bool HasSelection => SelectedNode is not null;

    public ReceivedMessage? ShownMessage => SelectedHistoryMessage ?? SelectedNode?.LastMessage;

    public bool HasShownMessage => ShownMessage is not null;

    public string? PublishTopicError => PublishTopic.Length == 0 ? null : TopicRules.Validate(PublishTopic);

    private bool CanPublish => Client.IsConnected && TopicRules.Validate(PublishTopic) is null;

    private bool CanClearRetained => Client.IsConnected && SelectedNode is { IsRetained: true };

    /// <summary>Starts listening. Called the first time the explorer is shown.</summary>
    public void Activate()
    {
        if (_activated)
            return;

        _activated = true;
        _flushTimer.Start();
        _ = ApplyFiltersAsync();
    }

    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        var filters = Filters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (filters.Length == 0)
        {
            FilterError = "Enter at least one topic filter, for example #";
            return;
        }

        FilterError = filters.Select(TopicRules.ValidateFilter).FirstOrDefault(e => e is not null);
        if (FilterError is null)
            FilterError = await _connection.SetSubscriptionsAsync(filters);

        if (FilterError is null && Client.Profile.ExplorerFilters != Filters)
        {
            Client.Profile.ExplorerFilters = Filters;
            _save();
        }
    }

    [RelayCommand]
    private void Clear()
    {
        while (_incoming.TryDequeue(out _))
        {
        }

        SelectedHistoryMessage = null;
        SelectedNode = null;
        Roots.Clear();
        _rootIndex.Clear();
        _topicCount = 0;
        _messageCount = 0;
        UpdateSummary();
    }

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync()
        => await SendAsync(PublishTopic, Encoding.UTF8.GetBytes(PublishBody), PublishQos, PublishRetain);

    /// <summary>Removes the selected topic's stored value by publishing an empty retained message.</summary>
    [RelayCommand(CanExecute = nameof(CanClearRetained))]
    private async Task ClearRetainedAsync()
    {
        if (SelectedNode is { } node)
            await SendAsync(node.FullTopic, [], 0, retain: true);
    }

    private async Task SendAsync(string topic, byte[] payload, int qos, bool retain)
    {
        var (ok, error) = await _connection.PublishAsync(topic, payload, qos, retain);
        if (ok)
            MqttMetrics.RecordSent(manual: true);
        else
            MqttMetrics.RecordFailed();

        PublishStatus = ok
            ? $"Sent to {topic} at {DateTime.Now:HH:mm:ss}"
            : $"Not sent: {error}";
    }

    partial void OnSelectedNodeChanged(TopicNodeViewModel? value)
    {
        SelectedHistoryMessage = null;
        if (value is not null)
            PublishTopic = value.FullTopic;
    }

    partial void OnSearchChanged(string value)
    {
        foreach (var root in Roots)
            ApplySearch(root, value.Trim());
    }

    // Arrives on the MQTT client's thread; the tree is only touched on the UI thread.
    private void OnMessageReceived(ReceivedMessage message)
    {
        if (_activated && !IsPaused)
            _incoming.Enqueue(message);
    }

    private void OnClientChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MqttClientViewModel.IsConnected))
        {
            PublishCommand.NotifyCanExecuteChanged();
            ClearRetainedCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnFlushTick(object? sender, EventArgs e)
    {
        var taken = 0;
        var selectedUpdated = false;
        while (taken < MaxMessagesPerFlush && _incoming.TryDequeue(out var message))
        {
            taken++;
            var node = NodeFor(message.Topic);
            if (!node.HasValue)
                _topicCount++;
            node.Receive(message);
            selectedUpdated |= node == SelectedNode;
        }

        if (taken == 0)
            return;

        _messageCount += taken;
        MqttMetrics.RecordReceived(taken);
        UpdateSummary();

        if (selectedUpdated)
        {
            OnPropertyChanged(nameof(ShownMessage));
            OnPropertyChanged(nameof(HasShownMessage));
            ClearRetainedCommand.NotifyCanExecuteChanged();
        }
    }

    private TopicNodeViewModel NodeFor(string topic)
    {
        var levels = topic.Split('/');
        var search = Search.Trim();

        if (!_rootIndex.TryGetValue(levels[0], out var node))
        {
            node = new TopicNodeViewModel(levels[0], levels[0], null);
            _rootIndex[levels[0]] = node;
            TopicNodeViewModel.Insert(Roots, node);
            SetVisibility(node, search);
        }

        var path = levels[0];
        for (var i = 1; i < levels.Length; i++)
        {
            path = $"{path}/{levels[i]}";
            var child = node.Find(levels[i]);
            if (child is null)
            {
                child = node.Add(levels[i], path);
                SetVisibility(child, search);
            }

            node = child;
        }

        return node;
    }

    // A new node is shown if it matches the search; its ancestors are then shown and opened too.
    private static void SetVisibility(TopicNodeViewModel node, string search)
    {
        if (search.Length == 0)
            return;

        node.IsVisible = Matches(node, search);
        if (!node.IsVisible)
            return;

        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.IsVisible = true;
            ancestor.IsExpanded = true;
        }
    }

    // Shows a node when it or anything below it matches. Returns whether it is shown.
    private static bool ApplySearch(TopicNodeViewModel node, string search)
    {
        var anyChild = false;
        foreach (var child in node.Children)
            anyChild |= ApplySearch(child, search);

        node.IsVisible = search.Length == 0 || anyChild || Matches(node, search);
        if (search.Length > 0 && anyChild)
            node.IsExpanded = true;
        return node.IsVisible;
    }

    private static bool Matches(TopicNodeViewModel node, string search)
        => node.FullTopic.Contains(search, StringComparison.OrdinalIgnoreCase);

    private void UpdateSummary()
    {
        var topics = _topicCount == 1 ? "1 topic" : $"{_topicCount:N0} topics";
        var messages = _messageCount == 1 ? "1 message" : $"{_messageCount:N0} messages";
        Summary = $"{topics}, {messages}";
    }

    /// <summary>Stops listening, when the client is removed.</summary>
    public void Dispose()
    {
        _flushTimer.Stop();
        _connection.MessageReceived -= OnMessageReceived;
        Client.PropertyChanged -= OnClientChanged;
    }
}
