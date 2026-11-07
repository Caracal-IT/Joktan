using System;
using System.IO;
using System.Text.Json;
using Joktan.Core;

namespace Joktan.Users;

/// <summary>Which database on Data / Database holds the users and roles. Set on Settings / User Settings; kept in users.json.</summary>
public sealed class UserSettings
{
    public const string FileName = "users.json";

    /// <summary>The id of the database on Data / Database; empty for none yet.</summary>
    public string DatabaseId { get; set; } = "";

    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    public static UserSettings Load(string? folder = null)
    {
        try
        {
            var path = PathIn(folder ?? DataFolder.For("settings"));
            if (File.Exists(path) && JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) is { } settings)
                return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings leave the users without a database until one is chosen.
        }

        return new();
    }

    public void Save(string? folder = null)
    {
        try
        {
            folder ??= DataFolder.For("settings");
            Directory.CreateDirectory(folder);
            File.WriteAllText(PathIn(folder), JsonSerializer.Serialize(this, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings that cannot be saved are used until the app closes.
        }
    }
}
