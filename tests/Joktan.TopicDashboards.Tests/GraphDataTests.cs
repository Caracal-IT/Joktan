using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Joktan.Mqtt;
using Xunit;

namespace Joktan.TopicDashboards.Tests;

public class SampleSeriesTests
{
    private static readonly DateTime Time = new(2026, 11, 2, 8, 0, 0);

    private readonly SampleSeries _series = new();

    [Fact]
    public void One_value_is_a_reading_at_that_time()
    {
        _series.Add(Time, [5]);

        Assert.Equal(new Sample(Time, 5), Assert.Single(_series.Samples));
    }

    [Fact]
    public void No_values_add_nothing()
    {
        _series.Add(Time, []);

        Assert.Empty(_series.Samples);
    }

    [Fact]
    public void Items_of_an_array_are_spread_over_the_time_since_the_last_reading()
    {
        _series.Add(Time, [1]);

        _series.Add(Time.AddSeconds(4), [2, 3]);

        Assert.Equal(
            [new Sample(Time, 1), new Sample(Time.AddSeconds(2), 2), new Sample(Time.AddSeconds(4), 3)],
            _series.Samples);
    }

    [Fact]
    public void The_first_items_are_spread_over_ten_seconds()
    {
        _series.Add(Time, [1, 2]);

        Assert.Equal(Time.AddSeconds(-5), _series.Samples[0].Time);
        Assert.Equal(Time, _series.Samples[1].Time);
    }

    [Fact]
    public void A_long_gap_spreads_items_over_ten_seconds_only()
    {
        _series.Add(Time, [1]);

        _series.Add(Time.AddHours(1), [2, 3]);

        Assert.Equal(Time.AddHours(1).AddSeconds(-5), _series.Samples[1].Time);
    }

    [Fact]
    public void Items_arriving_with_an_earlier_time_are_not_spread()
    {
        _series.Add(Time, [1]);

        _series.Add(Time.AddSeconds(-1), [2, 3]);

        Assert.Equal(Time.AddSeconds(-1), _series.Samples[1].Time);
        Assert.Equal(Time.AddSeconds(-1), _series.Samples[2].Time);
    }

    [Fact]
    public void Past_the_limit_every_other_reading_goes_but_never_the_newest()
    {
        for (var i = 0; i <= SampleSeries.MaxSamples; i++)
            _series.Add(Time.AddSeconds(i), [i]);

        Assert.InRange(_series.Samples.Count, 1, SampleSeries.MaxSamples);
        Assert.Equal(2001, _series.Samples.Count);
        Assert.Equal(0, _series.Samples[0].Value);
        Assert.Equal(2, _series.Samples[1].Value);
        Assert.Equal(SampleSeries.MaxSamples, _series.Samples[^1].Value);
    }

    [Fact]
    public void Pruning_keeps_the_last_old_reading_so_a_line_enters_from_the_edge()
    {
        for (var i = 0; i < 5; i++)
            _series.Add(Time.AddSeconds(i), [i]);

        _series.Prune(Time.AddSeconds(3));

        Assert.Equal([2.0, 3.0, 4.0], _series.Samples.Select(s => s.Value));
    }

    [Fact]
    public void Pruning_with_little_old_keeps_everything()
    {
        _series.Add(Time, [1]);
        _series.Add(Time.AddSeconds(1), [2]);

        _series.Prune(Time);
        Assert.Equal(2, _series.Samples.Count);

        _series.Prune(Time.AddMilliseconds(1));
        Assert.Equal(2, _series.Samples.Count);
    }
}

public class GraphDataTests
{
    private static readonly DateTime Time = new(2026, 11, 2, 8, 0, 0);

    private readonly GraphData _data = new();

    private static ReceivedMessage Message(string topic, string payload, bool retain = false, DateTime? time = null)
        => new(topic, Encoding.UTF8.GetBytes(payload), 0, retain, time ?? Time);

