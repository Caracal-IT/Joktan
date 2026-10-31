using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;

namespace Joktan.TopicExplorer.ViewModels;

/// <summary>
/// The recent values of one number on a topic: the whole payload, or a field inside it.
/// </summary>
public partial class FieldTrendViewModel : ViewModelBase
{
    public FieldTrendViewModel(string path, IReadOnlyList<double> values)
    {
        Path = path;
        Update(values);
    }

    /// <summary>Where the number is in the payload; empty when it is the whole payload.</summary>
    public string Path { get; }

    public bool HasPath => Path.Length > 0;

    [ObservableProperty]
    public partial IReadOnlyList<double> Values { get; set; } = [];

    [ObservableProperty]
    public partial double Minimum { get; set; }

    [ObservableProperty]
    public partial double Maximum { get; set; } = 1;

    public string MinimumText => Values.Count > 0 ? Values.Min().ToString("0.###") : "";

    public string MaximumText => Values.Count > 0 ? Values.Max().ToString("0.###") : "";

    public string LatestText => Values.Count > 0 ? Values[^1].ToString("0.###") : "";

    public void Update(IReadOnlyList<double> values)
    {
        var minimum = double.MaxValue;
        var maximum = double.MinValue;
        foreach (var value in values)
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

        Minimum = minimum;
        Maximum = maximum;
        Values = values;
        OnPropertyChanged(nameof(MinimumText));
        OnPropertyChanged(nameof(MaximumText));
        OnPropertyChanged(nameof(LatestText));
    }
}
