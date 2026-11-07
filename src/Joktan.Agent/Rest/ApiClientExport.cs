using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Joktan.Agent.Mcp;
using Joktan.Api;
using Joktan.Core;

namespace Joktan.Agent.Rest;

/// <summary>
/// The REST API's calls as requests for Dev Tools / API Client, in a folder of their own with one folder per
/// category. They reach the API through two variables of an environment of their own, so a key is typed in
/// once there, and never written into a request.
/// </summary>
public static class ApiClientExport
{
    public const string Folder = "Joktan REST API";
    public const string Environment = "Joktan REST API";
    public const string UrlVariable = "joktanUrl";
    public const string KeyVariable = "joktanKey";

    public static IReadOnlyList<ApiRequestFile> Requests(IEnumerable<McpTool> calls)
        => calls.Select(t => new ApiRequestFile([Folder, t.Category], t.Name, Request(t))).ToList();

    /// <summary>The address, set every time, and the key, left empty so that one typed in stays.</summary>
    public static IReadOnlyList<ApiPair> Variables(int port) =>
    [
        new() { Name = UrlVariable, Value = $"http://localhost:{port}{RestApi.Root}" },
        new() { Name = KeyVariable, Value = "" },
    ];

    /// <summary>
    /// A GET with the arguments in the query for a call that only looks, the optional ones switched off;
    /// otherwise a POST with a body of the required arguments, to be filled in.
    /// </summary>
    public static ApiRequest Request(McpTool tool)
    {
        var request = new ApiRequest
        {
            Url = $"{{{{{UrlVariable}}}}}/{tool.Name}",
            AuthKind = ApiAuthKind.Bearer,
            Token = $"{{{{{KeyVariable}}}}}",
        };

        if (OpenApi.TakesGet(tool))
        {
            request.Method = "GET";
            request.Query = tool.Parameters.Select(p => new ApiPair { Name = p.Name, Enabled = p.Required, Value = Placeholder(p) }).ToList();
            return request;
        }

        var body = new JsonObject();
        foreach (var parameter in tool.Parameters.Where(p => p.Required))
            body[parameter.Name] = parameter.Type switch
            {
                "number" => 0,
                "boolean" => false,
                "object" => new JsonObject(),
                _ => Placeholder(parameter),
            };
        request.Method = "POST";
        request.BodyKind = ApiBodyKind.Json;
        request.Body = body.ToJsonString(JsonFormats.Indented);
        return request;
    }

    // The first choice where there are choices, so that the request shows what can go there.
    private static string Placeholder(ToolParameter parameter) => parameter.Choices is [var first, ..] ? first : "";
}
