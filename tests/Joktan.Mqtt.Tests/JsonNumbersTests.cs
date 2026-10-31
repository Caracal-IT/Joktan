using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class JsonNumbersTests
{
    private static IReadOnlyList<NumericField> Find(string json) => JsonNumbers.Find(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Finds_numbers_at_their_paths_and_ignores_the_rest()
    {
        var fields = Find("""{"a":1,"b":{"c":2.5},"s":"x","t":true,"n":null}""");

        Assert.Equal(["a", "b.c"], fields.Select(f => f.Path));
        Assert.Equal([1.0], fields[0].Values);
        Assert.Equal([2.5], fields[1].Values);
    }

    [Fact]
    public void Finds_an_arrays_numbers_together_and_one_by_one_with_the_together_ones_first()
    {
        var fields = Find("""{"r":[{"v":1},{"v":2}],"x":3}""");

        Assert.Equal(["r[*].v", "x", "r[0].v", "r[1].v"], fields.Select(f => f.Path));
        Assert.Equal([1.0, 2.0], fields[0].Values);
        Assert.Equal([1.0], fields[2].Values);
        Assert.Equal([2.0], fields[3].Values);
    }

    [Fact]
    public void A_body_that_is_an_array_starts_its_paths_with_the_index()
    {
        var fields = Find("[1,2]");

        Assert.Equal(["[*]", "[0]", "[1]"], fields.Select(f => f.Path));
    }

    [Fact]
    public void Arrays_inside_arrays_have_a_star_for_each()
    {
        var fields = Find("""{"m":[[1,2],[3]]}""");

        Assert.Equal([1.0, 2.0, 3.0], fields.Single(f => f.Path == "m[*][*]").Values);
        Assert.Equal([2.0], fields.Single(f => f.Path == "m[0][1]").Values);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"a\":")]
    [InlineData("1")]
    [InlineData("\"text\"")]
    [InlineData("{}")]
    [InlineData("{\"a\":1e400}")]
    public void Finds_nothing_in_a_payload_without_usable_numbers(string payload) => Assert.Empty(Find(payload));

    [Fact]
    public void Skips_a_payload_over_a_megabyte()
    {
        var padding = new string(' ', 1024 * 1024);

        Assert.Empty(Find("{\"a\":1}" + padding));
    }

    [Fact]
    public void Names_only_the_first_twenty_items_of_an_array()
    {
        var fields = Find($"[{string.Join(',', Enumerable.Range(0, 25))}]");

        Assert.Equal(21, fields.Count);
        Assert.Contains(fields, f => f.Path == "[19]");
        Assert.DoesNotContain(fields, f => f.Path == "[20]");
    }

    [Fact]
    public void Keeps_the_last_sixty_values_of_a_field()
    {
        var fields = Find($"[{string.Join(',', Enumerable.Range(0, 100))}]");

        var values = fields.Single(f => f.Path == "[*]").Values;
        Assert.Equal(60, values.Count);
        Assert.Equal(40, values[0]);
        Assert.Equal(99, values[^1]);
    }

    [Fact]
    public void Finds_at_most_five_hundred_fields()
    {
        var members = Enumerable.Range(0, 600).Select(i => $"\"f{i}\":{i}");

        Assert.Equal(500, Find($"{{{string.Join(',', members)}}}").Count);
    }

    [Theory]
    [InlineData("a", false)]
    [InlineData("", false)]
    [InlineData("a[0]", true)]
    [InlineData("a[0].b", true)]
    [InlineData("a[*]", false)]
    [InlineData("a[*].b", false)]
    [InlineData("a[*][0]", false)]
    public void Tells_one_item_from_a_value_or_a_whole_array(string path, bool expected)
        => Assert.Equal(expected, JsonNumbers.IsOneItem(path));
}
