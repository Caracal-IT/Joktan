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

    [Fact]
    public void Asking_for_a_series_again_gives_the_same_one()
    {
        Assert.Same(_data.Series("a", "x"), _data.Series("a", "x"));
        Assert.NotSame(_data.Series("a", "x"), _data.Series("a", "y"));
        Assert.NotSame(_data.Series("a", "x"), _data.Series("b", "x"));
    }

    [Fact]
    public void Adds_the_numbers_in_a_message()
    {
        Assert.True(_data.Add(Message("a", """{"x":1,"y":2,"s":"t"}""")));

        Assert.Equal(1, _data.Series("a", "x").Samples.Single().Value);
        Assert.Equal(2, _data.Series("a", "y").Samples.Single().Value);
    }

    [Fact]
    public void Adds_a_bare_number_under_no_path()
    {
        _data.Add(Message("a", "42"));

        Assert.Equal(42, _data.Series("a", "").Samples.Single().Value);
    }

    [Fact]
    public void A_message_without_numbers_adds_nothing()
    {
        Assert.False(_data.Add(Message("a", "text")));
    }

    [Fact]
    public void Keeps_only_the_numbers_wanted()
    {
        var wanted = new HashSet<(string, string)> { ("a", "x") };

        Assert.True(_data.Add(Message("a", """{"x":1,"y":2}"""), wanted));
        Assert.False(_data.Add(Message("b", """{"x":1}"""), wanted));

        Assert.Single(_data.Series("a", "x").Samples);
        Assert.Empty(_data.Series("a", "y").Samples);
    }

    [Fact]
    public void A_retained_message_only_counts_when_there_is_nothing_yet()
    {
        Assert.True(_data.Add(Message("a", "1", retain: true)));
        Assert.False(_data.Add(Message("a", "2", retain: true)));
        Assert.True(_data.Add(Message("a", "3")));

        Assert.Equal([1.0, 3.0], _data.Series("a", "").Samples.Select(s => s.Value));
    }

    [Fact]
    public void A_copy_changes_separately()
    {
        _data.Add(Message("a", "1"));

        var copy = _data.Copy();
        _data.Add(Message("a", "2"));
        copy.Series("b", "").Add(Time, [9]);

        Assert.Single(copy.Series("a", "").Samples);
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
    public void Forgets_everything_a_topic_or_all_but_some()
    {
        _data.Add(Message("a", """{"x":1,"y":2}"""));
        _data.Add(Message("b", "3"));

        _data.Remove("a");
        Assert.Empty(_data.OrderedPaths("a"));
        Assert.Single(_data.OrderedPaths("b"));

        _data.Add(Message("a", """{"x":1,"y":2}"""));
        _data.Keep(new HashSet<(string, string)> { ("a", "x") });
        Assert.Equal(["x"], _data.OrderedPaths("a"));
        Assert.Empty(_data.OrderedPaths("b"));

        _data.Clear();
        Assert.Empty(_data.OrderedPaths("a"));
    }

    [Fact]
    public void Offers_values_and_arrays_before_single_items()
    {
        _data.Add(Message("a", """{"r":[1,2],"x":3}"""));

        Assert.Equal(["r[*]", "x", "r[0]", "r[1]"], _data.OrderedPaths("a"));
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
