using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Joktan.Core;

namespace Joktan.Settings.Backup;

/// <summary>Where backups go, how many are kept and what they hold; kept in <c>backup.json</c>.</summary>
public sealed class BackupSettings
{
    public const int DefaultKeep = 10;
    public const int MaxKeep = 100;

    /// <summary>The folder backups go to; empty for a folder in the user's documents.</summary>
    public string Folder { get; set; } = "";

    public int KeepLast { get; set; } = DefaultKeep;

    /// <summary>What was ticked or cleared, by item. An item not listed takes what it comes with.</summary>
    public Dictionary<string, bool> Included { get; set; } = [];

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Joktan Backups");

    public string EffectiveFolder => Folder.Trim() is { Length: > 0 } folder ? folder : DefaultFolder;

    private static string FilePath => Path.Combine(DataFolder.Path, "backup.json");

    public static BackupSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<BackupSettings>(File.ReadAllText(FilePath)) is { } settings)
            {
                settings.Folder ??= "";
                settings.Included ??= [];
                settings.KeepLast = Math.Clamp(settings.KeepLast, 1, MaxKeep);
                return settings;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable file is treated as none.
        }

        return new BackupSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Used until the app closes.
        }
    }
}
