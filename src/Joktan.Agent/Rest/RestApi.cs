using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Core;
using Joktan.Workflows.Engine;
using Microsoft.Extensions.Logging;

namespace Joktan.Agent.Rest;

/// <summary>A request as the server read it.</summary>
/// <param name="Path">The path, without the query.</param>
/// <param name="Query">What followed the question mark, still encoded; empty for none.</param>
public sealed record RestRequest(string Method, string Path, string Query, IReadOnlyDictionary<string, string> Headers, string Body);

/// <summary>What to answer a request with: JSON, or for the docs, a file.</summary>
public sealed record RestReply(int Status, JsonNode? Body, IReadOnlyDictionary<string, string>? Headers = null)
{
    public const string Json = "application/json; charset=utf-8";

    /// <summary>The file to answer with in place of <see cref="Body"/>.</summary>
    public byte[]? Content { get; init; }

    public string ContentType { get; init; } = Json;

    public static RestReply Problem(int status, string message, IReadOnlyDictionary<string, string>? headers = null)
        => new(status, new JsonObject { ["error"] = message }, headers);

    public static RestReply File(byte[] content, string contentType) => new(200, null) { Content = content, ContentType = contentType };
}

/// <summary>
/// The REST API: each tool of the MCP server is a call at <c>/api/&lt;name&gt;</c>, made with POST and a
/// JSON object of its arguments, or for those that only look, with GET and the arguments in the query.
/// Every request needs a key that is on and has not expired, and only calls that are switched on answer.
/// Tools that hand out a secret are never calls. Runs where the tools may run, on the UI thread.
/// </summary>
public sealed class RestApi(RestApiSettings settings, Func<IReadOnlyList<McpTool>> tools, Func<DateOnly>? today = null)
{
    public const string Root = "/api";

    private readonly Lazy<Dictionary<string, McpTool>> _tools = new(() => tools().Where(RestApiSettings.IsOffered).ToDictionary(t => t.Name));
    private readonly Func<DateOnly> _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
    private readonly ILogger _log = AppLog.For("REST API");

    /// <summary>The workflows' webhook triggers, answered at <c>/hooks/&lt;path&gt;</c>; null while there are none to answer.</summary>
    public WebhookTriggers? Webhooks { get; set; }

    /// <summary>The tools that can be REST calls, whether switched on or not, in the order the MCP server has them.</summary>
    public IReadOnlyCollection<McpTool> Calls => _tools.Value.Values;

    public async Task<RestReply> AnswerAsync(RestRequest request)
    {
        request.Headers.TryGetValue("Host", out var host);

        // On this computer alone, a page elsewhere whose name was pointed at this computer would name itself, which stops DNS rebinding.
        if (!settings.FromOtherComputers && !IsLoopbackHost(host))
            return RestReply.Problem(403, "Call this computer as localhost or 127.0.0.1.");

        // A web page can reach this computer, but says where it came from. Only the docs page, which is served from here, is let in, so nothing asks for CORS.
        if (request.Headers.TryGetValue("Origin", out var origin) && origin.Length > 0 && !IsOwnOrigin(origin, host))
            return RestReply.Problem(403, "Requests from other web pages are not accepted.");

        var path = request.Path.TrimEnd('/');
        if (path.StartsWith(WebhookTriggers.Root + "/", StringComparison.Ordinal))
            return await WebhookAsync(request, Uri.UnescapeDataString(path[(WebhookTriggers.Root.Length + 1)..]));
        if (path != Root && !path.StartsWith(Root + "/", StringComparison.Ordinal))
            return RestReply.Problem(404, "Not found. The calls are under /api.");

        // The docs hold no secrets, and a browser cannot send a key to open them.
        if (settings.SwaggerEnabled && request.Method == "GET")
        {
            if (path == Root + "/openapi.json")
                return new RestReply(200, OpenApi.Document(_tools.Value.Values.Where(settings.IsCallOn), AppVersion));
            if (path == SwaggerUi.Path)
                return RestReply.File(SwaggerUi.Page(), "text/html; charset=utf-8");
            if (path.StartsWith(SwaggerUi.Path + "/", StringComparison.Ordinal))
                return SwaggerUi.Asset(path[(SwaggerUi.Path.Length + 1)..]) is { } asset
                    ? RestReply.File(asset.Content, asset.ContentType)
                    : RestReply.Problem(404, "Not found.");
        }

        var (key, refused) = Authorize(request);
        if (refused is not null)
            return refused;

        if (path == Root)
            return request.Method == "GET" ? List() : NotAllowed("GET");

        var name = Uri.UnescapeDataString(path[(Root.Length + 1)..]);
        if (!_tools.Value.TryGetValue(name, out var tool))
            return RestReply.Problem(404, $"There is no call named {name}. GET {Root} lists them.");
        if (!settings.IsCallOn(tool))
            return RestReply.Problem(403, $"The {name} call is turned off.");

        var allowed = tool.Risk == ToolRisk.Read ? "GET, POST" : "POST";
        JsonElement arguments;
        switch (request.Method)
        {
            case "POST":
                if (Parse(request.Body) is not { } body)
                    return RestReply.Problem(400, "The body must be a JSON object of the call's arguments.");
                arguments = body;
                break;
            case "GET" when tool.Risk == ToolRisk.Read:
                arguments = FromQuery(request.Query);
                break;
            default:
                return NotAllowed(allowed);
        }

        var reply = await RunAsync(tool, arguments);
        _log.LogInformation("{Key} called {Call}: {Status}", key.Name, tool.Name, reply.Status);
        return reply;
    }

