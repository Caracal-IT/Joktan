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

/// <summary>Key values that tell series, graphs or tabs apart. The order they were found in makes no difference.</summary>
public sealed class SeriesKeys : IEquatable<SeriesKeys>
{
    public static readonly SeriesKeys None = new([]);

    private readonly SortedDictionary<string, string> _values;

    public SeriesKeys(IEnumerable<KeyValuePair<string, string>> values)
    {
        _values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
            _values[key] = value;
        Id = string.Join('\u001f', _values.Select(v => $"{v.Key}={v.Value}"));
    }

    /// <summary>The same text for the same values.</summary>
    public string Id { get; }

    public IReadOnlyDictionary<string, string> Values => _values;

    public bool IsEmpty => _values.Count == 0;

    public string? this[string key] => _values.GetValueOrDefault(key);

    /// <summary>Whether all of another's values are among these.</summary>
    public bool Includes(SeriesKeys other) => other._values.All(v => _values.TryGetValue(v.Key, out var mine) && mine == v.Value);

    /// <summary>Only the values of these keys.</summary>
    public SeriesKeys Only(IEnumerable<string> keys)
    {
        var wanted = keys.ToHashSet(StringComparer.Ordinal);
        return new SeriesKeys(_values.Where(v => wanted.Contains(v.Key)));
    }

    /// <summary>Puts in the values for <c>{key}</c> in a name, and says if there were any to put in.</summary>
    public string Fill(string pattern, out bool filled)
    {
        filled = false;
        foreach (var (key, value) in _values)
        {
            var slot = $"{{{key}}}";
            if (pattern.Contains(slot, StringComparison.Ordinal))
            {
                pattern = pattern.Replace(slot, value, StringComparison.Ordinal);
                filled = true;
            }
        }

        return pattern;
    }

    public bool Equals(SeriesKeys? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as SeriesKeys);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
}

/// <summary>One series a graph has: a number, the key values that tell it from others, and its readings.</summary>
public sealed record KeyedSeries(string Topic, string Path, SeriesKeys Keys, SampleSeries Samples);

/// <summary>The readings a graph has, by topic, where they are in the payload, and their key values.</summary>
public sealed class GraphData
{
    private readonly Dictionary<(string Topic, string Path, string Keys), KeyedSeries> _series = [];

    /// <summary>Changes whenever a series starts or goes.</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<KeyedSeries> All => _series.Values;

    public int Count => _series.Count;

    public SampleSeries Series(string topic, string path, SeriesKeys? keys = null)
    {
        keys ??= SeriesKeys.None;
        if (!_series.TryGetValue((topic, path, keys.Id), out var series))
        {
            _series[(topic, path, keys.Id)] = series = new KeyedSeries(topic, path, keys, new SampleSeries());
            Version++;
        }

        return series.Samples;
    }

    public bool Has(string topic, string path, SeriesKeys keys) => _series.ContainsKey((topic, path, keys.Id));

    /// <summary>A separate copy of these readings.</summary>
    public GraphData Copy()
    {
        var copy = new GraphData();
        foreach (var series in _series.Values)
        {
            var target = copy.Series(series.Topic, series.Path, series.Keys);
            foreach (var sample in series.Samples.Samples)
                target.Add(sample.Time, [sample.Value]);
        }

        return copy;
    }

    /// <summary>
    /// Adds what was read from a message and returns whether any of it was new. A retained message is
    /// the broker's stored value, not a new reading, so it only counts when there is nothing yet.
    /// </summary>
    public bool Add(ReceivedMessage message, IEnumerable<GraphReading> readings)
    {
        var added = false;
        foreach (var reading in readings)
        {
            var series = Series(reading.Topic, reading.Path, reading.Keys);
            if (message.Retain && series.Samples.Count > 0)
                continue;

            series.Add(message.Time, reading.Values);
            added = true;
        }

        return added;
    }

    public void Prune(DateTime before)
    {
        foreach (var series in _series.Values)
            series.Samples.Prune(before);
    }

    /// <summary>Forgets the series with keys that have sent nothing since a time, as these come and go.</summary>
    public void ForgetIdle(DateTime before)
    {
        foreach (var key in _series.Where(s => !s.Value.Keys.IsEmpty && IsIdle(s.Value.Samples, before)).Select(s => s.Key).ToList())
        {
            _series.Remove(key);
            Version++;
        }
    }

    public void Forget(KeyedSeries series)
    {
        _series.Remove((series.Topic, series.Path, series.Keys.Id));
        Version++;
    }

    public void Clear()
    {
        _series.Clear();
        Version++;
    }

    /// <summary>Forgets everything received on a topic.</summary>
    public void Remove(string topic)
    {
        foreach (var key in _series.Keys.Where(k => k.Topic == topic).ToList())
            _series.Remove(key);
        Version++;
    }

    /// <summary>Forgets every number but these.</summary>
    public void Keep(IReadOnlySet<(string Topic, string Path)> numbers)
    {
        foreach (var key in _series.Keys.Where(k => !numbers.Contains((k.Topic, k.Path))).ToList())
            _series.Remove(key);
        Version++;
    }

    private static bool IsIdle(SampleSeries series, DateTime before)
        => series.Samples.Count == 0 || series.Samples[^1].Time < before;
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
