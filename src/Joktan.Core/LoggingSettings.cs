using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Joktan.Core;

/// <summary>Whether logs are sent, which ones, and where to; kept in settings/logging.json.</summary>
public sealed class LoggingSettings
{
    public bool Enabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter<LogLevel>))]
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>The OTLP endpoint for logs, metrics and traces; empty for what OTEL_EXPORTER_OTLP_ENDPOINT says, or the default.</summary>
    public string Endpoint { get; set; } = "";

    private static string FilePath => Path.Combine(DataFolder.For("settings"), "logging.json");

    /// <summary>Whether a text is empty, or a whole http or https address.</summary>
    public static bool IsValidEndpoint(string text)
        => text.Trim().Length == 0
           || (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");

    public static LoggingSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LoggingSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings leave logging as it starts.
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
