using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joktan.Core;
using Joktan.Logs.Resources;

namespace Joktan.Logs;

/// <summary>Where a source's logs are.</summary>
public enum LogSourceKind
{
    /// <summary>The Elasticsearch on Settings / Telemetry.</summary>
    JoktanElastic,

    /// <summary>The log files Settings / Telemetry writes.</summary>
    JoktanFiles,

    Elastic,

    Files,
}

/// <summary>Where one app's source code is, for following its stack traces.</summary>
public sealed class AppFolder
{
    /// <summary>The app, as its logs name it; empty for any app the source has no other folder for.</summary>
    public string App { get; set; } = "";

    public string Folder { get; set; } = "";
}

/// <summary>Somewhere to read logs from, with where the code of each app that logs there is.</summary>
public sealed class LogSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonConverter(typeof(JsonStringEnumConverter<LogSourceKind>))]
    public LogSourceKind Kind { get; set; }

    /// <summary>The name given; the built-in sources have names of their own.</summary>
    public string Name { get; set; } = "";

    public string Url { get; set; } = "http://localhost:9200";

    [JsonConverter(typeof(JsonStringEnumConverter<ElasticAuthentication>))]
    public ElasticAuthentication Authentication { get; set; }

    public string Username { get; set; } = "";

    /// <summary>The indices or data streams to search, such as <c>logs-*</c>.</summary>
    public string Index { get; set; } = "logs-*";

    /// <summary>A log file, or a folder of them.</summary>
    public string Path { get; set; } = "";

    /// <summary>Which files in the folder are logs, such as <c>*.log</c>; several are separated by semicolons.</summary>
    public string Filter { get; set; } = "*.log";

    public bool Subfolders { get; set; }

    public List<AppFolder> Folders { get; set; } = [];

    /// <summary>The API key or password, read from the credential store; for Joktan's Elasticsearch, a key that may read. Never written to the file.</summary>
    [JsonIgnore]
    public string Secret { get; set; } = "";

    [JsonIgnore]
    public bool IsBuiltIn => Kind is LogSourceKind.JoktanElastic or LogSourceKind.JoktanFiles;

    [JsonIgnore]
    public bool IsElastic => Kind is LogSourceKind.JoktanElastic or LogSourceKind.Elastic;

    [JsonIgnore]
    public string DisplayName => Kind switch
    {
        LogSourceKind.JoktanElastic => Strings.LogSource_JoktanElastic,
        LogSourceKind.JoktanFiles => Strings.LogSource_JoktanFiles,
        _ => Name,
    };

    /// <summary>The source folder for an app: the one named after it, or else the one for any app.</summary>
    public string? FolderFor(string? app)
    {
        var named = Folders.FirstOrDefault(f => f.App.Trim().Length > 0 && string.Equals(f.App.Trim(), app?.Trim(), StringComparison.OrdinalIgnoreCase));
        var folder = named ?? Folders.FirstOrDefault(f => f.App.Trim().Length == 0);
        return folder is { Folder.Length: > 0 } ? TelemetrySettings.ExpandHome(folder.Folder.Trim()) : null;
    }

    /// <summary>What reads this source, with the address and sign-in of Settings / Telemetry for Joktan's own.</summary>
    public ILogReader Reader(TelemetrySettings telemetry, HttpMessageHandler? handler = null) => Kind switch
    {
        LogSourceKind.JoktanElastic => new ElasticLogReader(telemetry.ElasticUrl, Index,
            Secret.Length > 0 ? $"ApiKey {Secret}" : telemetry.Authorization(), handler),
        LogSourceKind.Elastic => new ElasticLogReader(Url, Index, Authorization(), handler),
        LogSourceKind.JoktanFiles => new FileLogReader(telemetry.EffectiveLogFolder, AppLog.AppName + "-*.log", false),
        _ => new FileLogReader(TelemetrySettings.ExpandHome(Path.Trim()), Filter, Subfolders),
    };

    /// <summary>The value of the Authorization header for another Elasticsearch, if signing in.</summary>
    public string? Authorization() => Authentication switch
    {
        ElasticAuthentication.ApiKey when Secret.Length > 0 => $"ApiKey {Secret}",
        ElasticAuthentication.Password when Secret.Length > 0 =>
            "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Username}:{Secret}")),
        _ => null,
    };

    /// <summary>Whether a text can name indices: no spaces, and none of the characters Elasticsearch refuses.</summary>
    public static bool IsValidIndex(string text)
        => text.Trim().Length > 0 && !text.Trim().Any(c => char.IsWhiteSpace(c) || c is '/' or '\\' or '?' or '"' or '<' or '>' or '|' or '#');

    /// <summary>Whether a text is a whole path, perhaps starting with ~.</summary>
    public static bool IsValidPath(string text)
        => text.Trim().Length > 0 && System.IO.Path.IsPathFullyQualified(TelemetrySettings.ExpandHome(text.Trim()));

    /// <summary>Whether a filter names files and not folders, such as <c>*.log</c> or <c>app-*.txt;*.log</c>.</summary>
    public static bool IsValidFilter(string text)
    {
        var patterns = FileLogReader.Patterns(text);
        return patterns.Count > 0 && patterns.All(p => p.IndexOfAny(['/', '\\']) < 0 && p.IndexOfAny(System.IO.Path.GetInvalidFileNameChars().Except(['*', '?']).ToArray()) < 0);
    }
}

