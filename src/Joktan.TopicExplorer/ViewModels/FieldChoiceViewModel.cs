using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;

namespace Joktan.TopicExplorer.ViewModels;

/// <summary>
/// One number offered in the list of what to draw for a topic: a value, a whole array, or one
/// item of an array.
/// </summary>
public partial class FieldChoiceViewModel : ViewModelBase
{
    private readonly Action<FieldChoiceViewModel> _toggled;
    private bool _settingFromNode;

    public FieldChoiceViewModel(string path, Action<FieldChoiceViewModel> toggled)
    {
        Path = path;
        _toggled = toggled;
    }

    /// <summary>Where the number is in the payload; empty when it is the whole payload.</summary>
    public string Path { get; }

    public string DisplayPath => Path.Length == 0 ? "The whole payload" : Path;

    public string Kind => Path.Contains("[*]", StringComparison.Ordinal) ? "array"
        : Path.Contains('[') ? "item"
        : "value";

    [ObservableProperty]
    public partial bool IsChosen { get; set; }

    [ObservableProperty]
    public partial string LatestText { get; set; } = "";

    /// <summary>Shows what the node works out, without counting as the user's choice.</summary>
    public void Show(bool chosen)
    {
        _settingFromNode = true;
        IsChosen = chosen;
        _settingFromNode = false;
    }

    partial void OnIsChosenChanged(bool value)
    {
        if (!_settingFromNode)
            _toggled(this);
    }
}
