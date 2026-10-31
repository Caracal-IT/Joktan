using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.ViewModels;

namespace Joktan.TopicDashboards.ViewModels;

/// <summary>One dashboard, on its own tab: its format and its graphs.</summary>
public partial class DashboardTabViewModel : ViewModelBase
{
    private readonly TopicDashboardsViewModel _page;

    public DashboardTabViewModel(DashboardProfile profile, TopicDashboardsViewModel page)
    {
        Profile = profile;
        Name = profile.Name;
        Columns = profile.Columns;
        RowHeight = profile.RowHeight;
        Gap = profile.Gap;
        Background = profile.Background;

        // Set last, so that loading these does not count as changing them.
        _page = page;
        foreach (var graph in profile.Graphs)
            Graphs.Add(NewCard(graph, new GraphData()));
    }

    public DashboardProfile Profile { get; }

    public ObservableCollection<GraphCardViewModel> Graphs { get; } = [];

    /// <summary>The clients this dashboard's graphs subscribe through, that exist.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MqttClientViewModel> Clients { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Name { get; set; }

    public string DisplayName => Name.Trim() is { Length: > 0 } name ? name : "Untitled";

    [ObservableProperty]
    public partial int Columns { get; set; }

    [ObservableProperty]
    public partial double RowHeight { get; set; }

    [ObservableProperty]
    public partial double Gap { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    public partial string Background { get; set; }

    public object BackgroundBrush => TabColors.BrushOrUnset(Background);

    /// <summary>Whether the format settings are open.</summary>
    [ObservableProperty]
    public partial bool IsFormatting { get; set; }

    /// <summary>Whether graphs can be moved, resized, edited and removed.</summary>
    [ObservableProperty]
    public partial bool IsArranging { get; set; }

    [ObservableProperty]
    public partial bool IsConfirmingRemove { get; set; }

    /// <summary>Whether this dashboard can be removed; the last one cannot.</summary>
    public bool CanRemove => _page.CanRemoveDashboards;

    /// <summary>Says that there is now one dashboard more or fewer.</summary>
    public void RefreshCanRemove() => OnPropertyChanged(nameof(CanRemove));

    public bool HasNoGraphs => Graphs.Count == 0;

    public string RemoveQuestion => Graphs.Count switch
    {
        0 => "Remove this dashboard?",
        1 => "Remove this dashboard and its graph?",
        _ => $"Remove this dashboard and its {Graphs.Count} graphs?",
    };

    public static IReadOnlyList<int> ColumnChoices { get; } = Enumerable.Range(1, DashboardProfile.MaxColumns).ToList();

    public static IReadOnlyList<Swatch> Palette => TabColors.Palette;

    public double MinRowHeight => DashboardProfile.MinRowHeight;

    public double MaxRowHeight => DashboardProfile.MaxRowHeight;

    public double MaxGap => DashboardProfile.MaxGap;

    public void Add(GraphProfile profile, GraphData data)
    {
        Profile.Graphs.Add(profile);
        Graphs.Add(NewCard(profile, data));
        OnGraphsChanged();
    }

    /// <summary>Moves a graph to a position among the others.</summary>
    public void MoveTo(GraphCardViewModel card, int index)
    {
        var from = Graphs.IndexOf(card);
        index = Math.Clamp(index, 0, Graphs.Count - 1);
        if (from < 0 || from == index)
            return;

        Graphs.Move(from, index);
        Profile.Graphs.RemoveAt(from);
        Profile.Graphs.Insert(index, card.Profile);
        _page.Save();
    }

    /// <summary>Says which clients exist now, and how each graph stands with its own.</summary>
    public void UpdateClients()
    {
        Clients = Graphs
            .Select(g => _page.FindClient(g.Profile.Client))
            .OfType<MqttClientViewModel>()
            .Distinct()
            .ToList();

        foreach (var card in Graphs)
            card.Status = _page.StatusOf(card);
    }

    partial void OnNameChanged(string value)
    {
        Profile.Name = value;
        _page?.Save();
    }

    partial void OnColumnsChanged(int value)
    {
        Profile.Columns = value;
        _page?.Save();
    }

    partial void OnRowHeightChanged(double value)
    {
        Profile.RowHeight = (int)value;
        _page?.Save();
    }

    partial void OnGapChanged(double value)
    {
        Profile.Gap = (int)value;
        _page?.Save();
    }

    partial void OnBackgroundChanged(string value)
    {
        Profile.Background = value;
        _page?.Save();
    }

    [RelayCommand]
    private void PickBackground(string hex) => Background = hex;

    [RelayCommand]
    private void AskRemove()
    {
        OnPropertyChanged(nameof(RemoveQuestion));
        IsConfirmingRemove = true;
    }

    [RelayCommand]
    private void KeepDashboard() => IsConfirmingRemove = false;

    [RelayCommand]
    private void Remove()
    {
        IsConfirmingRemove = false;
        _page.Remove(this);
    }

    [RelayCommand]
    private void AddGraph() => _page.AddGraph(this);

    private GraphCardViewModel NewCard(GraphProfile profile, GraphData data)
        => new(profile, data)
        {
            EditRequested = c => _page.EditGraph(this, c),
            RemoveRequested = RemoveGraph,
            MoveRequested = (c, by) => MoveTo(c, Graphs.IndexOf(c) + by),
            SpanChanged = () => _page.Save(),
        };

    private void RemoveGraph(GraphCardViewModel card)
    {
        Graphs.Remove(card);
        Profile.Graphs.Remove(card.Profile);
        OnGraphsChanged();
    }

    private void OnGraphsChanged()
    {
        OnPropertyChanged(nameof(HasNoGraphs));
        _page.GraphsChanged();
    }
}