    private bool Add(ReceivedMessage message, params (string Topic, string Path)[] numbers)
    {
        var profile = new GraphProfile { Values = numbers.Select(n => new GraphValue { Topic = n.Topic, Path = n.Path }).ToList() };
        return _data.Add(message, new GraphReader(profile).Read(message).Readings);
    }

    [Fact]
    public void Asking_for_a_series_again_gives_the_same_one()
    {
        var keys = new SeriesKeys([new("axis", "x")]);

        Assert.Same(_data.Series("a", "x"), _data.Series("a", "x"));
        Assert.NotSame(_data.Series("a", "x"), _data.Series("a", "y"));
        Assert.NotSame(_data.Series("a", "x"), _data.Series("b", "x"));
        Assert.Same(_data.Series("a", "x", keys), _data.Series("a", "x", new SeriesKeys([new("axis", "x")])));
        Assert.NotSame(_data.Series("a", "x"), _data.Series("a", "x", keys));
    }

    [Fact]
    public void Adds_the_numbers_in_a_message()
    {
        Assert.True(Add(Message("a", """{"x":1,"y":2,"s":"t"}"""), ("a", "x"), ("a", "y")));

        Assert.Equal(1, _data.Series("a", "x").Samples.Single().Value);
        Assert.Equal(2, _data.Series("a", "y").Samples.Single().Value);
    }

    [Fact]
    public void Adds_a_bare_number_under_no_path()
    {
        Add(Message("a", "42"), ("a", ""));

        Assert.Equal(42, _data.Series("a", "").Samples.Single().Value);
    }

    [Fact]
    public void A_message_without_numbers_adds_nothing()
    {
        Assert.False(Add(Message("a", "text"), ("a", "")));
    }

    [Fact]
    public void Keeps_only_the_numbers_wanted()
    {
        Assert.True(Add(Message("a", """{"x":1,"y":2}"""), ("a", "x")));
        Assert.False(Add(Message("b", """{"x":1}"""), ("a", "x")));

        Assert.Single(_data.Series("a", "x").Samples);
        Assert.Empty(_data.Series("a", "y").Samples);
    }

    [Fact]
    public void A_retained_message_only_counts_when_there_is_nothing_yet()
    {
        Assert.True(Add(Message("a", "1", retain: true), ("a", "")));
        Assert.False(Add(Message("a", "2", retain: true), ("a", "")));
        Assert.True(Add(Message("a", "3"), ("a", "")));

        Assert.Equal([1.0, 3.0], _data.Series("a", "").Samples.Select(s => s.Value));
    }

    [Fact]
    public void A_copy_changes_separately_and_keeps_its_keys()
    {
        var keys = new SeriesKeys([new("axis", "x")]);
        _data.Series("a", "", keys).Add(Time, [1]);

        var copy = _data.Copy();
        _data.Series("a", "", keys).Add(Time, [2]);
        copy.Series("b", "").Add(Time, [9]);

        Assert.Single(copy.Series("a", "", keys).Samples);
        Assert.Empty(_data.Series("b", "").Samples);
    }

    [Fact]
    public void Prunes_every_series()
    {
        _data.Series("a", "").Add(Time, [1]);
        _data.Series("a", "").Add(Time.AddSeconds(1), [2]);
        _data.Series("a", "").Add(Time.AddSeconds(2), [3]);
        _data.Series("b", "").Add(Time, [1]);
        _data.Series("b", "").Add(Time.AddSeconds(1), [2]);
        _data.Series("b", "").Add(Time.AddSeconds(2), [3]);

        _data.Prune(Time.AddSeconds(2));

        Assert.Equal(2, _data.Series("a", "").Samples.Count);
        Assert.Equal(2, _data.Series("b", "").Samples.Count);
    }

