using System;
using System.Text.Json.Nodes;
using Joktan.Workflows.Engine;
using Xunit;

namespace Joktan.Workflows.Tests.Engine;

public class TemplatesTests
{
    private static readonly JsonObject Data = (JsonObject)JsonNode.Parse("""
        {"input": {"name": "Ada", "n": 3, "items": [{"id": "a"}, {"id": "b"}]}, "vars": {"reply": {"body": {"ok": true}}}, "last": "plain"}
        """)!;

    [Theory]
    [InlineData("Hello {{input.name}}", "Hello Ada")]
    [InlineData("{{ input.n }} items", "3 items")]
    [InlineData("first {{input.items[0].id}}, second {{input.items[1].id}}", "first a, second b")]
    [InlineData("{{vars.reply.body}}", """{"ok":true}""")]
    [InlineData("missing: [{{vars.nothing}}]", "missing: []")]
    [InlineData("left for the environment: {{baseUrl}}", "left for the environment: {{baseUrl}}")]
    [InlineData("{{last}}", "plain")]
    public void Render_fills_in_paths(string text, string expected)
        => Assert.Equal(expected, Templates.Render(text, Data));

    [Fact]
    public void A_path_alone_keeps_its_JSON()
    {
        var value = Templates.Value("{{input.items}}", Data);

        Assert.IsType<JsonArray>(value);
        Assert.Equal(2, value!.AsArray().Count);

        // It is a copy, so changing it leaves the run's data alone.
        value.AsArray().Clear();
        Assert.Equal(2, Data["input"]!["items"]!.AsArray().Count);
    }

    [Fact]
    public void Other_text_is_read_as_JSON_when_it_is_JSON_and_as_text_when_not()
    {
        Assert.Equal("""{"name":"Ada","n":3}""", JsonText.Of(Templates.Value("""{"name": "{{input.name}}", "n": {{input.n}}}""", Data)));
        Assert.Equal("\"Hi Ada\"", JsonText.Of(Templates.Value("Hi {{input.name}}", Data)));
        Assert.Equal("42", JsonText.Of(Templates.Value("42", Data)));
        Assert.Null(Templates.Value("   ", Data));
    }

    [Fact]
    public void Paths_are_found_and_checked()
    {
        Assert.Equal(["input.name", "vars.x"], Templates.PathsIn("{{input.name}} {{vars.x}} {{baseUrl}}"));
        Assert.Null(Templates.Check("vars.a[0].b"));
        Assert.NotNull(Templates.Check("other.a"));
        Assert.Null(Templates.Resolve(Data, "input.items[9]"));
        Assert.Null(Templates.Resolve(Data, "input.name.deeper"));
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("2h", 7200)]
    [InlineData("1d", 86400)]
    [InlineData("250ms", 0.25)]
    [InlineData("12", 12)]
    [InlineData(" 1.5M ", 90)]
    public void Durations_are_read_in_their_units(string text, double seconds)
    {
        Assert.True(Durations.TryParse(text, out var duration));
        Assert.Equal(seconds, duration.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("-5s")]
    [InlineData("5x")]
    public void Durations_that_are_not_lengths_of_time_are_refused(string text)
        => Assert.False(Durations.TryParse(text, out _));

    [Fact]
    public void Durations_are_written_as_they_would_be_typed()
    {
        Assert.Equal("1m 30s", Durations.Text(TimeSpan.FromSeconds(90)));
        Assert.Equal("1d 2h", Durations.Text(TimeSpan.FromHours(26)));
        Assert.Equal("250ms", Durations.Text(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void The_paths_into_a_value_are_listed_as_they_would_be_written()
    {
        var paths = Templates.PathsOf(JsonNode.Parse("""{"order": {"id": 7, "lines": [{"sku": "a"}]}, "odd.name": 1}"""));

        Assert.Equal(["{{input}}", "{{input.order}}", "{{input.order.id}}", "{{input.order.lines}}", "{{input.order.lines[0]}}", "{{input.order.lines[0].sku}}"], paths);
    }
}
