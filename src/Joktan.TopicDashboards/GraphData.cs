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

/// <summary>The readings a graph has, by topic and where they are in the payload.</summary>
public sealed class GraphData
{
    private readonly Dictionary<(string Topic, string Path), SampleSeries> _series = [];

    public SampleSeries Series(string topic, string path)
    {
        if (!_series.TryGetValue((topic, path), out var series))
            _series[(topic, path)] = series = new SampleSeries();
        return series;
    }

    /// <summary>A separate copy of these readings.</summary>
    public GraphData Copy()
    {
        var copy = new GraphData();
        foreach (var ((topic, path), series) in _series)
        {
            var target = copy.Series(topic, path);
            foreach (var sample in series.Samples)
                target.Add(sample.Time, [sample.Value]);
        }

        return copy;
    }

    /// <summary>
    /// Adds the numbers in a message and returns whether any were new. A retained message is the
    /// broker's stored value, not a new reading, so it only counts when there is nothing yet.
    /// </summary>
    /// <param name="wanted">The numbers to keep; null keeps all.</param>
    public bool Add(ReceivedMessage message, IReadOnlySet<(string Topic, string Path)>? wanted = null)
    {
        var added = false;
        foreach (var field in message.NumericFields)
        {
            if (wanted?.Contains((message.Topic, field.Path)) == false)
                continue;

            var series = Series(message.Topic, field.Path);
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

    /// <summary>Forgets everything received on a topic.</summary>
    public void Remove(string topic)
    {
        foreach (var key in _series.Keys.Where(k => k.Topic == topic).ToList())
            _series.Remove(key);
    }

    /// <summary>Forgets every number but these.</summary>
    public void Keep(IReadOnlySet<(string Topic, string Path)> numbers)
    {
        foreach (var key in _series.Keys.Where(k => !numbers.Contains(k)).ToList())
            _series.Remove(key);
    }

    /// <summary>The numbers on a topic in the order they are offered: values and arrays before single items.</summary>
    public IEnumerable<string> OrderedPaths(string topic)
        => _series.Keys.Where(k => k.Topic == topic).Select(k => k.Path).OrderBy(JsonNumbers.IsOneItem);
}

/// <summary>Short names for topics, for telling the numbers on a graph apart.</summary>
public static class TopicNames
{
    /// <summary>For each topic, as few of its last levels as make it different from the others.</summary>
    public static Dictionary<string, string> Shorten(IReadOnlyCollection<string> topics)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var topic in topics)
        {
            var levels = topic.Split('/');
            var name = topic;
            for (var count = 1; count <= levels.Length; count++)
            {
                name = string.Join('/', levels[^count..]);
                var suffix = "/" + name;
                if (!topics.Any(t => t != topic && (t == name || t.EndsWith(suffix, StringComparison.Ordinal))))
                    break;
            }

            names[topic] = name;
        }

        return names;
    }
}
