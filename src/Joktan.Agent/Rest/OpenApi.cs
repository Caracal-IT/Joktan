using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Joktan.Agent.Mcp;

namespace Joktan.Agent.Rest;

/// <summary>
/// The REST API described as OpenAPI 3.0, for Swagger UI and any tool that reads it: the calls that
/// are on, grouped by category as tags, with their arguments and the answers they give.
/// </summary>
public static class OpenApi
{
    public const string Version = "3.0.3";

    public static JsonObject Document(IEnumerable<McpTool> calls, string appVersion)
    {
        var list = calls.ToList();
        var paths = new JsonObject();
        foreach (var tool in list)
        {
            var path = new JsonObject();
            if (TakesGet(tool))
                path["get"] = Operation(tool, get: true);
            path["post"] = Operation(tool, get: false);
            paths["/" + tool.Name] = path;
        }

        return new JsonObject
        {
            ["openapi"] = Version,
            ["info"] = new JsonObject
            {
                ["title"] = "Joktan REST API",
                ["version"] = appVersion,
                ["description"] = "Each call does what one of the Joktan agent's tools does, on the same data the app's pages show. "
                                  + "Every call needs an API key from Settings / REST API. Calls that change something run without asking, "
                                  + "and only calls switched on there are listed.",
            },
            ["servers"] = new JsonArray(new JsonObject { ["url"] = RestApi.Root }),
            ["tags"] = new JsonArray(list.Select(t => t.Category).Distinct().Select(c => (JsonNode)new JsonObject { ["name"] = c }).ToArray()),
            ["security"] = new JsonArray(new JsonObject { ["bearer"] = new JsonArray() }, new JsonObject { ["apiKey"] = new JsonArray() }),
            ["paths"] = paths,
            ["components"] = new JsonObject
            {
                ["securitySchemes"] = new JsonObject
                {
                    ["bearer"] = new JsonObject { ["type"] = "http", ["scheme"] = "bearer", ["description"] = "An API key, as Authorization: Bearer <key>." },
                    ["apiKey"] = new JsonObject { ["type"] = "apiKey", ["in"] = "header", ["name"] = "X-Api-Key", ["description"] = "An API key, as X-Api-Key: <key>." },
                },
                ["schemas"] = new JsonObject
                {
                    ["Result"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["result"] = new JsonObject { ["description"] = "What the call gives: text, or JSON." } },
                    },
                    ["Error"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["error"] = new JsonObject { ["type"] = "string" } },
                    },
                },
            },
        };
    }

    /// <summary>Whether the docs offer GET for a call: those that only look, unless an argument is an object, which a query cannot hold.</summary>
    public static bool TakesGet(McpTool tool) => tool.Risk == ToolRisk.Read && tool.Parameters.All(p => p.Type != "object");

    private static JsonObject Operation(McpTool tool, bool get)
    {
        var operation = new JsonObject
        {
            ["tags"] = new JsonArray(tool.Category),
            ["summary"] = tool.Description,
            ["operationId"] = get ? tool.Name + "_get" : tool.Name,
        };

        if (get)
        {
            operation["parameters"] = new JsonArray(tool.Parameters.Select(p => (JsonNode)new JsonObject
            {
                ["name"] = p.Name,
                ["in"] = "query",
                ["required"] = p.Required,
                ["description"] = p.Description,
                ["schema"] = Schema(p),
            }).ToArray());
        }
        else
        {
            var properties = new JsonObject();
            foreach (var parameter in tool.Parameters)
            {
                var property = Schema(parameter);
                property["description"] = parameter.Description;
                properties[parameter.Name] = property;
            }

            var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
            // OpenAPI 3.0 does not allow an empty list of required properties.
            if (tool.Parameters.Where(p => p.Required).Select(p => (JsonNode?)JsonValue.Create(p.Name)).ToArray() is { Length: > 0 } required)
                schema["required"] = new JsonArray(required);
            operation["requestBody"] = new JsonObject
            {
                ["required"] = false,
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = schema } },
            };
        }

        operation["responses"] = new JsonObject
        {
            ["200"] = Response("It worked.", "Result"),
            ["400"] = Response("The body is not a JSON object.", "Error"),
            ["401"] = Response("No key, a key that is not known, or one that is off or past its date.", "Error"),
            ["403"] = Response("The call is off.", "Error"),
            ["422"] = Response("The call could not do what was asked.", "Error"),
            ["500"] = Response("Something failed on the way.", "Error"),
        };
        return operation;
    }

    private static JsonObject Schema(ToolParameter parameter)
    {
        var schema = new JsonObject { ["type"] = parameter.Type };
        if (parameter.Choices is { } choices)
            schema["enum"] = new JsonArray(choices.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray());
        if (parameter.Type == "object")
            schema["additionalProperties"] = new JsonObject { ["type"] = "string" };
        return schema;
    }

    private static JsonObject Response(string description, string schema) => new()
    {
        ["description"] = description,
        ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = $"#/components/schemas/{schema}" } } },
    };
}
