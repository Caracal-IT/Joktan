using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Joktan.ViewModels;

/// <summary>
/// A headline number on the dashboard, optionally with a usage meter and a recent trend.
/// </summary>
public partial class MetricTileViewModel : ViewModelBase
{
    private const int HistoryLength = 30;
    private const double WarningPercent = 75;
    private const double DangerPercent = 90;

    private readonly List<double> _history = [];

    public MetricTileViewModel(string label, Geometry icon, bool hasMeter = false, bool hasTrend = false)
    {
        Label = label;
        Icon = icon;
        HasMeter = hasMeter;
        HasTrend = hasTrend;
    }

    public string Label { get; }

    public Geometry Icon { get; }

    public bool HasMeter { get; }

    public bool HasTrend { get; }

    [ObservableProperty]
    public partial string Value { get; set; } = "–";

    [ObservableProperty]
    public partial string Detail { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWarning), nameof(IsDanger))]
    public partial double Percent { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<double> History { get; set; } = [];

    public bool IsWarning => Percent is >= WarningPercent and < DangerPercent;

    public bool IsDanger => Percent >= DangerPercent;

    public void Update(string value, string detail, double? ratio = null)
    {
        Value = value;
        Detail = detail;
        Percent = (ratio ?? 0) * 100;

        if (!HasTrend || ratio is null)
            return;

        _history.Add(Percent);
        if (_history.Count > HistoryLength)
            _history.RemoveAt(0);
        History = _history.ToArray();
    }
}
