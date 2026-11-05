using System.Text.Json.Nodes;
using Joktan.Mqtt.Proxy;
using Xunit;

namespace Joktan.Mqtt.Tests.Proxy;

public class JsonMergeTests
{
    [Theory]
    [InlineData("""{"a":1,"o":{"x":1,"y":1}}""", """{"b":2,"o":{"y":2}}""", """{"a":1,"o":{"x":1,"y":2},"b":2}""")]
    [InlineData("""{"list":[1,2,3]}""", """{"list":[9]}""", """{"list":[9]}""")]
    [InlineData("""{"o":{"x":1}}""", """{"o":5}""", """{"o":5}""")]
    [InlineData("[1,2]", """{"a":1}""", """{"a":1}""")]
    [InlineData("""{"a":1}""", "[1]", "[1]")]
    public void Objects_merge_at_every_depth_and_anything_else_is_replaced(string target, string overlay, string expected)
        => Assert.Equal(expected, JsonMerge.Merge(JsonNode.Parse(target), JsonNode.Parse(overlay))!.ToJsonString());

    [Fact]
    public void Neither_side_is_changed()
    {
        var target = JsonNode.Parse("""{"o":{"x":1}}""");
        var overlay = JsonNode.Parse("""{"o":{"y":2}}""");

        JsonMerge.Merge(target, overlay);

        Assert.Equal("""{"o":{"x":1}}""", target!.ToJsonString());
        Assert.Equal("""{"o":{"y":2}}""", overlay!.ToJsonString());
        Assert.Null(JsonMerge.Merge(target, null));
    }
}
