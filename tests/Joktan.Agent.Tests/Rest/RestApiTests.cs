using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Agent.Rest;
using Xunit;

namespace Joktan.Agent.Tests.Rest;

public class RestApiTests
{
    private static readonly DateOnly Today = new(2026, 11, 7);

    private readonly RestApiSettings _settings = new();
    private readonly string _secret;
    private readonly ApiKey _key;
    private readonly List<string> _ran = [];
    private readonly RestApi _api;

    public RestApiTests()
    {
        (_key, _secret) = ApiKey.Create("Tests", null, DateTimeOffset.Now);
        _settings.Keys.Add(_key);
        _api = new RestApi(_settings, () =>
        [
            McpTool.Sync("notes_list", "Lists the notes.", ToolRisk.Read, [P.OptText("folder", "The folder.")],
                args => { _ran.Add("notes_list"); return new { folder = args.OptText("folder"), count = args.Whole("count") }; }),
            McpTool.Sync("notes_update", "Changes a note.", ToolRisk.Change, [P.Text("title", "The title.")],
                args => { _ran.Add("notes_update"); return $"Updated {args.Text("title")}."; }),
            McpTool.Sync("notes_delete", "Deletes a note.", ToolRisk.Risky, [], _ => throw new IOException("The disk is gone.")),
            McpTool.Sync("passwords_reveal", "Reveals a password.", ToolRisk.Secret, [], _ => { _ran.Add("passwords_reveal"); return "secret"; }),
        ], () => Today);
    }

    private Task<RestReply> Send(string method, string path, string body = "", string query = "", Dictionary<string, string>? headers = null)
    {
        headers ??= new Dictionary<string, string> { ["Authorization"] = $"Bearer {_secret}" };
        var sent = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        sent.TryAdd("Host", "localhost:47821");
        return _api.AnswerAsync(new RestRequest(method, path, query, sent, body));
    }

    private static string Error(RestReply reply) => reply.Body!["error"]!.GetValue<string>();

    [Fact]
    public async Task Turns_away_a_request_without_a_key()
    {
        var reply = await Send("GET", "/api", headers: []);

        Assert.Equal(401, reply.Status);
        Assert.Equal("Bearer", reply.Headers!["WWW-Authenticate"]);
        Assert.Empty(_ran);
    }

    [Fact]
    public async Task Turns_away_a_key_it_does_not_know()
    {
        var reply = await Send("GET", "/api/notes_list", headers: new() { ["Authorization"] = "Bearer jok_nope" });

        Assert.Equal(401, reply.Status);
        Assert.Contains("not known", Error(reply));
        Assert.Empty(_ran);
    }

    [Fact]
    public async Task Takes_the_key_in_an_x_api_key_header_too()
    {
        var reply = await Send("GET", "/api/notes_list", headers: new() { ["X-Api-Key"] = _secret });

        Assert.Equal(200, reply.Status);
    }

    [Fact]
    public async Task Turns_away_a_key_that_is_turned_off()
    {
        _key.Enabled = false;

        var reply = await Send("GET", "/api/notes_list");

        Assert.Equal(401, reply.Status);
        Assert.Contains("turned off", Error(reply));
        Assert.Empty(_ran);
    }

    [Fact]
    public async Task Takes_a_key_on_its_last_day_and_turns_it_away_after()
    {
        _key.Expires = Today;
        Assert.Equal(200, (await Send("GET", "/api/notes_list")).Status);

        _key.Expires = Today.AddDays(-1);
        var reply = await Send("GET", "/api/notes_list");

        Assert.Equal(401, reply.Status);
        Assert.Contains("expired on 2026-11-06", Error(reply));
    }

    [Fact]
    public async Task Turns_away_requests_from_web_pages()
    {
        var reply = await Send("GET", "/api", headers: new() { ["Authorization"] = $"Bearer {_secret}", ["Origin"] = "https://example.com" });

        Assert.Equal(403, reply.Status);
    }