/// <summary>Settings / Log Sources; kept in settings/log-sources.json. API keys and passwords are in the credential store.</summary>
public sealed class LogSourceSettings
{
    public const string JoktanElasticId = "joktan-elasticsearch";
    public const string JoktanFilesId = "joktan-files";

    public List<LogSource> Sources { get; set; } = [];

    private static string FilePath => System.IO.Path.Combine(DataFolder.For("settings"), "log-sources.json");

    public static LogSourceSettings Load()
    {
        LogSourceSettings? settings = null;
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<LogSourceSettings>(File.ReadAllText(FilePath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings start again with Joktan's own sources.
        }

        settings ??= new();
        settings.AddBuiltIn();
        return settings;
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

    public LogSource? Find(string? idOrName)
        => Sources.FirstOrDefault(s => s.Id == idOrName)
           ?? Sources.FirstOrDefault(s => string.Equals(s.DisplayName, idOrName?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads each source's API key or password.</summary>
    public void ReadSecrets(CredentialStore store)
    {
        foreach (var source in Sources)
        {
            if (source.Kind == LogSourceKind.JoktanElastic || (source.Kind == LogSourceKind.Elastic && source.Authentication != ElasticAuthentication.None))
                source.Secret = store.Read(source.Id) ?? "";
        }
    }

    /// <summary>Keeps a source's API key or password, or forgets it when empty. Whether it could be saved.</summary>
    public static bool WriteSecret(CredentialStore store, LogSource source, string secret)
    {
        source.Secret = secret;
        if (secret.Length == 0)
        {
            store.Delete(source.Id);
            return true;
        }

        return store.Write(source.Id, secret);
    }

    // Joktan's own sources are always there, first, and cannot be removed.
    private void AddBuiltIn()
    {
        Sources.RemoveAll(s => s.IsBuiltIn && s.Id is not (JoktanElasticId or JoktanFilesId));
        if (Sources.All(s => s.Id != JoktanFilesId))
            Sources.Insert(0, new LogSource { Id = JoktanFilesId, Kind = LogSourceKind.JoktanFiles, Folders = [new AppFolder { App = AppLog.AppName }] });
        if (Sources.All(s => s.Id != JoktanElasticId))
            Sources.Insert(0, new LogSource { Id = JoktanElasticId, Kind = LogSourceKind.JoktanElastic, Index = "logs-joktan*", Folders = [new AppFolder { App = AppLog.AppName }] });
        foreach (var source in Sources)
            source.Kind = source.Id switch
            {
                JoktanElasticId => LogSourceKind.JoktanElastic,
                JoktanFilesId => LogSourceKind.JoktanFiles,
                _ => source.Kind,
            };
    }
}
