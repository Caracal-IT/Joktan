using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class TopicTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("device", true)]
    [InlineData("a.b-c_1", true)]
    [InlineData("folder", false)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    [InlineData("{a}", false)]
    public void Names_a_topic_field_may_have(string name, bool expected)
        => Assert.Equal(expected, TopicTemplate.IsValidName(name));

    [Fact]
    public void Lists_the_names_in_order_of_first_use()
    {
        Assert.Equal(["x", "y"], TopicTemplate.Names("a/{x}/{y}/{x}"));
        Assert.Empty(TopicTemplate.Names("a/b"));
    }

    [Theory]
    [InlineData("plant", "", "plant")]
    [InlineData("plant", "other", "other")]
    [InlineData("plant", "{folder}/x", "plant/x")]
    [InlineData("plant", "{folder}/{folder}", "plant/plant")]
    [InlineData("", "{folder}/x", "/x")]
    public void The_effective_topic_uses_the_folder(string folder, string topic, string expected)
        => Assert.Equal(expected, TopicTemplate.Effective(folder, topic));

    [Fact]
    public void Checks_a_topic_against_its_fields()
    {
        FieldRule[] device = [new("device", FieldType.Guid)];

        Assert.Null(TopicTemplate.Check("a/{device}", device));
        Assert.Null(TopicTemplate.Check("a/b", []));
        Assert.Equal("The topic is empty", TopicTemplate.Check("", []));
        Assert.Contains("{folder} can only be used", TopicTemplate.Check("{folder}/a", []));
        Assert.Equal("Topic part {device} has no topic field", TopicTemplate.Check("a/{device}", []));
        Assert.Equal("Topic field device is not used in the topic; write it as {device}", TopicTemplate.Check("a/b", device));
        Assert.Equal("A topic may not contain + or #", TopicTemplate.Check("a/+/{device}", device));
    }

    [Fact]
    public void A_topic_without_fields_is_left_as_it_is()
    {
        Assert.Equal("a/{x}", TopicTemplate.Resolve("a/{x}", [], new Dictionary<string, long>(), Now));
    }

    [Fact]
    public void Fills_in_fields_and_advances_counters()
    {
        var counters = new Dictionary<string, long>();
        FieldRule[] rules = [new("n", FieldType.Counter) { Start = 5, Step = 2 }];

        Assert.Equal("a/5", TopicTemplate.Resolve("a/{n}", rules, counters, Now));
        Assert.Equal("a/7", TopicTemplate.Resolve("a/{n}", rules, counters, Now));
    }

    [Fact]
    public void Topic_positions_do_not_mix_with_the_counters_of_a_body_field_of_that_name()
    {
        var counters = new Dictionary<string, long> { ["n"] = 100 };
        FieldRule[] rules = [new("n", FieldType.Counter)];

        Assert.Equal("1", TopicTemplate.Resolve("{n}", rules, counters, Now));
        Assert.Equal(100, counters["n"]);
    }

    [Fact]
    public void A_string_goes_in_without_quotes_and_other_values_as_JSON()
    {
        FieldRule[] rules =
        [
            new("text", FieldType.Fixed) { Value = "abc" },
            new("number", FieldType.Fixed) { Value = 42 },
            new("flag", FieldType.Fixed) { Value = true },
            new("nothing", FieldType.Fixed) { Value = null },
            new("object", FieldType.Fixed) { Value = new JsonObject { ["a"] = 1 } },
        ];

        var topic = TopicTemplate.Resolve("{text}/{number}/{flag}/{nothing}/{object}", rules, new Dictionary<string, long>(), Now);

        Assert.Equal("abc/42/true/null/{\"a\":1}", topic);
    }

    [Fact]
    public void A_part_with_no_field_is_left_in_place()
    {
        FieldRule[] rules = [new("a", FieldType.Fixed) { Value = "x" }];

        Assert.Equal("x/{b}", TopicTemplate.Resolve("{a}/{b}", rules, new Dictionary<string, long>(), Now));
    }
}
