using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Agent.Rest;
using Joktan.Api;
using Xunit;

namespace Joktan.Agent.Tests.Rest;

public class ApiClientExportTests
{
    private static McpTool Tool(string name, string category, ToolRisk risk, params ToolParameter[] parameters)
        => new(name, "", risk, parameters, _ => Task.FromResult<object?>(null)) { Category = category };

    [Fact]
    public void Puts_each_call_in_a_folder_for_its_category()
    {
        var requests = ApiClientExport.Requests([Tool("notes_list", "Notes", ToolRisk.Read), Tool("mqtt_publish", "MQTT", ToolRisk.Risky)]);

        Assert.Equal([["Joktan REST API", "Notes"], ["Joktan REST API", "MQTT"]], requests.Select(r => r.Folders.ToArray()));
        Assert.Equal(["notes_list", "mqtt_publish"], requests.Select(r => r.Name));
    }

    [Fact]
    public void Reaches_the_api_through_the_environment_s_variables()
    {
        var request = ApiClientExport.Request(Tool("notes_list", "Notes", ToolRisk.Read));

        Assert.Equal("{{joktanUrl}}/notes_list", request.Url);
        Assert.Equal(ApiAuthKind.Bearer, request.AuthKind);
        Assert.Equal("{{joktanKey}}", request.Token);
    }

    [Fact]
    public void A_call_that_only_looks_is_a_get_with_the_optional_arguments_switched_off()
    {
        var request = ApiClientExport.Request(Tool("notes_read", "Notes", ToolRisk.Read, P.Text("id", "The id."), P.OptChoice("format", "How.", "markdown", "text")));

        Assert.Equal("GET", request.Method);
        Assert.Equal(ApiBodyKind.None, request.BodyKind);
        Assert.Equal(["id", "format"], request.Query.Select(q => q.Name));
        Assert.Equal([true, false], request.Query.Select(q => q.Enabled));
        Assert.Equal("markdown", request.Query[1].Value);
    }

    [Fact]
    public void Any_other_call_is_a_post_with_a_body_of_its_required_arguments()
    {
        var request = ApiClientExport.Request(Tool("mqtt_publish", "MQTT", ToolRisk.Risky,
            P.Text("client", "Which."), P.OptText("body", "What."), P.OptNumber("qos", "0 to 2."), P.Number("count", "How many."), P.Flag("retain", "Keep it.")));

        Assert.Equal("POST", request.Method);
        Assert.Equal(ApiBodyKind.Json, request.BodyKind);
        var body = JsonNode.Parse(request.Body)!.AsObject();
        Assert.Equal(["client", "count", "retain"], body.Select(p => p.Key));
        Assert.Equal(0, body["count"]!.GetValue<int>());
        Assert.False(body["retain"]!.GetValue<bool>());
    }

    [Fact]
    public void Gives_the_address_and_leaves_the_key_to_be_typed_in()
    {
        var variables = ApiClientExport.Variables(50000);

        Assert.Equal("http://localhost:50000/api", variables.Single(v => v.Name == "joktanUrl").Value);
        Assert.Equal("", variables.Single(v => v.Name == "joktanKey").Value);
    }
}