    // A workflow's webhook: with a key, as the calls are, unless the workflow lets anyone who can reach the server start it.
    private async Task<RestReply> WebhookAsync(RestRequest request, string hookPath)
    {
        if (Webhooks?.Find(hookPath) is not { } workflow)
            return RestReply.Problem(404, $"No workflow has a webhook at {WebhookTriggers.Root}/{WebhookTriggers.Normalize(hookPath)}.");
        var by = "no key";
        if (workflow.Triggers.WebhookNeedsKey)
        {
            var (key, refused) = Authorize(request);
            if (refused is not null)
                return refused;
            by = key.Name;
        }

        var answer = await Webhooks.AnswerAsync(workflow, new WebhookCall(request.Method, hookPath, request.Query, request.Body));
        _log.LogInformation("{Key} called the webhook of {Workflow}: {Status}", by, workflow.Name, answer.Status);
        return new RestReply(answer.Status, answer.Body);
    }

    private static string AppVersion => typeof(RestApi).Assembly.GetName().Version?.ToString(3) ?? "";

    private static bool IsLoopbackHost(string? host)
        => host is not null && Uri.TryCreate($"http://{host}/", UriKind.Absolute, out var uri) && uri.IsLoopback;

    private static bool IsOwnOrigin(string origin, string? host)
        => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
           && (uri.IsLoopback || (host is not null && string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The key a request holds, or why it is turned away.</summary>
    private (ApiKey Key, RestReply? Refused) Authorize(RestRequest request)
    {
        var challenge = new Dictionary<string, string> { ["WWW-Authenticate"] = "Bearer" };
        if (Secret(request) is not { } secret)
            return (null!, RestReply.Problem(401, "Send an API key, as Authorization: Bearer <key> or X-Api-Key: <key>.", challenge));

        var hash = ApiKey.HashOf(secret);
        if (settings.Keys.FirstOrDefault(k => k.Matches(hash)) is not { } key)
            return (null!, RestReply.Problem(401, "That API key is not known.", challenge));

        return key.StateOn(_today()) switch
        {
            ApiKeyState.Disabled => (key, RestReply.Problem(401, "That API key is turned off.", challenge)),
            ApiKeyState.Expired => (key, RestReply.Problem(401, $"That API key expired on {key.Expires:yyyy-MM-dd}.", challenge)),
            _ => (key, null),
        };
    }

    private static string? Secret(RestRequest request)
    {
        const string bearer = "Bearer ";
        if (request.Headers.TryGetValue("Authorization", out var authorization) && authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
            return authorization[bearer.Length..].Trim() is { Length: > 0 } token ? token : null;
        return request.Headers.TryGetValue("X-Api-Key", out var header) && header.Trim() is { Length: > 0 } key ? key : null;
    }

    private RestReply List()
    {
        var calls = _tools.Value.Values.Where(settings.IsCallOn).Select(t =>
        {
            var definition = t.Definition();
            return (JsonNode)new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["path"] = $"{Root}/{t.Name}",
                ["methods"] = t.Risk == ToolRisk.Read ? new JsonArray("GET", "POST") : new JsonArray("POST"),
                ["arguments"] = definition["inputSchema"]!.DeepClone(),
            };
        });
        return new RestReply(200, new JsonObject { ["calls"] = new JsonArray(calls.ToArray()) });
    }

    private static RestReply NotAllowed(string allowed)
        => RestReply.Problem(405, $"Use {allowed}.", new Dictionary<string, string> { ["Allow"] = allowed });

    private static JsonElement? Parse(string body)
    {
        if (body.Trim().Length == 0)
            return JsonDocument.Parse("{}").RootElement.Clone();
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The arguments in a query, each as text, which the tools read as numbers and flags where they need them.</summary>
    public static JsonElement FromQuery(string query)
    {
        var arguments = new JsonObject();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = Decode(equals < 0 ? pair : pair[..equals]);
            if (name.Length > 0)
                arguments[name] = equals < 0 ? "true" : Decode(pair[(equals + 1)..]);
        }

        return JsonSerializer.SerializeToElement(arguments);
    }

    private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));

    private static async Task<RestReply> RunAsync(McpTool tool, JsonElement arguments)
    {
        try
        {
            var result = await tool.RunAsync(new ToolArgs(arguments));
            var value = result as string is { } text ? JsonValue.Create(text) : JsonSerializer.SerializeToNode(result, JsonFormats.Compact);
            return new RestReply(200, new JsonObject { ["result"] = value });
        }
        catch (ToolFailure e)
        {
            return RestReply.Problem(422, e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or ArgumentException or JsonException or NotSupportedException)
        {
            return RestReply.Problem(500, $"That did not work: {e.Message}");
        }
    }
}