    [Fact]
    public void Only_series_with_keys_are_forgotten_when_idle()
    {
        var keys = new SeriesKeys([new("device", "d1")]);
        _data.Series("a", "").Add(Time, [1]);
        _data.Series("a", "", keys).Add(Time, [1]);
        _data.Series("a", "", new SeriesKeys([new("device", "d2")])).Add(Time.AddMinutes(5), [1]);
        var version = _data.Version;

        _data.ForgetIdle(Time.AddMinutes(1));

        Assert.Equal(2, _data.Count);
        Assert.False(_data.Has("a", "", keys));
        Assert.NotEqual(version, _data.Version);
    }

    [Fact]
    public void Forgets_everything_a_topic_or_all_but_some()
    {
        _data.Series("a", "x").Add(Time, [1]);
        _data.Series("a", "y").Add(Time, [1]);
        _data.Series("b", "").Add(Time, [1]);

        _data.Remove("a");
        Assert.Equal("b", Assert.Single(_data.All).Topic);

        _data.Series("a", "x").Add(Time, [1]);
        _data.Series("a", "y").Add(Time, [1]);
        _data.Keep(new HashSet<(string, string)> { ("a", "x") });
        Assert.Equal("x", Assert.Single(_data.All).Path);

        _data.Clear();
        Assert.Empty(_data.All);
    }
}

public class SeriesKeysTests
{
    [Fact]
    public void The_order_found_in_makes_no_difference()
    {
        var one = new SeriesKeys([new("a", "1"), new("b", "2")]);
        var other = new SeriesKeys([new("b", "2"), new("a", "1")]);

        Assert.Equal(one, other);
        Assert.Equal(one.Id, other.Id);
        Assert.Equal(one.GetHashCode(), other.GetHashCode());
    }

    [Fact]
    public void Includes_what_it_has_at_least()
    {
        var keys = new SeriesKeys([new("a", "1"), new("b", "2")]);

        Assert.True(keys.Includes(SeriesKeys.None));
        Assert.True(keys.Includes(new SeriesKeys([new("a", "1")])));
        Assert.False(keys.Includes(new SeriesKeys([new("a", "2")])));
        Assert.False(keys.Includes(new SeriesKeys([new("c", "1")])));
    }

    [Fact]
    public void Fills_in_a_name()
    {
        var keys = new SeriesKeys([new("machine", "press-07"), new("axis", "x")]);

        Assert.Equal("press-07 / x", keys.Fill("{machine} / {axis}", out var filled));
        Assert.True(filled);
        Assert.Equal("Plain", keys.Fill("Plain", out filled));
        Assert.False(filled);
    }

    [Fact]
    public void Keeps_only_some()
    {
        var keys = new SeriesKeys([new("a", "1"), new("b", "2")]);

        Assert.Equal("a=1", keys.Only(["a", "c"]).Id);
    }
}

public class TopicNamesTests
{
    [Fact]
    public void Uses_as_few_of_the_last_levels_as_tell_topics_apart()
    {
        var names = TopicNames.Shorten(["a/x/temp", "b/y/temp", "c/humidity"]);

        Assert.Equal("x/temp", names["a/x/temp"]);
        Assert.Equal("y/temp", names["b/y/temp"]);
        Assert.Equal("humidity", names["c/humidity"]);
    }

    [Fact]
    public void A_topic_that_ends_another_keeps_its_whole_name()
    {
        var names = TopicNames.Shorten(["temp", "a/temp"]);

        Assert.Equal("temp", names["temp"]);
        Assert.Equal("a/temp", names["a/temp"]);
    }

    [Fact]
    public void Only_whole_levels_count_as_the_same()
    {
        var names = TopicNames.Shorten(["a/temp", "b/attemp"]);

        Assert.Equal("temp", names["a/temp"]);
        Assert.Equal("attemp", names["b/attemp"]);
    }

    [Fact]
    public void A_single_topic_gets_its_last_level()
    {
        Assert.Equal("c", TopicNames.Shorten(["a/b/c"])["a/b/c"]);
        Assert.Empty(TopicNames.Shorten([]));
    }
}
