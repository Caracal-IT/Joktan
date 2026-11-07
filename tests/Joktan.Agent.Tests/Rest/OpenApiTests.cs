using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Agent.Rest;
using Xunit;

namespace Joktan.Agent.Tests.Rest;

public class OpenApiTests
{
    private static McpTool Tool(string name, string category, ToolRisk risk, params ToolParameter[] parameters)
        => new(name, $"Does {name}.", risk, parameters, _ => Task.FromResult<object?>(null)) { Category = category };

    private static readonly McpTool[] Calls =
    [
        Tool("notes_list", "Notes", ToolRisk.Read),
        Tool("notes_update", "Notes", ToolRisk.Change, P.Text("id", "The id."), P.OptText("title", "The title.")),
        Tool("mqtt_clients", "MQTT", ToolRisk.Read, P.OptChoice("state", "Which.", "connected", "all")),
        Tool("settings_search", "Settings", ToolRisk.Read, P.Map("areas", "What to search.")),
    ];

    private static JsonObject Document() => OpenApi.Document(Calls, "1.2.3");

    [Fact]
    public void Is_openapi_3_with_the_api_as_its_server()
    {
        var document = Document();

        Assert.Equal("3.0.3", document["openapi"]!.GetValue<string>());
        Assert.Equal("1.2.3", document["info"]!["version"]!.GetValue<string>());
        Assert.Equal("/api", document["servers"]![0]!["url"]!.GetValue<string>());
    }

    [Fact]
    public void Groups_the_calls_by_category_as_tags()
    {
        var document = Document();

        Assert.Equal(["Notes", "MQTT", "Settings"], document["tags"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()));
        Assert.Equal("MQTT", document["paths"]!["/mqtt_clients"]!["post"]!["tags"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Offers_get_for_calls_that_only_look()
    {
        var paths = Document()["paths"]!;

        Assert.NotNull(paths["/notes_list"]!["get"]);
        Assert.Null(paths["/notes_update"]!["get"]);
        Assert.NotNull(paths["/notes_update"]!["post"]);
        // A query cannot hold an object.
        Assert.Null(paths["/settings_search"]!["get"]);
    }

    [Fact]
    public void Describes_the_arguments_of_get_in_the_query()
    {
        var parameter = Document()["paths"]!["/mqtt_clients"]!["get"]!["parameters"]![0]!;

        Assert.Equal("state", parameter["name"]!.GetValue<string>());
        Assert.Equal("query", parameter["in"]!.GetValue<string>());
        Assert.False(parameter["required"]!.GetValue<bool>());
        Assert.Equal(["connected", "all"], parameter["schema"]!["enum"]!.AsArray().Select(e => e!.GetValue<string>()));
    }

    [Fact]
    public void Describes_the_arguments_of_post_as_a_json_body()
    {
        var schema = Document()["paths"]!["/notes_update"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!;

        Assert.Equal(["id", "title"], schema["properties"]!.AsObject().Select(p => p.Key));
        Assert.Equal(["id"], schema["required"]!.AsArray().Select(r => r!.GetValue<string>()));
    }

    [Fact]
    public void Leaves_out_an_empty_list_of_required_arguments()
    {
        var schema = Document()["paths"]!["/notes_list"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!;

        Assert.Null(schema["required"]);
    }

    [Fact]
    public void Operation_ids_are_unique()
    {
        var ids = Document()["paths"]!.AsObject()
            .SelectMany(p => p.Value!.AsObject().Select(o => o.Value!["operationId"]!.GetValue<string>()))
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Asks_for_a_key_as_a_bearer_token_or_a_header()
    {
        var document = Document();

        var schemes = document["components"]!["securitySchemes"]!;
        Assert.Equal("bearer", schemes["bearer"]!["scheme"]!.GetValue<string>());
        Assert.Equal("X-Api-Key", schemes["apiKey"]!["name"]!.GetValue<string>());
        Assert.Equal(2, document["security"]!.AsArray().Count);
    }
}
