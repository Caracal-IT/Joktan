using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Joktan.Api.Tests;

public class ResponseCaptureTests
{
    private const string Json = """{"data":{"items":[{"id":7,"name":"seven"}],"a.b":"dotted","nothing":null,"flag":true},"list":[1,2]}""";

    private static (List<ApiPair> Values, List<string> Problems) Read(string body, params (string Name, string Path)[] captures)
        => ResponseCapture.Read(
            new ApiResponse { Body = body },
            captures.Select(c => new ApiPair { Name = c.Name, Value = c.Path }));

    [Theory]
    [InlineData("data.items[0].id", "7")]
    [InlineData("data.items[0].name", "seven")]
    [InlineData("$.data.items[0].name", "seven")]
    [InlineData("$data.flag", "true")]
    [InlineData("""data["a.b"]""", "dotted")]
    [InlineData("list[1]", "2")]
    [InlineData("list", "[1,2]")]
    [InlineData("data.items[0]", """{"id":7,"name":"seven"}""")]
    public void Reads_the_value_at_a_path(string path, string expected)
    {
        var (values, problems) = Read(Json, ("v", path));

        Assert.Empty(problems);
        Assert.Equal(expected, Assert.Single(values).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("$")]
    public void No_path_means_the_whole_body(string path)
    {
        Assert.Equal("""{"a":1}""", Read("""{"a":1}""", ("v", path)).Values.Single().Value);
    }

    [Fact]
    public void The_whole_body_of_text_is_saved_as_it_is_and_JSON_text_without_quotes()
    {
        Assert.Equal("plain", Read("plain", ("v", "")).Values.Single().Value);
        Assert.Equal("text", Read("\"text\"", ("v", "")).Values.Single().Value);
    }

    [Theory]
    [InlineData("missing", "v did not change: the response has no missing.")]
    [InlineData("list[9]", "v did not change: the response has no list[9].")]
    [InlineData("list.a", "v did not change: the response has no list.a.")]
    [InlineData("data[0]", "v did not change: the response has no data[0].")]
    [InlineData("data.nothing", "v did not change: data.nothing is null in the response.")]
    [InlineData("data items", "v did not change: data items is not a path; write it like data.items[0].id.")]
    [InlineData("list[x]", "v did not change: list[x] is not a path; write it like data.items[0].id.")]
    public void Says_why_a_value_cannot_be_read(string path, string expected)
    {
        var (values, problems) = Read(Json, ("v", path));

        Assert.Empty(values);
        Assert.Equal(expected, Assert.Single(problems));
    }

    [Fact]
    public void A_path_needs_a_JSON_body()
    {
        Assert.Equal("v did not change: the body is not JSON, so it has no a.", Read("plain", ("v", "a")).Problems.Single());
    }

    [Fact]
    public void An_empty_body_has_nothing_to_save()
    {
        Assert.Equal("v did not change: the response has no body.", Read("", ("v", "")).Problems.Single());
    }

    [Fact]
    public void A_body_cut_short_changes_nothing()
    {
        var (values, problems) = ResponseCapture.Read(
            new ApiResponse { Body = Json, IsCutShort = true },
            [new ApiPair { Name = "a", Value = "list" }, new ApiPair { Name = "b", Value = "data" }]);

        Assert.Empty(values);
        Assert.Equal("The body is too big to save from, so a, b did not change.", Assert.Single(problems));
    }

    [Fact]
    public void Captures_that_are_off_or_unnamed_are_ignored()
    {
        var (values, problems) = ResponseCapture.Read(
            new ApiResponse { IsCutShort = true },
            [new ApiPair { Name = "a", Value = "x", Enabled = false }, new ApiPair { Name = " ", Value = "x" }]);

        Assert.Empty(values);
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(" name ")]
    [InlineData("{{name}}")]
    [InlineData("{{ name }}")]
    public void A_variable_may_be_named_as_it_is_used(string name)
    {
        Assert.Equal("name", Read(Json, (name, "list")).Values.Single().Name);
    }

    [Fact]
    public void Reads_what_it_can_and_reports_the_rest()
    {
        var (values, problems) = Read(Json, ("a", "list[0]"), ("b", "missing"));

        Assert.Equal("a", Assert.Single(values).Name);
        Assert.Single(problems);
    }
}
