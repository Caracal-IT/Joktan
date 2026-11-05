using System.Text.Json.Nodes;
using Joktan.Mqtt.Proxy;
using Xunit;

namespace Joktan.Mqtt.Tests.Proxy;

public class TopicRewriteTests
{
    [Theory]
    [InlineData("plant/{line}/status", "plant/line1/status", true)]
    [InlineData("plant/{line}/status", "plant/line1/other", false)]
    [InlineData("plant/{line}/status", "plant/status", false)]
    [InlineData("plant/status", "plant/status", true)]
    [InlineData("{a}", "x", true)]
    [InlineData("{}", "x", false)]
    public void A_template_matches_its_topics(string template, string topic, bool expected)
        => Assert.Equal(expected, TopicRewrite.MatchesTemplate(template, topic));

    [Fact]
    public void Fields_come_from_the_topic_and_then_the_payload()
    {
        var payload = JsonNode.Parse("""{"id":"cell-7","n":3,"sensor":{"serial":"S1"},"line":"ignored"}""");

        Assert.Equal("lab/line1/cell-7/3/S1", TopicRewrite.Rewrite("plant/{line}/status", "lab/{line}/{id}/{n}/{sensor.serial}", "plant/line1/status", payload));
        Assert.Equal("lab/x", TopicRewrite.Rewrite("a", "lab/x", "a"));
        Assert.Null(TopicRewrite.Rewrite("a", "lab/{gone}", "a", payload));
        Assert.Null(TopicRewrite.Rewrite("a", "lab/{id}", "a"));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("lab/{line}/{id}", null)]
    [InlineData("lab/#", "wildcards")]
    [InlineData("lab/+/x", "wildcards")]
    [InlineData("/lab", null)]
    public void A_topic_at_the_broker_has_no_wildcards(string topic, string? problem)
    {
        var result = TopicRewrite.Check(topic);
        if (problem is null)
            Assert.Null(result);
        else
            Assert.Contains("+ or #", result);
    }

    [Fact]
    public void Only_a_topic_with_every_field_of_the_messages_own_goes_back()
    {
        Assert.True(TopicRewrite.CanReverse("plant/{line}", "lab/{line}/{id}"));
        Assert.False(TopicRewrite.CanReverse("plant/{line}", "lab/{id}"));
        Assert.True(TopicRewrite.CanReverse("plant", "lab/{id}"));
        Assert.Equal(["line", "id"], TopicRewrite.Fields("lab/{line}/{id}"));
        Assert.Equal("lab/+/+", TopicRewrite.AsFilter("lab/{line}/{id}"));
    }

    [Theory]
    [InlineData("#", "plant/{line}/status", true)]
    [InlineData("plant/#", "plant/{line}/status", true)]
    [InlineData("plant/+/status", "plant/{line}/status", true)]
    [InlineData("plant/line1/status", "plant/{line}/status", true)]
    [InlineData("plant/line1", "plant/{line}/status", false)]
    [InlineData("plant/line1/status/more", "plant/{line}/status", false)]
    [InlineData("other/#", "plant/{line}/status", false)]
    public void A_filter_overlaps_a_template_when_it_can_take_in_one_of_its_topics(string filter, string template, bool expected)
        => Assert.Equal(expected, TopicRewrite.Overlaps(filter, template));
}
