using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt;

namespace Joktan.TopicDashboards.ViewModels;

/// <summary>One graph on a dashboard, or the preview of one being set up.</summary>
public partial class GraphCardViewModel : ViewModelBase
{
    private HashSet<string> _wanted = [];

    public GraphCardViewModel(GraphProfile profile, GraphData data)
    {
        Profile = profile;
        Data = data;
        Apply(profile, data);
    }

    public GraphProfile Profile { get; private set; }

    public GraphData Data { get; private set; }

    /// <summary>The numbers being drawn.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GraphSeriesViewModel> Series { get; private set; } = [];

    /// <summary>Changes whenever there is something new to draw.</summary>
    [ObservableProperty]
    public partial int Revision { get; private set; }

    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    [ObservableProperty]
    public partial string Source { get; private set; } = "";

    /// <summary>The columns of the dashboard it spans.</summary>
    [ObservableProperty]
    public partial int WidthSpan { get; set; } = 1;

    /// <summary>The rows of the dashboard it spans.</summary>
    [ObservableProperty]
    public partial int HeightSpan { get; set; } = 1;

    /// <summary>Why nothing is drawn, or null when there is something to draw.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Status { get; set; }

    public bool HasStatus => Status is not null;

    /// <summary>Whether a line or bar graph has several numbers to tell apart; gauges and numbers name their own.</summary>
    public bool HasLegend => Series.Count > 1 && Profile.Kind is GraphKind.Line or GraphKind.Area;

    /// <summary>The latest value beside the name, for a graph that draws one number over time.</summary>
    public bool HasHeadlineValue => Series.Count == 1 && Profile.Kind is GraphKind.Line or GraphKind.Area or GraphKind.Bars;

    public string HeadlineValue => Series.Count > 0 ? Series[0].LatestText : "";

    /// <summary>This graph is being dragged to another place.</summary>
    [ObservableProperty]
    public partial bool IsDragging { get; set; }

    /// <summary>Another graph being dragged would go here.</summary>
    [ObservableProperty]
    public partial bool IsDropTarget { get; set; }

    /// <summary>Remove was pressed once, and is being asked about.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingRemove { get; set; }

    public Action<GraphCardViewModel>? EditRequested { get; set; }

    public Action<GraphCardViewModel>? RemoveRequested { get; set; }

    public Action<GraphCardViewModel, int>? MoveRequested { get; set; }

    public Action? SpanChanged { get; set; }

    /// <summary>Changes what is drawn and how, and from which readings.</summary>
    public void Apply(GraphProfile profile, GraphData data)
    {
        Profile = profile;
        Data = data;
        Name = profile.Name;
        Source = profile.Topic;
        WidthSpan = profile.Width;
        HeightSpan = profile.Height;
        _wanted = profile.Values.Select(v => v.Path).ToHashSet(StringComparer.Ordinal);
        Series = profile.Values.Select(v => new GraphSeriesViewModel(this, v, data.Series(v.Path))).ToList();
        OnPropertyChanged(nameof(HasLegend));
        OnPropertyChanged(nameof(HasHeadlineValue));
        OnPropertyChanged(nameof(HeadlineValue));
        Revision++;
    }

    /// <summary>Takes in a message on the graph's topic.</summary>
    public void Receive(ReceivedMessage message)
    {
        if (Data.Add(message, _wanted))
            Refresh();
    }

    /// <summary>Draws the readings again, after new ones have been added.</summary>
    public void Refresh()
    {
        foreach (var series in Series)
            series.Refresh();
        OnPropertyChanged(nameof(HeadlineValue));
        Revision++;
    }

    /// <summary>Moves the graph on as time passes, and forgets what has gone past its left edge.</summary>
    /// <param name="visible">Whether it is on screen, so needs drawing again.</param>
    public void Tick(DateTime now, bool visible)
    {
        Data.Prune(now - TimeSpan.FromMinutes(Profile.SpanMinutes));
        if (visible)
            Revision++;
    }

    public bool HasReadings => Series.Any(s => s.Samples.Samples.Count > 0);

    /// <summary>A value as the graph is set to show it, with its unit.</summary>
    public string Format(double value)
    {
        var text = Profile.Decimals >= 0 ? value.ToString($"F{Profile.Decimals}") : value.ToString("0.###");
        return Profile.Unit.Trim() switch
        {
            { Length: 0 } => text,
            "%" => $"{text}%",
            var unit => $"{text} {unit}",
        };
    }

    /// <summary>A value on the scale, shortened from a thousand up. The unit is left to the headline.</summary>
    public string FormatScale(double value)
    {
        var size = Math.Abs(value);
        return size >= 1e9 ? $"{value / 1e9:0.#}G"
            : size >= 1e6 ? $"{value / 1e6:0.#}M"
            : size >= 1e3 ? $"{value / 1e3:0.#}K"
            : value.ToString("0.###");
    }

    partial void OnWidthSpanChanged(int value)
    {
        Profile.Width = value;
        SpanChanged?.Invoke();
    }

    partial void OnHeightSpanChanged(int value)
    {
        Profile.Height = value;
        SpanChanged?.Invoke();
    }

    [RelayCommand]
    private void Edit() => EditRequested?.Invoke(this);

    [RelayCommand]
    private void AskRemove() => IsConfirmingRemove = true;

    [RelayCommand]
    private void KeepGraph() => IsConfirmingRemove = false;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    [RelayCommand]
    private void MoveEarlier() => MoveRequested?.Invoke(this, -1);

    [RelayCommand]
    private void MoveLater() => MoveRequested?.Invoke(this, 1);
}
