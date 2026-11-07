using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Joktan.Agent.Mcp;
using Joktan.Agent.Resources;
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
            Description = Description(tool),
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

    /// <summary>In Markdown: what the call does, how to send it from the API Client, and what each argument is.</summary>
    public static string Description(McpTool tool)
    {
        var text = new StringBuilder();
        if (tool.Description.Length > 0)
            text.AppendLine(tool.Description).AppendLine();

        text.AppendLine(Strings.ApiClientExport_HowToUse);
        text.AppendLine().AppendLine(tool.Parameters.Count == 0 ? Strings.ApiClientExport_NoArguments
            : OpenApi.TakesGet(tool) ? Strings.ApiClientExport_GetArguments
            : Strings.ApiClientExport_PostArguments);
        text.AppendLine().AppendLine(tool.Risk switch
        {
            ToolRisk.Read => Strings.ApiClientExport_OnlyLooks,
            ToolRisk.Change => Strings.ApiClientExport_Changes,
            ToolRisk.Risky => Strings.ApiClientExport_Risky,
            _ => Strings.ApiClientExport_Secret,
        });
        text.AppendLine().AppendLine(string.Format(Strings.ApiClientExport_Key, KeyVariable, Environment));

        if (tool.Parameters.Count > 0)
        {
            text.AppendLine().AppendLine(Strings.ApiClientExport_Arguments).AppendLine();
            foreach (var parameter in tool.Parameters)
                text.AppendLine(string.Format(Strings.ApiClientExport_Argument, parameter.Name, About(parameter), parameter.Description));
        }

        return text.ToString().TrimEnd();
    }

    // Such as "text, required, one of markdown, text".
    private static string About(ToolParameter parameter)
    {
        var about = new List<string>
        {
            parameter.Type switch
            {
                "number" => Strings.ApiClientExport_Number,
                "boolean" => Strings.ApiClientExport_TrueOrFalse,
                "object" => Strings.ApiClientExport_Object,
                _ => Strings.ApiClientExport_Text,
            },
            parameter.Required ? Strings.ApiClientExport_Required : Strings.ApiClientExport_Optional,
        };
        if (parameter.Choices is { Count: > 0 } choices)
            about.Add(string.Format(Strings.ApiClientExport_OneOf, string.Join(", ", choices)));
        return string.Join(", ", about);
    }

    // The first choice where there are choices, so that the request shows what can go there.
    private static string Placeholder(ToolParameter parameter) => parameter.Choices is [var first, ..] ? first : "";
}
