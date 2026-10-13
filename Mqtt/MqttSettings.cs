using System;
using System.IO;
using System.Text.Json;

namespace Joktan.Mqtt;

/// <summary>
/// What the MQTT page remembers between runs. The password is not part of it; see
/// <see cref="CredentialStore"/>.
/// </summary>
public sealed class MqttSettings
{
    public string Host { get; set; } = "";

    public string Port { get; set; } = "1883";

    public string Username { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string? Folder { get; set; }

    public string ManualTopic { get; set; } = "";

    public string ManualBody { get; set; } = "";

    public int ManualQos { get; set; }

    public bool ManualRetain { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Joktan", "mqtt.json");

    public static MqttSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<MqttSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable settings file is treated as no settings.
        }

        return new MqttSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings that cannot be saved are simply not remembered.
        }
    }
}
