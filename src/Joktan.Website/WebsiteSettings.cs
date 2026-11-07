using System;
using System.IO;
using System.Text.Json;
using Joktan.Core;

namespace Joktan.Website;

/// <summary>Settings / Website: whether the website for answering workflows' questions is up, and where.</summary>
public sealed class WebsiteSettings
{
    public const int DefaultPort = 47822;

    public const int DefaultSessionHours = 8;

    /// <summary>Whether the website is listening.</summary>
    public bool Enabled { get; set; }

    public int Port { get; set; } = DefaultPort;

    /// <summary>Whether to answer other computers, rather than only this one.</summary>
    public bool FromOtherComputers { get; set; }

    /// <summary>How long someone stays signed in without using the website.</summary>
    public int SessionHours { get; set; } = DefaultSessionHours;

    private static string FilePath => Path.Combine(DataFolder.For("settings"), "website.json");

    public static bool IsValidPort(int port) => port is >= 1024 and <= 65535;

    public static bool IsValidSessionHours(int hours) => hours is >= 1 and <= 24 * 30;

    public TimeSpan SessionLength => TimeSpan.FromHours(IsValidSessionHours(SessionHours) ? SessionHours : DefaultSessionHours);

    public static WebsiteSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<WebsiteSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings start over, with the website off.
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
