using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class VaryingFieldsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 30, 0, 123, TimeSpan.Zero);

    private readonly Dictionary<string, long> _counters = [];

    private static JsonNode Body(string json) => JsonNode.Parse(json)!;

    private JsonNode? Generate(FieldRule rule) => VaryingFields.Generate(rule, rule.Path, _counters, Now);

    [Fact]
    public void Times_come_from_the_clock_given()
    {
        Assert.Equal(Now.ToUnixTimeSeconds(), Generate(new FieldRule("a", FieldType.UnixSeconds))!.GetValue<long>());
        Assert.Equal(Now.ToUnixTimeMilliseconds(), Generate(new FieldRule("a", FieldType.UnixMilliseconds))!.GetValue<long>());
        Assert.Equal("2026-11-02T08:30:00.123Z", Generate(new FieldRule("a", FieldType.IsoTimestamp))!.GetValue<string>());
    }

    [Fact]
    public void An_ISO_timestamp_is_always_in_UTC()
    {
        var elsewhere = new DateTimeOffset(2026, 11, 2, 10, 30, 0, TimeSpan.FromHours(2));

        var value = VaryingFields.Generate(new FieldRule("a", FieldType.IsoTimestamp), "a", _counters, elsewhere);

        Assert.Equal("2026-11-02T08:30:00.000Z", value!.GetValue<string>());
    }

    [Fact]
    public void A_GUID_is_new_each_time()
    {
        var rule = new FieldRule("a", FieldType.Guid);

        var first = Generate(rule)!.GetValue<string>();
        var second = Generate(rule)!.GetValue<string>();

        Assert.True(Guid.TryParse(first, out _));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_counter_starts_and_steps_as_set()
    {
        var rule = new FieldRule("a", FieldType.Counter) { Start = 10, Step = 5 };

        Assert.Equal(10, Generate(rule)!.GetValue<long>());
        Assert.Equal(15, Generate(rule)!.GetValue<long>());
        Assert.Equal(20, _counters["a"]);
    }

    [Fact]
    public void A_counter_can_count_down()
    {
        var rule = new FieldRule("a", FieldType.Counter) { Start = 1, Step = -1 };

        Generate(rule);

        Assert.Equal(0, Generate(rule)!.GetValue<long>());
    }

    [Fact]
    public void A_random_integer_stays_in_its_range_and_reaches_both_ends()
    {
        var rule = new FieldRule("a", FieldType.RandomInteger) { Min = 1, Max = 3 };

        var seen = Enumerable.Range(0, 500).Select(_ => Generate(rule)!.GetValue<long>()).ToHashSet();

        Assert.Equal([1L, 2L, 3L], seen.Order());
    }

    [Fact]
    public void A_random_integer_rounds_its_range_inward()
    {
        var rule = new FieldRule("a", FieldType.RandomInteger) { Min = 1.5, Max = 2.5 };

        Assert.Equal(2, Generate(rule)!.GetValue<long>());
    }

    [Fact]
    public void A_random_integer_with_no_whole_number_in_range_uses_the_lower_end()
    {
        var rule = new FieldRule("a", FieldType.RandomInteger) { Min = 1.2, Max = 1.8 };

        Assert.Equal(2, Generate(rule)!.GetValue<long>());
    }

    [Fact]
    public void A_random_number_stays_in_its_range_with_its_decimals()
    {
        var rule = new FieldRule("a", FieldType.RandomNumber) { Min = 1, Max = 2, Decimals = 2 };

        foreach (var _ in Enumerable.Range(0, 200))
        {
            var value = Generate(rule)!.GetValue<double>();
            Assert.InRange(value, 1, 2);
            Assert.Equal(Math.Round(value, 2), value);
        }
    }

    [Fact]
    public void One_of_picks_from_the_list()
    {
        var rule = new FieldRule("a", FieldType.OneOf) { Values = ["x", "y"] };

        var seen = Enumerable.Range(0, 200).Select(_ => Generate(rule)!.GetValue<string>()).ToHashSet();

        Assert.Equal(["x", "y"], seen.Order());
    }

    [Fact]
    public void Lists_with_nothing_in_them_give_null()
    {
        Assert.Null(Generate(new FieldRule("a", FieldType.OneOf)));
        Assert.Null(Generate(new FieldRule("a", FieldType.Cycle)));
    }

    [Fact]
    public void A_cycle_goes_round_in_turn()
    {
        var rule = new FieldRule("a", FieldType.Cycle) { Values = ["x", "y"] };

        var values = Enumerable.Range(0, 5).Select(_ => Generate(rule)!.GetValue<string>());

        Assert.Equal(["x", "y", "x", "y", "x"], values);
    }

    [Fact]
    public void A_cycle_survives_its_list_getting_shorter()
    {
        _counters["a"] = 5;
        var rule = new FieldRule("a", FieldType.Cycle) { Values = ["x", "y"] };

        Assert.Equal("y", Generate(rule)!.GetValue<string>());
    }

    [Fact]
    public void A_fixed_value_is_a_copy_each_time()
    {
        var value = new JsonObject { ["a"] = 1 };
        var rule = new FieldRule("f", FieldType.Fixed) { Value = value };

        var generated = Generate(rule);

        Assert.True(JsonNode.DeepEquals(value, generated));
        Assert.NotSame(value, generated);
        Assert.Null(Generate(new FieldRule("n", FieldType.Fixed)));
    }

    [Fact]
    public void Apply_leaves_the_original_alone()
    {
        var body = Body("""{"a":1}""");

        var result = VaryingFields.Apply(body, [new FieldRule("a", FieldType.Fixed) { Value = 2 }], _counters, Now);

        Assert.Equal("""{"a":1}""", body.ToJsonString());
        Assert.Equal("""{"a":2}""", result.ToJsonString());
    }

    [Theory]
    [InlineData("""{"a":{"b":1}}""", "a.b", """{"a":{"b":9}}""")]
    [InlineData("""{"a":[1,2]}""", "a[1]", """{"a":[1,9]}""")]
    [InlineData("""{"a":[{"b":1}]}""", "a[0].b", """{"a":[{"b":9}]}""")]
    [InlineData("""[1,2]""", "[0]", """[9,2]""")]
    [InlineData("""[{"t":1}]""", "[0].t", """[{"t":9}]""")]
    [InlineData("""{"a":[[1],[2]]}""", "a[1][0]", """{"a":[[1],[9]]}""")]
    [InlineData("""{"a":null}""", "a", """{"a":9}""")]
    public void Paths_reach_inside_objects_and_arrays(string body, string path, string expected)
    {
        var result = VaryingFields.Apply(Body(body), [new FieldRule(path, FieldType.Fixed) { Value = 9 }], _counters, Now);

        Assert.Equal(expected, result.ToJsonString());
    }

    [Fact]
    public void A_star_reaches_every_item_and_a_counter_counts_across_them()
    {
        var body = Body("""{"r":[{"n":0},{"n":0},{"n":0}]}""");

        var result = VaryingFields.Apply(body, [new FieldRule("r[*].n", FieldType.Counter)], _counters, Now);

        Assert.Equal("""{"r":[{"n":1},{"n":2},{"n":3}]}""", result.ToJsonString());
    }

    [Fact]
    public void A_star_at_the_top_reaches_a_body_that_is_an_array()
    {
        var result = VaryingFields.Apply(Body("[0,0]"), [new FieldRule("[*]", FieldType.Counter)], _counters, Now);

        Assert.Equal("[1,2]", result.ToJsonString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("a.missing")]
    [InlineData("a.b.c")]
    [InlineData("r[5]")]
    [InlineData("a[0]")]
    [InlineData("a[*]")]
    [InlineData("")]
    [InlineData("a..b")]
    [InlineData(".a")]
    [InlineData("r[")]
    [InlineData("r[x]")]
    [InlineData("r[-1]")]
    [InlineData("r]0[")]
    [InlineData("a.[0]")]
    public void A_path_that_reaches_nothing_is_missing(string path)
    {
        var body = Body("""{"a":{"b":1},"r":[1]}""");

        Assert.Equal(path, VaryingFields.FindMissing(body, [new FieldRule(path, FieldType.Guid)]));
    }

    [Fact]
    public void Finds_the_first_missing_field_or_none()
    {
        var body = Body("""{"a":1,"r":[]}""");
        var found = new FieldRule("a", FieldType.Guid);
        var missing = new FieldRule("b", FieldType.Guid);

        Assert.Null(VaryingFields.FindMissing(body, [found]));
        Assert.Equal("b", VaryingFields.FindMissing(body, [found, missing]));
        Assert.Equal("r[*]", VaryingFields.FindMissing(body, [new FieldRule("r[*]", FieldType.Guid)]));
    }

    [Fact]
    public void Checks_that_an_array_rule_can_be_used()
    {
        var body = Body("""{"a":[1],"e":[],"n":1}""");

        Assert.Null(VaryingFields.CheckArray(body, new ArrayRule("a", 5, 5)));
        Assert.Null(VaryingFields.CheckArray(body, new ArrayRule("e", 0, 0)));
        Assert.Equal("Array missing is not in the body", VaryingFields.CheckArray(body, new ArrayRule("missing", 1, 1)));
        Assert.Equal("n is not an array", VaryingFields.CheckArray(body, new ArrayRule("n", 1, 1)));
        Assert.Equal("Array e is empty in the file, so it has no item to repeat", VaryingFields.CheckArray(body, new ArrayRule("e", 1, 1)));
    }

    [Fact]
    public void An_array_is_filled_from_its_items_in_turn()
    {
        var result = VaryingFields.Apply(Body("""{"a":[1,2]}"""), [new ArrayRule("a", 5, 5)], [], _counters, Now);

        Assert.Equal("""{"a":[1,2,1,2,1]}""", result.ToJsonString());
    }

    [Theory]
    [InlineData(0, """{"a":[]}""")]
    [InlineData(1, """{"a":[1]}""")]
    public void An_array_can_be_made_shorter(int count, string expected)
    {
        var result = VaryingFields.Apply(Body("""{"a":[1,2]}"""), [new ArrayRule("a", count, count)], [], _counters, Now);

        Assert.Equal(expected, result.ToJsonString());
    }

    [Fact]
    public void An_array_size_in_a_range_stays_in_the_range()
    {
        var sizes = Enumerable.Range(0, 200)
            .Select(_ => VaryingFields.Apply(Body("""{"a":[1]}"""), [new ArrayRule("a", 1, 3)], [], _counters, Now)["a"]!.AsArray().Count)
            .ToHashSet();

        Assert.Equal([1, 2, 3], sizes.Order());
    }

    [Fact]
    public void Array_rules_apply_before_field_rules()
    {
        var result = VaryingFields.Apply(
            Body("""{"r":[{"n":0}]}"""),
            [new ArrayRule("r", 3, 3)],
            [new FieldRule("r[*].n", FieldType.Counter)],
            _counters,
            Now);

        Assert.Equal("""{"r":[{"n":1},{"n":2},{"n":3}]}""", result.ToJsonString());
    }

    [Fact]
    public void A_star_reaches_each_nested_array_for_an_array_rule()
    {
        var result = VaryingFields.Apply(Body("""{"o":[{"a":[1]},{"a":[2]}]}"""), [new ArrayRule("o[*].a", 2, 2)], [], _counters, Now);

        Assert.Equal("""{"o":[{"a":[1,1]},{"a":[2,2]}]}""", result.ToJsonString());
    }

    [Fact]
    public void An_array_rule_skips_what_is_not_an_array_or_is_empty()
    {
        var result = VaryingFields.Apply(Body("""{"a":1,"e":[]}"""), [new ArrayRule("a", 2, 2), new ArrayRule("e", 2, 2)], [], _counters, Now);

        Assert.Equal("""{"a":1,"e":[]}""", result.ToJsonString());
    }

    [Fact]
    public void Gets_the_value_at_the_first_place_a_path_reaches()
    {
        var body = Body("""{"a":{"b":7},"r":[5,6],"n":null}""");

        Assert.True(VaryingFields.TryGetValue(body, "a.b", out var value));
        Assert.Equal(7, value!.GetValue<int>());

        Assert.True(VaryingFields.TryGetValue(body, "r[*]", out value));
        Assert.Equal(5, value!.GetValue<int>());

        Assert.True(VaryingFields.TryGetValue(body, "n", out value));
        Assert.Null(value);

        Assert.False(VaryingFields.TryGetValue(body, "missing", out _));
        Assert.False(VaryingFields.TryGetValue(null, "a", out _));
    }

    [Fact]
    public void Lists_every_value_then_the_same_with_stars()
    {
        var paths = VaryingFields.ListPaths(Body("""{"a":1,"b":{"c":"x"},"r":[{"v":1},{"v":2}],"n":null}"""));

        Assert.Equal(["a", "b.c", "r[0].v", "r[1].v", "n", "r[*].v"], paths);
    }

    [Fact]
    public void Lists_the_values_of_a_body_that_is_an_array()
    {
        Assert.Equal(["[0]", "[1]", "[*]"], VaryingFields.ListPaths(Body("[1,2]")));
    }

    [Fact]
    public void Lists_nothing_for_a_bare_value_or_nothing()
    {
        Assert.Empty(VaryingFields.ListPaths(Body("1")));
        Assert.Empty(VaryingFields.ListPaths(null));
    }

    [Fact]
    public void Lists_each_array_once_with_stars_for_those_inside_arrays()
    {
        var paths = VaryingFields.ListArrayPaths(Body("""{"r":[{"s":[1]},{"s":[2]}],"e":[],"o":{"t":[]},"n":1}"""));

        Assert.Equal(["r", "r[*].s", "e", "o.t"], paths);
    }

    [Fact]
    public void Does_not_list_a_body_that_is_itself_an_array_but_does_list_inside_it()
    {
        Assert.Equal(["[*].a"], VaryingFields.ListArrayPaths(Body("""[{"a":[1]}]""")));
        Assert.Empty(VaryingFields.ListArrayPaths(null));
    }
}
