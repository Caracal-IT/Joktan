using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Joktan.Core;
using Microsoft.Extensions.Logging;

namespace Joktan.Mqtt;

/// <summary>One message of a recording, and when it arrived after the recording started.</summary>
/// <param name="Payload">The payload exactly as it arrived, gzipped if it was.</param>
public sealed record RecordedMessage(TimeSpan At, string Topic, byte[] Payload, int Qos, bool Retain);

/// <summary>
/// Messages received from a broker, in the order and at the pace they arrived, to be sent again
/// later. Kept as one JSON file in the recordings folder, with the varying fields to apply on playback.
/// </summary>
public sealed class Recording
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    /// <summary>The name of the file in the recordings folder, without ".json".</summary>
    public string Id { get; init; } = "";

    public string Name { get; set; } = "";

    public DateTime Created { get; init; }

    /// <summary>The client it was recorded through, as it was then called.</summary>
    public string Client { get; init; } = "";

    /// <summary>The topic filters it was recorded with, separated by commas.</summary>
    public string Filters { get; init; } = "";

    /// <summary>How long it was recording, which is at least as long as the time of the last message.</summary>
    public TimeSpan Length { get; init; }

    public IReadOnlyList<RecordedMessage> Messages { get; init; } = [];

    /// <summary>Fields changed in every message that has them, when it is played.</summary>
    public IReadOnlyList<FieldRule> Fields { get; set; } = [];

    public int TopicCount => Messages.Select(m => m.Topic).Distinct().Count();

    /// <summary>The folder recordings are kept in, unless another is chosen.</summary>
    public static string DefaultFolder => Path.Combine(DataFolder.For("mqtt"), "recordings");

    // Remembers the chosen folder; it cannot live in the folder itself.
    private static string LocationPath => Path.Combine(DataFolder.For("settings"), "recordings-location.json");

    /// <summary>The folder chosen for recordings, or the default.</summary>
    public static string ChosenFolder
    {
        get
        {
            try
            {
                if (File.Exists(LocationPath)
                    && JsonNode.Parse(File.ReadAllText(LocationPath))?["folder"]?.GetValue<string>() is { Length: > 0 } folder)
                    return folder;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                // An unreadable choice falls back to the default folder.
            }

            return DefaultFolder;
        }
    }

    public static void SaveChosenFolder(string folder)
    {
        try
        {
            if (SameFolder(folder, DefaultFolder))
            {
                File.Delete(LocationPath);
                return;
            }

            Directory.CreateDirectory(DataFolder.For("settings"));
            File.WriteAllText(LocationPath, new JsonObject { ["folder"] = folder }.ToJsonString(JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A choice that cannot be saved is used until the app closes.
        }
    }

    public static bool SameFolder(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a folder holds any recording files.</summary>
    public static bool HasRecordings(string folder)
        => Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.json").Any();

    /// <summary>Copies every recording file from one folder to another, leaving any of the same name already there.</summary>
    public static void CopyAll(string from, string to)
    {
        Directory.CreateDirectory(to);
        if (!Directory.Exists(from))
            return;
        foreach (var file in Directory.EnumerateFiles(from, "*.json"))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(target))
                File.Copy(file, target);
        }
    }

    /// <summary>
    /// Reads a recording file from anywhere and copies it into the folder under a name not yet used there,
    /// so that it is listed and saved with the others. A file already in the folder, or the same as one there,
    /// is only read.
    /// </summary>
    /// <exception cref="FormatException">The file is not a recording.</exception>
    /// <exception cref="IOException">The file cannot be read, or copied.</exception>
    public static Recording Import(string file, string folder)
    {
        var text = File.ReadAllText(file);
        var recording = Parse(Path.GetFileNameWithoutExtension(file), text);
        if (SameFolder(Path.GetDirectoryName(Path.GetFullPath(file))!, folder))
            return recording;

        Directory.CreateDirectory(folder);
        foreach (var kept in Directory.EnumerateFiles(folder, "*.json"))
        {
            if (File.ReadAllText(kept) == text)
                return Parse(Path.GetFileNameWithoutExtension(kept), text);
        }

        var id = NewId(folder, recording.Created == DateTime.MinValue ? DateTime.Now : recording.Created);
        File.Copy(file, Path.Combine(folder, id + ".json"));
        return Parse(id, File.ReadAllText(Path.Combine(folder, id + ".json")));
    }

    /// <summary>A file name for a recording started at <paramref name="time"/>, not yet used in the folder.</summary>
    public static string NewId(string folder, DateTime time)
    {
        var id = time.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        var unused = id;
        for (var n = 2; File.Exists(Path.Combine(folder, unused + ".json")); n++)
            unused = $"{id}-{n}";
        return unused;
    }

    /// <summary>
    /// The payload to send for a message: as recorded, or with the fields that are in it changed.
    /// A gzipped payload is unzipped to change it and zipped again. One that is not JSON is sent as recorded.
    /// </summary>
    public static byte[] Vary(RecordedMessage message, IReadOnlyList<FieldRule> fields, IDictionary<string, long> counters, DateTimeOffset now)
    {
        if (fields.Count == 0)
            return message.Payload;

        var unzipped = Gzip.TryDecompress(message.Payload);
        JsonNode? body;
        try
        {
            body = JsonNode.Parse(unzipped ?? message.Payload);
        }
        catch (JsonException)
        {
            return message.Payload;
        }

        // A message with none of the fields is sent exactly as it arrived.
        if (body is not (JsonObject or JsonArray) || !fields.Any(f => VaryingFields.TryGetValue(body, f.Path, out _)))
            return message.Payload;

        var varied = Encoding.UTF8.GetBytes(VaryingFields.Apply(body, fields, counters, now).ToJsonString(JsonFormats.Compact));
        return unzipped is null ? varied : Gzip.Compress(varied);
    }

    /// <summary>Every path a field could be given, over the first message of each topic that is JSON.</summary>
    public IReadOnlyList<string> BodyPaths()
    {
        var paths = new List<string>();
        foreach (var first in Messages.DistinctBy(m => m.Topic))
        {
            try
            {
                paths.AddRange(VaryingFields.ListPaths(JsonNode.Parse(Gzip.TryDecompress(first.Payload) ?? first.Payload)));
            }
            catch (JsonException)
            {
                // Not JSON, so it has no fields.
            }
        }

        return paths.Distinct().Order(StringComparer.Ordinal).ToList();
    }

    public void Save(string? folder = null)
    {
        folder ??= DefaultFolder;
        Directory.CreateDirectory(folder);

        var fields = new JsonObject();
        foreach (var field in Fields)
            fields[field.Path] = MessageSettings.FieldToJson(field);

        var messages = new JsonArray();
        foreach (var m in Messages)
        {
            var node = new JsonObject
            {
                ["at"] = Math.Round(m.At.TotalSeconds, 3),
                ["topic"] = m.Topic,
                ["qos"] = m.Qos,
                ["retain"] = m.Retain,
            };

            // Text stays readable in the file; anything else, gzip included, is kept exactly.
            if (!Gzip.LooksZipped(m.Payload) && TryText(m.Payload) is { } text)
                node["text"] = text;
            else
                node["base64"] = Convert.ToBase64String(m.Payload);
            messages.Add(node);
        }

        var root = new JsonObject
        {
            ["name"] = Name,
            ["created"] = Created.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["client"] = Client,
            ["filters"] = Filters,
            ["length"] = Math.Round(Length.TotalSeconds, 3),
            ["fields"] = fields,
            ["messages"] = messages,
        };

        var path = Path.Combine(folder, Id + ".json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(JsonFormats.Indented));
        File.Move(temporary, path, overwrite: true);
    }

    public void Delete(string? folder = null)
    {
        var path = Path.Combine(folder ?? DefaultFolder, Id + ".json");
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Every recording in the folder, newest first. A file that cannot be read is left out.</summary>
    public static List<Recording> LoadAll(string? folder = null)
    {
        folder ??= DefaultFolder;
        if (!Directory.Exists(folder))
            return [];

        var recordings = new List<Recording>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                recordings.Add(Parse(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                AppLog.For("Mqtt").LogWarning("Could not read the recording {File}: {Reason}", file, e.Message);
            }
        }

        return recordings.OrderByDescending(r => r.Created).ToList();
    }

    /// <exception cref="FormatException">The text is not a recording.</exception>
    public static Recording Parse(string id, string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                throw new FormatException("A recording must be a JSON object.");

            var messages = new List<RecordedMessage>();
            foreach (var node in root["messages"] as JsonArray ?? [])
            {
                if (node is not JsonObject m)
                    continue;

                var payload = m["base64"]?.GetValue<string>() is { } base64
                    ? Convert.FromBase64String(base64)
                    : Encoding.UTF8.GetBytes(m["text"]?.GetValue<string>() ?? "");
                messages.Add(new RecordedMessage(
                    TimeSpan.FromSeconds(m["at"]?.GetValue<double>() ?? 0),
                    m["topic"]?.GetValue<string>() ?? throw new FormatException("A message has no topic."),
                    payload,
                    Math.Clamp(m["qos"]?.GetValue<int>() ?? 0, 0, 2),
                    m["retain"]?.GetValue<bool>() ?? false));
            }

            messages.Sort((a, b) => a.At.CompareTo(b.At));
            var length = TimeSpan.FromSeconds(root["length"]?.GetValue<double>() ?? 0);
            return new Recording
            {
                Id = id,
                Name = root["name"]?.GetValue<string>() ?? id,
                Created = DateTime.TryParse(root["created"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) ? created : DateTime.MinValue,
                Client = root["client"]?.GetValue<string>() ?? "",
                Filters = root["filters"]?.GetValue<string>() ?? "",
                Length = messages.Count > 0 && messages[^1].At > length ? messages[^1].At : length,
                Messages = messages,
                Fields = root["fields"] is { } fields ? MessageSettings.ParseFields(fields, "fields") : [],
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new FormatException(e.Message, e);
        }
    }

    private static string? TryText(byte[] payload)
    {
        try
        {
            return StrictUtf8.GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
