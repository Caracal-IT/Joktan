using Xunit;

namespace Joktan.Mqtt.Tests;

public class TopicPatternTests
{
    [Fact]
    public void An_exact_topic_has_no_keys()
    {
        var pattern = TopicPattern.Parse("plant/line1/temperature")!;

        Assert.True(pattern.IsExact);
        Assert.Equal("plant/line1/temperature", pattern.Filter);
        Assert.True(pattern.Matches("plant/line1/temperature"));
        Assert.False(pattern.Matches("plant/line2/temperature"));
    }

    [Fact]
    public void A_named_level_is_a_plus_with_a_name()
    {
        var pattern = TopicPattern.Parse("plant/{line}/telemetry/vibration")!;

        Assert.Equal("plant/+/telemetry/vibration", pattern.Filter);
        var key = Assert.Single(pattern.Keys);
        Assert.Equal(new TopicKey("line", 1, false), key);
        Assert.Equal("line2", pattern.KeyValues("plant/line2/telemetry/vibration")["line"]);
    }

    [Fact]
    public void A_plain_wildcard_is_a_key_named_after_its_level()
    {
        var pattern = TopicPattern.Parse("devices/+/heartbeat")!;

        Assert.Equal("devices/+/heartbeat", pattern.Filter);
        Assert.Equal("a81f", pattern.KeyValues("devices/a81f/heartbeat")["level1"]);
    }

    [Fact]
    public void A_named_hash_holds_the_rest_of_the_topic()
    {
        var pattern = TopicPattern.Parse("plant/{area#}")!;

        Assert.Equal("plant/#", pattern.Filter);
        Assert.Equal("line1/telemetry/rms", pattern.KeyValues("plant/line1/telemetry/rms")["area"]);
        Assert.Equal("", pattern.KeyValues("plant")["area"]);
    }

    [Fact]
    public void A_plain_hash_is_a_key_too()
    {
        var pattern = TopicPattern.Parse("plant/#")!;

        Assert.Equal("a/b", pattern.KeyValues("plant/a/b")["level1"]);
    }

    [Fact]
    public void A_real_brace_is_written_with_a_backslash()
    {
        var pattern = TopicPattern.Parse(@"plant/\{odd}/value")!;

        Assert.True(pattern.IsExact);
        Assert.True(pattern.Matches("plant/{odd}/value"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/{2}/b")]
    [InlineData("a/{}/b")]
    [InlineData("a/{x}/{x}")]
    [InlineData("a/b{x}/c")]
    [InlineData("a/{x#}/c")]
    [InlineData("a/#/c")]
    [InlineData("a/b+/c")]
    public void Refuses_what_cannot_be_subscribed_to(string text)
    {
        Assert.NotNull(TopicPattern.Validate(text));
        Assert.Null(TopicPattern.Parse(text));
    }

    [Theory]
    [InlineData("line1", 1, "line")]
    [InlineData("sensor-02", 1, "sensor")]
    [InlineData("press_7", 1, "press")]
    [InlineData("42", 3, "level3")]
    public void Suggests_a_name_from_a_value(string value, int level, string expected)
        => Assert.Equal(expected, TopicPattern.SuggestName(value, level, []));

    [Fact]
    public void A_suggested_name_is_not_one_already_used()
        => Assert.Equal("line2", TopicPattern.SuggestName("line1", 1, ["line"]));

    [Fact]
    public void A_set_of_filters_checks_exact_topics_and_wildcards()
    {
        var set = new TopicFilterSet(["a/b", "c/+", "c/+", "d/#"]);

        Assert.Equal(3, set.Count);
        Assert.True(set.Contains("a/b"));
        Assert.True(set.Contains("c/x"));
        Assert.True(set.Contains("d/x/y"));
        Assert.False(set.Contains("a/c"));
        Assert.False(set.Contains("c/x/y"));
    }
}
