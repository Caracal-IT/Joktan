using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;

namespace Joktan.Agent.Tools;

/// <summary>Every tool of the app's MCP server. Each drives the same commands as the page it belongs to, so its checks apply and the page shows the result.</summary>
public static class AgentTools
{
    public static IReadOnlyList<McpTool> All(AgentContext app) =>
    [
        .. In("App", AppTools.Create(app)),
        .. In("Notes", NotesTools.Create(app.Notes)),
        .. In("Passwords", PasswordTools.Create(app.Passwords)),
        .. In("Pomodoro", PomodoroTools.Create(app.Pomodoro)),
        .. In("MQTT", MqttTools.Create(app.Mqtt)),
        .. In("Alerts and Messages", InboxTools.Create(app.Inbox, app.Messaging)),
        .. In("Topic Explorer", ExplorerTools.Create(app.Explorer)),
        .. In("MQTT Dashboards", DashboardTools.Create(app.Dashboards)),
        .. In("Certificates", CertificateTools.Create(app.Certificates, app.CertificateDefaults)),
        .. In("API Client", ApiTools.Create(app.Api)),
        .. In("Containers", ContainerTools.Create(app.Containers)),
        .. In("Errors", LogTools.Create(app.Errors)),
        .. In("Settings", SettingsTools.Create(app)),
    ];

    /// <summary>Puts tools in a category. In English, like the tools' descriptions, as the REST API's docs and folders use it.</summary>
    private static IEnumerable<McpTool> In(string category, IEnumerable<McpTool> tools)
    {
        foreach (var tool in tools)
        {
            tool.Category = category;
            yield return tool;
        }
    }

    public static McpTool Read(string name, string description, ToolParameter[] parameters, Func<ToolArgs, object?> run)
        => McpTool.Sync(name, description, ToolRisk.Read, parameters, run);

    public static McpTool ReadAsync(string name, string description, ToolParameter[] parameters, Func<ToolArgs, Task<object?>> run)
        => new(name, description, ToolRisk.Read, parameters, run);

    public static McpTool Change(string name, string description, ToolParameter[] parameters, Func<ToolArgs, object?> run)
        => McpTool.Sync(name, description, ToolRisk.Change, parameters, run);

    public static McpTool ChangeAsync(string name, string description, ToolParameter[] parameters, Func<ToolArgs, Task<object?>> run)
        => new(name, description, ToolRisk.Change, parameters, run);

    public static McpTool Risky(string name, string description, ToolParameter[] parameters, Func<ToolArgs, object?> run)
        => McpTool.Sync(name, description, ToolRisk.Risky, parameters, run);

    public static McpTool RiskyAsync(string name, string description, ToolParameter[] parameters, Func<ToolArgs, Task<object?>> run)
        => new(name, description, ToolRisk.Risky, parameters, run);

    /// <summary>Stops with a reason when something a page reports is set.</summary>
    public static void Check(string? problem)
    {
        if (!string.IsNullOrEmpty(problem))
            throw new ToolFailure(problem);
    }
}
