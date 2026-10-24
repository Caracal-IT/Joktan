using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Joktan.Compose;

/// <summary>Which compose file the Containers page manages and with which engine; kept in containers.json.</summary>
public sealed class ComposeSettings
{
    public const string FileName = "containers.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The compose file; empty for the repository's containers/docker-compose.yml when Joktan runs from it.</summary>
    public string ComposeFile { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter<ContainerEngine>))]
    public ContainerEngine Engine { get; set; } = ContainerEngine.Auto;

    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    public static ComposeSettings Load(string? folder = null)
    {
        try
        {
            var path = PathIn(folder ?? DataFolder.Path);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<ComposeSettings>(File.ReadAllText(path), Options) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new();
    }

    public void Save(string? folder = null)
    {
        folder ??= DataFolder.Path;
        Directory.CreateDirectory(folder);
        File.WriteAllText(PathIn(folder), JsonSerializer.Serialize(this, Options));
    }

    /// <summary>
    /// containers/docker-compose.yml in the first folder above Joktan's own, or above the current
    /// folder, that has one; null when there is none, such as in an installed copy.
    /// </summary>
    public static string? FindRepositoryComposeFile()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
            {
                var candidate = Path.Combine(folder.FullName, "containers", "docker-compose.yml");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