    [Fact]
    public async Task Lists_only_the_calls_that_are_on()
    {
        _settings.SetCall(_api.Calls.Single(c => c.Name == "notes_update"), true);

        var reply = await Send("GET", "/api/");

        Assert.Equal(200, reply.Status);
        var calls = reply.Body!["calls"]!.AsArray();
        Assert.Equal(["notes_list", "notes_update"], calls.Select(c => c!["name"]!.GetValue<string>()));
        Assert.Equal("/api/notes_update", calls[1]!["path"]!.GetValue<string>());
        Assert.Equal(["POST"], calls[1]!["methods"]!.AsArray().Select(m => m!.GetValue<string>()));
        Assert.Equal("title", calls[1]!["arguments"]!["required"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Never_offers_a_tool_that_hands_out_a_secret()
    {
        _settings.Calls["passwords_reveal"] = true;

        var reply = await Send("POST", "/api/passwords_reveal");

        Assert.Equal(404, reply.Status);
        Assert.DoesNotContain(_api.Calls, c => c.Name == "passwords_reveal");
        Assert.Empty(_ran);
    }

    [Fact]
    public async Task A_call_that_is_off_is_forbidden()
    {
        var reply = await Send("POST", "/api/notes_update", """{"title":"Today"}""");

        Assert.Equal(403, reply.Status);
        Assert.Contains("turned off", Error(reply));
        Assert.Empty(_ran);
    }

    [Fact]
    public async Task Runs_a_call_that_is_on_with_the_body_as_its_arguments()
    {
        _settings.Calls["notes_update"] = true;

        var reply = await Send("POST", "/api/notes_update", """{"title":"Today"}""");

        Assert.Equal(200, reply.Status);
        Assert.Equal("Updated Today.", reply.Body!["result"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_call_that_only_looks_takes_get_with_the_arguments_in_the_query()
    {
        var reply = await Send("GET", "/api/notes_list", query: "folder=Work%2FDaily+notes&count=3");

        Assert.Equal(200, reply.Status);
        Assert.Equal("Work/Daily notes", reply.Body!["result"]!["folder"]!.GetValue<string>());
        Assert.Equal(3, reply.Body!["result"]!["count"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_call_that_changes_something_does_not_take_get()
    {
        _settings.Calls["notes_update"] = true;

        var reply = await Send("GET", "/api/notes_update", query: "title=x");

        Assert.Equal(405, reply.Status);
        Assert.Equal("POST", reply.Headers!["Allow"]);
        Assert.Empty(_ran);
    }

    [Fact]
    public async Task An_empty_body_is_no_arguments()
    {
        var reply = await Send("POST", "/api/notes_list");

        Assert.Equal(200, reply.Status);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    public async Task A_body_that_is_not_a_json_object_is_a_bad_request(string body)
    {
        var reply = await Send("POST", "/api/notes_list", body);

        Assert.Equal(400, reply.Status);
        Assert.Empty(_ran);
    }

    [Fact]
    public async Task Says_why_a_call_could_not_do_what_it_was_asked()
    {
        _settings.Calls["notes_update"] = true;

        var reply = await Send("POST", "/api/notes_update", "{}");

        Assert.Equal(422, reply.Status);
        Assert.Equal("title is required.", Error(reply));
    }

    [Fact]
    public async Task A_call_that_fails_on_the_way_is_a_server_error()
    {
        _settings.Calls["notes_delete"] = true;

        var reply = await Send("POST", "/api/notes_delete");

        Assert.Equal(500, reply.Status);
        Assert.Contains("The disk is gone.", Error(reply));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/mcp")]
    [InlineData("/apis")]
    public async Task Nothing_but_the_api_is_there(string path)
    {
        var reply = await Send("GET", path);

        Assert.Equal(404, reply.Status);
    }

    [Fact]
    public async Task An_unknown_call_is_not_found()
    {
        var reply = await Send("POST", "/api/notes_burn");

        Assert.Equal(404, reply.Status);
    }

    [Fact]
    public void Reads_a_query_into_arguments()
    {
        var arguments = RestApi.FromQuery("a=1&b=two+words&flag&=ignored&c=%26");

        Assert.Equal("1", arguments.GetProperty("a").GetString());
        Assert.Equal("two words", arguments.GetProperty("b").GetString());
        Assert.Equal("true", arguments.GetProperty("flag").GetString());
        Assert.Equal("&", arguments.GetProperty("c").GetString());
        Assert.False(arguments.TryGetProperty("", out _));
    }

    [Fact]
    public async Task Leaves_a_result_of_json_as_json()
    {
        var reply = await Send("GET", "/api/notes_list");

        Assert.IsType<JsonObject>(reply.Body!["result"]);
    }

    [Fact]
    public async Task Turns_away_another_host_name_while_on_this_computer_only()
    {
        var reply = await Send("GET", "/api", headers: new() { ["Authorization"] = $"Bearer {_secret}", ["Host"] = "rebound.example:47821" });

        Assert.Equal(403, reply.Status);
        Assert.Empty(_ran);
    }

    [Fact]
    public async Task Answers_any_host_name_once_it_answers_other_computers()
    {
        _settings.FromOtherComputers = true;

        var reply = await Send("GET", "/api/notes_list", headers: new() { ["Authorization"] = $"Bearer {_secret}", ["Host"] = "workstation:47821" });

        Assert.Equal(200, reply.Status);
    }

    [Fact]
    public async Task Lets_in_the_docs_page_served_from_here()
    {
        _settings.FromOtherComputers = true;
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {_secret}", ["Host"] = "workstation:47821", ["Origin"] = "http://workstation:47821" };

        Assert.Equal(200, (await Send("POST", "/api/notes_list", headers: headers)).Status);

        headers["Origin"] = "http://elsewhere:47821";
        Assert.Equal(403, (await Send("POST", "/api/notes_list", headers: headers)).Status);
    }

    [Fact]
    public async Task Serves_the_docs_without_a_key()
    {
        var page = await Send("GET", "/api/docs", headers: []);
        var document = await Send("GET", "/api/openapi.json", headers: []);

        Assert.Equal(200, page.Status);
        Assert.StartsWith("text/html", page.ContentType);
        Assert.Contains("/api/openapi.json", System.Text.Encoding.UTF8.GetString(page.Content!));
        Assert.Equal(200, document.Status);
        Assert.Equal(["/notes_list"], document.Body!["paths"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public async Task Serves_swagger_ui_s_files_and_nothing_else_under_the_docs()
    {
        var script = await Send("GET", "/api/docs/swagger-ui-bundle.js", headers: []);
        var other = await Send("GET", "/api/docs/../settings.json", headers: []);

        Assert.Equal(200, script.Status);
        Assert.StartsWith("text/javascript", script.ContentType);
        Assert.True(script.Content!.Length > 100_000);
        Assert.Equal(404, other.Status);
    }

    [Fact]
    public async Task Serves_no_docs_once_they_are_turned_off()
    {
        _settings.SwaggerEnabled = false;

        Assert.Equal(401, (await Send("GET", "/api/docs", headers: [])).Status);
        Assert.Equal(404, (await Send("GET", "/api/openapi.json")).Status);
    }
}
