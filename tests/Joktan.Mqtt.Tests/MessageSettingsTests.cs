using System;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class MessageSettingsTests
{
    [Fact]
    public void An_empty_file_gives_the_defaults()
    {
        var settings = MessageSettings.Parse("{}");

        Assert.False(settings.Enabled);
        Assert.Equal("", settings.Group);
        Assert.Equal("", settings.Topic);
        Assert.Equal(0, settings.Qos);
        Assert.False(settings.Retain);
        Assert.False(settings.Gzip);
        Assert.Equal(ScheduleType.Manual, settings.Schedule.Type);
        Assert.Empty(settings.Fields);
        Assert.Empty(settings.TopicFields);
        Assert.Empty(settings.Arrays);
        Assert.False(settings.ChangesBody);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        var settings = MessageSettings.Parse("""
            {
              // Sent with care.
              "qos": 1,
            }
            """);

        Assert.Equal(1, settings.Qos);
    }

    [Fact]
    public void Reads_the_simple_settings()
    {
        var settings = MessageSettings.Parse("""
            { "enabled": true, "group": " Plant / Line 1/ ", "topic": " a/b ", "qos": 2, "retain": true, "gzip": true }
            """);

        Assert.True(settings.Enabled);
        Assert.Equal("Plant/Line 1", settings.Group);
        Assert.Equal("a/b", settings.Topic);
        Assert.Equal(2, settings.Qos);
        Assert.True(settings.Retain);
        Assert.True(settings.Gzip);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("""{"qos":3}""")]
    [InlineData("""{"qos":-1}""")]
    [InlineData("""{"qos":"high"}""")]
    [InlineData("""{"qos":1.5}""")]
    [InlineData("""{"enabled":1}""")]
    [InlineData("""{"group":1}""")]
    [InlineData("""{"topic":true}""")]
    [InlineData("""{"schedule":1}""")]
    [InlineData("""{"schedule":{"type":"sometimes"}}""")]
    [InlineData("""{"schedule":{"type":1}}""")]
    [InlineData("""{"schedule":{"type":"once"}}""")]
    [InlineData("""{"schedule":{"type":"once","at":"whenever"}}""")]
    [InlineData("""{"schedule":{"type":"interval"}}""")]
    [InlineData("""{"schedule":{"type":"interval","every":0}}""")]
    [InlineData("""{"schedule":{"type":"interval","every":1,"maxSends":0}}""")]
    [InlineData("""{"schedule":{"type":"interval","every":1,"unit":"weeks"}}""")]
    [InlineData("""{"schedule":{"type":"interval","every":1,"start":"soon"}}""")]
    [InlineData("""{"schedule":{"type":"daily","days":["mon"]}}""")]
    [InlineData("""{"schedule":{"type":"daily","times":["08:00"]}}""")]
    [InlineData("""{"schedule":{"type":"daily","times":["25:00"],"days":["mon"]}}""")]
    [InlineData("""{"schedule":{"type":"daily","times":["08:00"],"days":["someday"]}}""")]
    [InlineData("""{"fields":[]}""")]
    [InlineData("""{"fields":{"a":1}}""")]
    [InlineData("""{"fields":{"a":{}}}""")]
    [InlineData("""{"fields":{"a":{"type":"nonsense"}}}""")]
    [InlineData("""{"fields":{"a":{"type":"fixed"}}}""")]
    [InlineData("""{"fields":{"a":{"type":"fixed","value":1,"values":[2,3]}}}""")]
    [InlineData("""{"fields":{"a":{"type":"oneOf"}}}""")]
    [InlineData("""{"fields":{"a":{"type":"cycle","values":[]}}}""")]
    [InlineData("""{"fields":{"a":{"type":"randomInteger","min":2,"max":1}}}""")]
    [InlineData("""{"fields":{"a":{"type":"randomNumber","min":2,"max":1}}}""")]
    [InlineData("""{"fields":{"a":{"type":"randomNumber","decimals":16}}}""")]
    [InlineData("""{"fields":{"a":{"type":"randomNumber","decimals":-1}}}""")]
    [InlineData("""{"topicFields":[]}""")]
    [InlineData("""{"topicFields":{"a":{}}}""")]
    [InlineData("""{"arrays":[]}""")]
    [InlineData("""{"arrays":{"a":1}}""")]
    [InlineData("""{"arrays":{"a":{}}}""")]
    [InlineData("""{"arrays":{"a":{"min":1}}}""")]
    [InlineData("""{"arrays":{"a":{"count":-1}}}""")]
    [InlineData("""{"arrays":{"a":{"count":10001}}}""")]
    [InlineData("""{"arrays":{"a":{"min":3,"max":2}}}""")]
    public void A_bad_file_is_reported_as_a_format_problem(string json)
    {
        Assert.Throws<FormatException>(() => MessageSettings.Parse(json));
    }

    [Fact]
    public void The_problem_names_the_field_and_where_it_is()
    {
        var body = Assert.Throws<FormatException>(() => MessageSettings.Parse("""{"fields":{"a":{}}}"""));
        var topic = Assert.Throws<FormatException>(() => MessageSettings.Parse("""{"topicFields":{"a":{}}}"""));

        Assert.Equal("field \"a\" needs a \"type\"", body.Message);
        Assert.Equal("topic field \"a\" needs a \"type\"", topic.Message);
    }

    [Fact]
    public void Reads_a_single_send()
    {
        var schedule = MessageSettings.Parse("""{"schedule":{"type":"once","at":"2026-11-02T08:30:00"}}""").Schedule;

        Assert.Equal(ScheduleType.Once, schedule.Type);
        Assert.Equal(new DateTime(2026, 11, 2, 8, 30, 0), schedule.At);
    }

    [Fact]
    public void Reads_an_interval()
    {
        var schedule = MessageSettings.Parse("""
            {"schedule":{"type":"Interval","every":5,"unit":"MINUTES","start":"2026-11-02T08:00:00","end":"2026-11-03T08:00:00","maxSends":10}}
            """).Schedule;

        Assert.Equal(ScheduleType.Interval, schedule.Type);
        Assert.Equal(5, schedule.Every);
        Assert.Equal(IntervalUnit.Minutes, schedule.Unit);
        Assert.Equal(new DateTime(2026, 11, 2, 8, 0, 0), schedule.Start);
        Assert.Equal(new DateTime(2026, 11, 3, 8, 0, 0), schedule.End);
        Assert.Equal(10, schedule.MaxSends);
    }

    [Fact]
    public void Reads_a_daily_schedule_with_days_in_any_case_and_once_each()
    {
        var schedule = MessageSettings.Parse("""
            {"schedule":{"type":"daily","times":["08:30","9:15","10:00:30"],"days":["MON","mon","Sun"]}}
            """).Schedule;

        Assert.Equal(ScheduleType.Daily, schedule.Type);
        Assert.Equal([new TimeOnly(8, 30), new TimeOnly(9, 15), new TimeOnly(10, 0, 30)], schedule.Times);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Sunday], schedule.Days);
    }

    [Fact]
    public void Reads_a_field_of_each_type()
    {
        var fields = MessageSettings.Parse("""
            {"fields":{
              "a":{"type":"unixSeconds"},
              "b":{"type":"counter","start":10,"step":5},
              "c":{"type":"randomNumber","min":1.5,"max":2.5,"decimals":3},
              "d":{"type":"oneOf","values":["x",1,null]},
              "e":{"type":"fixed","value":null},
              "f":{"type":"fixed","value":2,"values":[1,2]}
            }}
            """).Fields;

        Assert.Equal(["a", "b", "c", "d", "e", "f"], fields.Select(f => f.Path));
        Assert.Equal(FieldType.UnixSeconds, fields[0].Type);
        Assert.Equal((10, 5), (fields[1].Start, fields[1].Step));
        Assert.Equal((1.5, 2.5, 3), (fields[2].Min, fields[2].Max, fields[2].Decimals));
        Assert.Equal(3, fields[3].Values.Count);
        Assert.Null(fields[3].Values[2]);
        Assert.Null(fields[4].Value);
        Assert.Equal(2, fields[5].Value!.GetValue<int>());
    }

    [Fact]
    public void Reads_array_sizes()
    {
        var arrays = MessageSettings.Parse("""
            {"arrays":{"a":{"count":3},"b":{"min":1,"max":5},"c":{"count":2,"max":4}}}
            """).Arrays;

        Assert.True(arrays[0].IsFixed);
        Assert.Equal((3, 3), (arrays[0].Min, arrays[0].Max));
        Assert.False(arrays[1].IsFixed);
        Assert.Equal((1, 5), (arrays[1].Min, arrays[1].Max));
        Assert.Equal((2, 4), (arrays[2].Min, arrays[2].Max));
    }

    [Fact]
    public void The_body_changes_with_fields_or_arrays()
    {
        Assert.False(new MessageSettings { TopicFields = [new FieldRule("t", FieldType.Guid)] }.ChangesBody);
        Assert.True(new MessageSettings { Fields = [new FieldRule("a", FieldType.Guid)] }.ChangesBody);
        Assert.True(new MessageSettings { Arrays = [new ArrayRule("a", 1, 1)] }.ChangesBody);
    }

    [Fact]
    public void Writes_only_what_differs_from_the_defaults_where_it_can()
    {
        var root = JsonNode.Parse(MessageSettings.Default.ToJson())!.AsObject();

        Assert.False(root.ContainsKey("topic"));
        Assert.False(root.ContainsKey("topicFields"));
        Assert.False(root.ContainsKey("gzip"));
        Assert.False(root.ContainsKey("arrays"));
        Assert.Equal("manual", root["schedule"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Everything_written_is_read_back()
    {
        var original = new MessageSettings
        {
            Enabled = true,
            Group = "Plant/Line 1",
            Topic = "{folder}/{device}",
            TopicFields = [new FieldRule("device", FieldType.Cycle) { Values = ["a", "b"] }],
            Qos = 1,
            Retain = true,
            Gzip = true,
            Schedule = new MessageSchedule
            {
                Type = ScheduleType.Interval,
                Every = 5,
                Unit = IntervalUnit.Minutes,
                Start = new DateTime(2026, 11, 2, 8, 0, 0),
                End = new DateTime(2026, 11, 3, 8, 0, 0),
                MaxSends = 3,
            },
            Fields =
            [
                new FieldRule("ts", FieldType.UnixMilliseconds),
                new FieldRule("n", FieldType.Counter) { Start = 7, Step = 2 },
                new FieldRule("i", FieldType.RandomInteger) { Min = 1, Max = 9 },
                new FieldRule("x", FieldType.RandomNumber) { Min = 0.5, Max = 1.5, Decimals = 4 },
                new FieldRule("o", FieldType.OneOf) { Values = [1, "a", null] },
                new FieldRule("f", FieldType.Fixed) { Value = new JsonObject { ["a"] = 1 } },
                new FieldRule("l", FieldType.Fixed) { Value = 2, Values = [1, 2] },
                new FieldRule("z", FieldType.Fixed) { Value = null },
            ],
            Arrays = [new ArrayRule("r", 2, 2), new ArrayRule("s", 1, 4)],
        };

        var read = MessageSettings.Parse(original.ToJson());

        Assert.Equal(original.ToJson(), read.ToJson());
        Assert.True(read.Enabled);
        Assert.Equal("Plant/Line 1", read.Group);
        Assert.Equal(FieldType.Cycle, read.TopicFields[0].Type);
        Assert.Equal(8, read.Fields.Count);
        Assert.Equal(original.Schedule.End, read.Schedule.End);
        Assert.True(read.Arrays[0].IsFixed);
        Assert.False(read.Arrays[1].IsFixed);
    }

    [Theory]
    [InlineData("once")]
    [InlineData("daily")]
    public void Schedules_are_read_back(string type)
    {
        var schedule = type == "once"
            ? new MessageSchedule { Type = ScheduleType.Once, At = new DateTime(2026, 11, 2, 8, 30, 15) }
            : new MessageSchedule { Type = ScheduleType.Daily, Times = [new TimeOnly(12, 0), new TimeOnly(8, 0)], Days = [DayOfWeek.Sunday, DayOfWeek.Friday] };

        var read = MessageSettings.Parse(new MessageSettings { Schedule = schedule }.ToJson()).Schedule;

        Assert.Equal(schedule.Type, read.Type);
        Assert.Equal(schedule.At, read.At);
        Assert.Equal(schedule.Describe(), read.Describe());
    }

    [Fact]
    public void The_settings_file_sits_beside_the_message()
    {
        Assert.Equal("/a/b.send.json", MessageSettings.PathFor("/a/b.json"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("Plant", "Plant")]
    [InlineData(" Plant / Line 1/", "Plant/Line 1")]
    [InlineData("//a///b//", "a/b")]
    public void Group_paths_are_tidied(string? group, string expected)
        => Assert.Equal(expected, MessageSettings.NormalizeGroup(group));

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(1, 10000, null)]
    [InlineData(-1, 1, "the number of items must not be negative")]
    [InlineData(1, 10001, "the number of items must not be more than 10,000")]
    [InlineData(3, 2, "\"min\" must not be greater than \"max\"")]
    public void Checks_an_array_rule(int min, int max, string? expected)
        => Assert.Equal(expected, MessageSettings.CheckArray(new ArrayRule("a", min, max)));

    [Fact]
    public void Rules_of_types_with_no_settings_are_always_fine()
    {
        foreach (var type in new[] { FieldType.UnixSeconds, FieldType.UnixMilliseconds, FieldType.IsoTimestamp, FieldType.Guid, FieldType.Counter })
            Assert.Null(MessageSettings.CheckRule(new FieldRule("a", type) { Min = 5, Max = 1, Decimals = 99 }));
    }

    [Fact]
    public void Fixed_values_compare_as_JSON()
    {
        var listed = new FieldRule("a", FieldType.Fixed) { Value = new JsonObject { ["x"] = 1 }, Values = [new JsonObject { ["x"] = 1 }] };
        var unlisted = new FieldRule("a", FieldType.Fixed) { Value = null, Values = [1] };
        var nullListed = new FieldRule("a", FieldType.Fixed) { Value = null, Values = [null] };

        Assert.Null(MessageSettings.CheckRule(listed));
        Assert.NotNull(MessageSettings.CheckRule(unlisted));
        Assert.Null(MessageSettings.CheckRule(nullListed));
    }
}
