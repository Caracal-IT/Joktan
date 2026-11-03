using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joktan.Core;

namespace Joktan.Containers;

/// <summary>Which compose files the Containers page manages and with which engine; kept in containers.json.</summary>
public sealed class ComposeSettings
{
    public const string FileName = "containers.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The single compose file that earlier versions kept. Read into <see cref="ComposeFiles"/> and not written again.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ComposeFile { get; set; }

    /// <summary>The compose files; null until any are chosen.</summary>
    public List<string>? ComposeFiles { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ContainerEngine>))]
    public ContainerEngine Engine { get; set; } = ContainerEngine.Auto;

    /// <summary>Whether the logs and command output are collapsed, leaving the page to the services.</summary>
    public bool LogPanelCollapsed { get; set; }

    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    public static ComposeSettings Load(string? folder = null)
    {
        try
        {
            var path = PathIn(folder ?? DataFolder.For("settings"));
            if (File.Exists(path) && JsonSerializer.Deserialize<ComposeSettings>(File.ReadAllText(path), Options) is { } settings)
            {
                if (settings.ComposeFiles is null && !string.IsNullOrWhiteSpace(settings.ComposeFile))
                    settings.ComposeFiles = [settings.ComposeFile];
                settings.ComposeFile = null;
                return settings;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new();
    }

    public void Save(string? folder = null)
    {
        folder ??= DataFolder.For("settings");
        Directory.CreateDirectory(folder);
        File.WriteAllText(PathIn(folder), JsonSerializer.Serialize(this, Options));
    }
}
