using Joktan.Core.ViewModels;

namespace Joktan.TopicDashboards.ViewModels;

/// <summary>One number a graph draws, with its readings.</summary>
public partial class GraphSeriesViewModel : ViewModelBase
{
    private readonly GraphCardViewModel _card;

    /// <param name="topicName">A short name for the topic when the graph has several, otherwise null.</param>
    public GraphSeriesViewModel(GraphCardViewModel card, GraphValue value, SampleSeries samples, string? topicName)
    {
        _card = card;
        Path = value.Path;
        Source = Path.Length > 0 ? $"{value.Topic} {Path}" : value.Topic;
        Slot = value.Color;
        Samples = samples;
        DisplayLabel = value.Label.Trim() is { Length: > 0 } label ? label
            : topicName is not null ? (Path.Length > 0 ? $"{topicName} {Path}" : topicName)
            : Path.Length > 0 ? Path
            : "Value";
    }

    public string Path { get; }

    /// <summary>The topic and path in full.</summary>
    public string Source { get; }

    /// <summary>Which of the graph colours it is drawn in. It stays with the number, whatever else is added or removed.</summary>
    public int Slot { get; }

    public SampleSeries Samples { get; }

    public string DisplayLabel { get; }

    public double? Latest => Samples.Samples.Count > 0 ? Samples.Samples[^1].Value : null;

    public string LatestText => Latest is { } latest ? _card.Format(latest) : "–";

    /// <summary>Says that new readings have arrived.</summary>
    public void Refresh() => OnPropertyChanged(nameof(LatestText));
}
