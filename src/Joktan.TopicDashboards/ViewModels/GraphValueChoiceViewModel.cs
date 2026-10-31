using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;

namespace Joktan.TopicDashboards.ViewModels;

/// <summary>One number offered in the graph editor, with its name and colour if it is drawn.</summary>
public partial class GraphValueChoiceViewModel : ViewModelBase
{
    private readonly Action<GraphValueChoiceViewModel> _toggled;
    private readonly Action _changed;
    private bool _settingFromEditor;

    public GraphValueChoiceViewModel(string topic, string path, int color, Action<GraphValueChoiceViewModel> toggled, Action changed)
    {
        Topic = topic;
        Path = path;
        _toggled = toggled;
        _changed = changed;
        Color = color;
    }

    public string Topic { get; }

    /// <summary>Where the number is in the payload; empty when it is the whole payload.</summary>
    public string Path { get; }

    public string DisplayPath => Path.Length == 0 ? "The whole payload" : Path;

    public string Kind => Path.Contains("[*]", StringComparison.Ordinal) ? "array"
        : Path.Contains('[') ? "item"
        : "value";

    [ObservableProperty]
    public partial bool IsChosen { get; set; }

    [ObservableProperty]
    public partial string Label { get; set; } = "";

    /// <summary>Which of the graph colours it is drawn in.</summary>
    [ObservableProperty]
    public partial int Color { get; set; }

    [ObservableProperty]
    public partial string LatestText { get; set; } = "";

    /// <summary>Ticks or clears it, without counting as the user's doing.</summary>
    public void Show(bool chosen)
    {
        _settingFromEditor = true;
        IsChosen = chosen;
        _settingFromEditor = false;
    }

    partial void OnIsChosenChanged(bool value)
    {
        if (!_settingFromEditor)
            _toggled(this);
    }

    partial void OnLabelChanged(string value) => _changed();

    partial void OnColorChanged(int value) => _changed();
}
