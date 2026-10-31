using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Joktan.Core;

namespace Joktan.Settings.Themes;

/// <summary>The theme in use and the themes the user has saved, in <c>themes.json</c>.</summary>
public sealed class ThemeFile
{
    public string? Current { get; set; }

    public List<Theme>? Themes { get; set; }

    private static string FilePath => Path.Combine(DataFolder.Path, "themes.json");

    public static ThemeFile Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<ThemeFile>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable file leaves the themes as they come.
        }

        return new();
    }

    /// <returns>Why the file could not be written, or null if it was.</returns>
    public string? Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonFormats.Indented));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}
