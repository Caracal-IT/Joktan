using System;
using System.Collections.Generic;
using System.Linq;
using Joktan.Mqtt;

namespace Joktan.TopicDashboards;

public readonly record struct Sample(DateTime Time, double Value);

/// <summary>The readings of one number on a topic, oldest first.</summary>
public sealed class SampleSeries
{
    // Beyond this every other reading goes, so a fast topic costs detail rather than memory.
    public const int MaxSamples = 4000;

    private static readonly TimeSpan MaxBatchSpread = TimeSpan.FromSeconds(10);

    private readonly List<Sample> _samples = [];

    public IReadOnlyList<Sample> Samples => _samples;

    /// <summary>
    /// Adds what one message held. The items of an array all arrive at once, so they are spread over
    /// the time since the last reading to be drawn one after another.
    /// </summary>
    public void Add(DateTime time, IReadOnlyList<double> values)
    {
        if (values.Count == 1)
        {
            Append(new Sample(time, values[0]));
            return;
        }

        var previous = _samples.Count > 0 ? _samples[^1].Time : time - MaxBatchSpread;
        var spread = time - previous;
        if (spread > MaxBatchSpread)
            spread = MaxBatchSpread;
        if (spread < TimeSpan.Zero)
            spread = TimeSpan.Zero;

        for (var i = 0; i < values.Count; i++)
            Append(new Sample(time - spread + spread * ((i + 1.0) / values.Count), values[i]));
    }

    /// <summary>Forgets readings before a time, except the last of them, so a line still enters from the edge.</summary>
    public void Prune(DateTime before)
    {
        var old = 0;
        while (old < _samples.Count && _samples[old].Time < before)
            old++;
        if (old > 1)
            _samples.RemoveRange(0, old - 1);
    }

    private void Append(Sample sample)
    {
        _samples.Add(sample);
        if (_samples.Count <= MaxSamples)
            return;

        var kept = 0;
        for (var i = 0; i < _samples.Count; i += 2)
            _samples[kept++] = _samples[i];
        _samples[kept - 1] = sample;
        _samples.RemoveRange(kept, _samples.Count - kept);
    }
}

/// <summary>The readings a graph has, by where they are in the payload.</summary>
public sealed class GraphData
{
    private readonly Dictionary<string, SampleSeries> _series = new(StringComparer.Ordinal);

    public SampleSeries Series(string path)
    {
        if (!_series.TryGetValue(path, out var series))
            _series[path] = series = new SampleSeries();
        return series;
    }

    /// <summary>A separate copy of these readings.</summary>
    public GraphData Copy()
    {
        var copy = new GraphData();
        foreach (var (path, series) in _series)
        {
            var target = copy.Series(path);
            foreach (var sample in series.Samples)
                target.Add(sample.Time, [sample.Value]);
        }

        return copy;
    }

    /// <summary>
    /// Adds the numbers in a message and returns whether any were new. A retained message is the
    /// broker's stored value, not a new reading, so it only counts when there is nothing yet.
    /// </summary>
    /// <param name="wanted">The paths to keep; null keeps all.</param>
    public bool Add(ReceivedMessage message, IReadOnlySet<string>? wanted = null)
    {
        var added = false;
        foreach (var field in message.NumericFields)
        {
            if (wanted?.Contains(field.Path) == false)
                continue;

            var series = Series(field.Path);
            if (message.Retain && series.Samples.Count > 0)
                continue;

            series.Add(message.Time, field.Values);
            added = true;
        }

        return added;
    }

    public void Prune(DateTime before)
    {
        foreach (var series in _series.Values)
            series.Prune(before);
    }

    public void Clear() => _series.Clear();

    /// <summary>Forgets every number but these.</summary>
    public void Keep(IReadOnlySet<string> paths)
    {
        foreach (var path in _series.Keys.Where(p => !paths.Contains(p)).ToList())
            _series.Remove(path);
    }

    /// <summary>The paths in the order they are offered: values and arrays before single items.</summary>
    public IEnumerable<string> OrderedPaths() => _series.Keys.OrderBy(JsonNumbers.IsOneItem);
}
