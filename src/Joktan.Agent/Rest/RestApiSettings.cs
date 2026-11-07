using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Joktan.Agent.Mcp;
using Joktan.Core;

namespace Joktan.Agent.Rest;

/// <summary>Settings / REST API: whether the app answers REST calls, where, with which keys, and which calls.</summary>
public sealed class RestApiSettings
{
    public const int DefaultPort = 47821;

    /// <summary>Whether the REST API is listening.</summary>
    public bool Enabled { get; set; }

    public int Port { get; set; } = DefaultPort;

    /// <summary>Whether to answer other computers, rather than only this one.</summary>
    public bool FromOtherComputers { get; set; }

    /// <summary>Whether the docs, Swagger UI at /api/docs and the OpenAPI document, are served. They need no key.</summary>
    public bool SwaggerEnabled { get; set; } = true;

    public List<ApiKey> Keys { get; set; } = [];

    /// <summary>The calls switched away from how they start, by name. See <see cref="IsCallOn"/>.</summary>
    public Dictionary<string, bool> Calls { get; set; } = [];

    private static string FilePath => Path.Combine(DataFolder.For("settings"), "rest-api.json");

    public static bool IsValidPort(int port) => port is >= 1024 and <= 65535;

    /// <summary>Whether a tool may be called. Those that only look start on, and those that change something start off.</summary>
    public bool IsCallOn(McpTool tool)
        => IsOffered(tool) && (Calls.TryGetValue(tool.Name, out var on) ? on : tool.Risk == ToolRisk.Read);

    /// <summary>Whether a tool is a REST call at all. Those that hand out a secret never are.</summary>
    public static bool IsOffered(McpTool tool) => tool.Risk != ToolRisk.Secret;

    public void SetCall(McpTool tool, bool on)
    {
        if (on == (tool.Risk == ToolRisk.Read))
            Calls.Remove(tool.Name);
        else
            Calls[tool.Name] = on;
    }

    public static RestApiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<RestApiSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings start over, with the API off.
        }

        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder.For("settings"));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings that cannot be saved are used until the app closes.
        }
    }
}
