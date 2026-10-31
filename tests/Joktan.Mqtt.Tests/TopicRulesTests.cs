using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System;
using Joktan.Mqtt;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class TopicRulesTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("a/b/c")]
    [InlineData("/a/")]
    [InlineData("$SYS/x")]
    public void A_plain_topic_can_be_published_to(string topic) => Assert.Null(TopicRules.Validate(topic));

    [Theory]
    [InlineData(null, "The topic is empty")]
    [InlineData("", "The topic is empty")]
    [InlineData("a/+/b", "A topic may not contain + or #")]
    [InlineData("a/#", "A topic may not contain + or #")]
    public void A_topic_with_a_problem_says_why(string? topic, string expected)
        => Assert.Equal(expected, TopicRules.Validate(topic));

    [Fact]
    public void A_topic_may_be_as_long_as_65535_bytes_counted_in_UTF8()
    {
        Assert.Null(TopicRules.Validate(new string('a', 65535)));
        Assert.Equal("The topic is longer than 65,535 bytes", TopicRules.Validate(new string('a', 65536)));
        Assert.Equal("The topic is longer than 65,535 bytes", TopicRules.Validate(new string('é', 32768)));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a/b")]
    [InlineData("#")]
    [InlineData("+")]
    [InlineData("a/#")]
    [InlineData("a/+/b")]
    [InlineData("+/+/#")]
    public void A_good_filter_can_be_subscribed_to(string filter) => Assert.Null(TopicRules.ValidateFilter(filter));

    [Theory]
    [InlineData(null, "The filter is empty")]
    [InlineData("", "The filter is empty")]
    [InlineData("a/#/b", "In a/#/b, # must be the whole last level")]
    [InlineData("a#", "In a#, # must be the whole last level")]
    [InlineData("a+/b", "In a+/b, + must be a whole level")]
    [InlineData("a/+b", "In a/+b, + must be a whole level")]
    public void A_bad_filter_says_why(string? filter, string expected)
        => Assert.Equal(expected, TopicRules.ValidateFilter(filter));

    [Fact]
    public void A_filter_may_be_as_long_as_65535_bytes()
    {
        Assert.Null(TopicRules.ValidateFilter(new string('a', 65535)));
        Assert.Equal("The filter is longer than 65,535 bytes", TopicRules.ValidateFilter(new string('a', 65536)));
    }

    [Theory]
    [InlineData("a/b", "a/b", true)]
    [InlineData("a/b", "a/c", false)]
    [InlineData("a", "a/b", false)]
    [InlineData("a/b", "a", false)]
    [InlineData("#", "a", true)]
    [InlineData("#", "a/b/c", true)]
    [InlineData("a/#", "a", true)]
    [InlineData("a/#", "a/b/c", true)]
    [InlineData("a/#", "b/c", false)]
    [InlineData("+", "a", true)]
    [InlineData("+", "a/b", false)]
    [InlineData("a/+", "a/b", true)]
    [InlineData("a/+", "a/b/c", false)]
    [InlineData("a/+/c", "a/b/c", true)]
    [InlineData("+/+", "a", false)]
    [InlineData("", "a", false)]
    public void Matches_a_topic(string filter, string topic, bool expected)
        => Assert.Equal(expected, TopicRules.Matches(filter, topic));

    [Theory]
    [InlineData("#", "$SYS/x", false)]
    [InlineData("+/x", "$SYS/x", false)]
    [InlineData("$SYS/#", "$SYS/x", true)]
    [InlineData("$SYS/+", "$SYS/x", true)]
    public void A_wildcard_at_the_start_does_not_match_a_dollar_topic(string filter, string topic, bool expected)
        => Assert.Equal(expected, TopicRules.Matches(filter, topic));

    [Theory]
    [InlineData("a/#", "a/+", true)]
    [InlineData("a/#", "a/#", true)]
    [InlineData("#", "a/#", true)]
    [InlineData("a/+", "a/#", false)]
    [InlineData("a/+", "a/b", true)]
    public void Matches_every_topic_of_another_filter(string filter, string other, bool expected)
        => Assert.Equal(expected, TopicRules.Matches(filter, other));

    [Fact]
    public void Leaves_out_filters_another_one_covers()
    {
        var kept = TopicRules.WithoutCovered(["a/#", "a/b", "c", "c", "a/+"]);

        Assert.Equal(["a/#", "c"], kept);
    }

    [Fact]
    public void Everything_does_not_cover_dollar_topics()
    {
        Assert.Equal(["#", "$SYS/#"], TopicRules.WithoutCovered(["#", "$SYS/#"]));
    }
}
