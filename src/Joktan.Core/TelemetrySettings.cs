using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Joktan.Core;

/// <summary>How Joktan signs in to Elasticsearch.</summary>
public enum ElasticAuthentication
{
    None,
    ApiKey,
    Password,
}

/// <summary>Where logs go and whether metrics can be scraped; kept in settings/telemetry.json. The API key or password is in the credential store.</summary>
public sealed class TelemetrySettings
{
    private const string SecretAccount = "elasticsearch";

    public bool LogsToFile { get; set; }

    public bool LogsToElastic { get; set; }

    public bool LogsToCollector { get; set; } = true;

    /// <summary>Where log files go; empty for logs/ in the data folder.</summary>
    public string LogFolder { get; set; } = "";

    /// <summary>How many days of log files are kept.</summary>
    public int LogFileDays { get; set; } = 7;

    /// <summary>
    /// The collector's OTLP address for metrics, traces and logs. It wins over OTEL_EXPORTER_OTLP_ENDPOINT, which an
    /// IDE such as Rider may set for what it runs; empty leaves it to that variable.
    /// </summary>
    public string CollectorEndpoint { get; set; } = "http://localhost:4317";

    [JsonConverter(typeof(JsonStringEnumConverter<LogLevel>))]
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>The address of Elasticsearch, on this computer or in the cloud.</summary>
    public string ElasticUrl { get; set; } = "http://localhost:9200";

    [JsonConverter(typeof(JsonStringEnumConverter<ElasticAuthentication>))]
    public ElasticAuthentication ElasticAuthentication { get; set; } = ElasticAuthentication.None;

    public string ElasticUsername { get; set; } = "";

    /// <summary>The data stream logs go to. The name fits the logs template Elasticsearch comes with.</summary>
    public string ElasticDataStream { get; set; } = "logs-joktan-default";

    public bool MetricsEndpointEnabled { get; set; }

    public int MetricsPort { get; set; } = 9464;

    /// <summary>Whether the metrics endpoint answers other computers, rather than only this one. It has no login.</summary>
    public bool MetricsFromOtherComputers { get; set; }

    /// <summary>The API key or password, read from the credential store. Never written to the file.</summary>
    [JsonIgnore]
    public string Secret { get; set; } = "";

    private static string FilePath => Path.Combine(DataFolder.For("settings"), "telemetry.json");

    public static bool IsValidUrl(string text)
        => Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    /// <summary>Whether a name can be a data stream: lowercase, with no spaces or characters Elasticsearch refuses.</summary>
    public static bool IsValidDataStream(string text)
        => text.Length > 0 && text[0] is not ('-' or '_' or '+') && text.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.');

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    public static bool IsValidDays(int days) => days is >= 1 and <= 3650;

    /// <summary>Where log files go now. ~ stands for the home folder.</summary>
    [JsonIgnore]
    public string EffectiveLogFolder => LogFolder.Trim() is { Length: > 0 } folder ? ExpandHome(folder) : DataFolder.For("logs");

    /// <summary>Whether a text is empty, or a whole path, perhaps starting with ~.</summary>
    public static bool IsValidFolder(string text)
        => text.Trim().Length == 0 || Path.IsPathFullyQualified(ExpandHome(text.Trim()));

    /// <summary>A path with a leading ~ standing for the home folder.</summary>
    public static string ExpandHome(string path)
        => path.StartsWith('~') ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..].TrimStart('/', '\\')) : path;

    public static TelemetrySettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<TelemetrySettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings leave logs and the endpoint off.
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

    /// <summary>Reads the API key or password, when there is one to read.</summary>
    public void ReadSecret(CredentialStore store)
    {
        if (ElasticAuthentication != ElasticAuthentication.None)
            Secret = store.Read(SecretAccount) ?? "";
    }

    /// <summary>Keeps the API key or password. Whether it could be saved.</summary>
    public bool WriteSecret(CredentialStore store, string secret)
    {
        Secret = secret;
        if (secret.Length == 0)
        {
            store.Delete(SecretAccount);
            return true;
        }

        return store.Write(SecretAccount, secret);
    }

    /// <summary>Where to send logs now, or null when nothing should be sent.</summary>
    public ElasticTarget? Target()
    {
        if (!LogsToElastic || !IsValidUrl(ElasticUrl) || !IsValidDataStream(ElasticDataStream))
            return null;

        return new ElasticTarget(new Uri(ElasticUrl.Trim().TrimEnd('/') + "/"), ElasticDataStream, Authorization());
    }

    /// <summary>The value of the Authorization header, if signing in.</summary>
    public string? Authorization() => ElasticAuthentication switch
    {
        ElasticAuthentication.ApiKey when Secret.Length > 0 => $"ApiKey {Secret}",
        ElasticAuthentication.Password when Secret.Length > 0 =>
            "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{ElasticUsername}:{Secret}")),
        _ => null,
    };
}
