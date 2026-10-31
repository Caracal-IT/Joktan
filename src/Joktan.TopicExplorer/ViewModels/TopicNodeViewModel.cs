using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;
using Joktan.Mqtt;

namespace Joktan.TopicExplorer.ViewModels;

/// <summary>
/// One level of a topic in the explorer's tree. A node can hold messages of its own, child
/// levels, or both.
/// </summary>
public partial class TopicNodeViewModel : ViewModelBase
{
    public const int HistoryLength = 100;
    public const int TrendLength = 60;

    private readonly Dictionary<string, TopicNodeViewModel> _index = new(StringComparer.Ordinal);
    private readonly List<double> _numbers = [];

    public TopicNodeViewModel(string name, string fullTopic, TopicNodeViewModel? parent)
    {
        Name = name;
        FullTopic = fullTopic;
        Parent = parent;
    }

    public string Name { get; }

    public string FullTopic { get; }

    public TopicNodeViewModel? Parent { get; }

    public string DisplayName => Name.Length == 0 ? "(empty level)" : Name;

    public ObservableCollection<TopicNodeViewModel> Children { get; } = [];

    /// <summary>Messages received on exactly this topic, newest first.</summary>
    public ObservableCollection<ReceivedMessage> History { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValue), nameof(Info))]
    public partial int MessageCount { get; set; }

    /// <summary>Topics below this node that have received a message.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Info))]
    public partial int TopicCount { get; set; }

    /// <summary>Messages received on topics below this node.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Info))]
    public partial int SubtreeMessages { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview), nameof(IsRetained))]
    public partial ReceivedMessage? LastMessage { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>False when the search box hides this node.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrend))]
    public partial IReadOnlyList<double> Trend { get; set; } = [];

    [ObservableProperty]
    public partial double TrendMinimum { get; set; }

    [ObservableProperty]
    public partial double TrendMaximum { get; set; } = 1;

    public bool HasValue => MessageCount > 0;

    public bool HasTrend => Trend.Count >= 2;

    public string Preview => LastMessage is { } m ? $"= {m.Preview}" : "";

    public bool IsRetained => LastMessage?.Retain == true;

    /// <summary>What sits below this node, for example "3 topics, 120 messages".</summary>
    public string Info
    {
        get
        {
            if (TopicCount == 0)
                return MessageCount > 1 ? $"({MessageCount:N0} messages)" : "";

            var topics = TopicCount == 1 ? "1 topic" : $"{TopicCount:N0} topics";
            var messages = SubtreeMessages == 1 ? "1 message" : $"{SubtreeMessages:N0} messages";
            return $"({topics}, {messages})";
        }
    }

    public string TrendMinimumText => TrendMinimum.ToString("0.###");

    public string TrendMaximumText => TrendMaximum.ToString("0.###");

    public TopicNodeViewModel? Find(string name) => _index.GetValueOrDefault(name);

    /// <summary>Adds a child level, keeping the children in alphabetical order.</summary>
    public TopicNodeViewModel Add(string name, string fullTopic)
    {
        var child = new TopicNodeViewModel(name, fullTopic, this);
        _index[name] = child;
        Insert(Children, child);
        return child;
    }

    public static void Insert(ObservableCollection<TopicNodeViewModel> list, TopicNodeViewModel node)
    {
        var low = 0;
        var high = list.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (StringComparer.OrdinalIgnoreCase.Compare(list[middle].Name, node.Name) <= 0)
                low = middle + 1;
            else
                high = middle;
        }

        list.Insert(low, node);
    }

    /// <summary>Records a message received on exactly this topic.</summary>
    public void Receive(ReceivedMessage message)
    {
        var first = MessageCount == 0;
        MessageCount++;
        LastMessage = message;

        History.Insert(0, message);
        if (History.Count > HistoryLength)
            History.RemoveAt(History.Count - 1);

        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.SubtreeMessages++;
            if (first)
                ancestor.TopicCount++;
        }

        if (message.Number is { } number)
        {
            _numbers.Add(number);
            if (_numbers.Count > TrendLength)
                _numbers.RemoveAt(0);
            UpdateTrend();
        }
    }

    private void UpdateTrend()
    {
        var minimum = double.MaxValue;
        var maximum = double.MinValue;
        foreach (var value in _numbers)
        {
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        // A flat line still needs a range to be drawn in.
        if (maximum - minimum < 1e-9)
        {
            minimum -= 1;
            maximum += 1;
        }

        TrendMinimum = minimum;
        TrendMaximum = maximum;
        OnPropertyChanged(nameof(TrendMinimumText));
        OnPropertyChanged(nameof(TrendMaximumText));
        Trend = _numbers.ToArray();
    }
}
