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
/// later. Kept as a folder of its own in the recordings folder: <see cref="InfoFile"/>, with the varying fields
/// to apply on playback, and one file per message with its time, topic and payload.
/// </summary>
public sealed class Recording
{
    /// <summary>The file in a recording's folder that holds its name, filters, length and fields.</summary>
    public const string InfoFile = "recording.json";

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    /// <summary>The name of its folder in the recordings folder.</summary>
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

    /// <summary>Whether a folder holds any recordings, including ones kept as a single file by older versions.</summary>
    public static bool HasRecordings(string folder)
        => Directory.Exists(folder)
           && (Directory.EnumerateDirectories(folder).Any(IsRecordingFolder) || Directory.EnumerateFiles(folder, "*.json").Any());

    /// <summary>Whether a folder is one recording: it has an info file next to its messages.</summary>
    public static bool IsRecordingFolder(string directory) => File.Exists(Path.Combine(directory, InfoFile));

    /// <summary>Copies every recording from one folder to another, leaving any of the same name already there.</summary>
    public static void CopyAll(string from, string to)
    {
        Directory.CreateDirectory(to);
        if (!Directory.Exists(from))
            return;
        foreach (var directory in Directory.EnumerateDirectories(from).Where(IsRecordingFolder))
        {
            var target = Path.Combine(to, Path.GetFileName(directory));
            if (Directory.Exists(target))
                continue;
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*.json"))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(target))
                File.Copy(file, target);
        }
    }

    /// <summary>
    /// Reads a recording from anywhere, given its info file or an older single-file recording, and copies it
    /// into the folder under a name not yet used there, so that it is listed and saved with the others.
    /// A recording already in the folder, or the same as one there, is only read.
    /// </summary>
    /// <exception cref="FormatException">The file is not a recording.</exception>
    /// <exception cref="IOException">The file cannot be read, or copied.</exception>
    public static Recording Import(string file, string folder)
    {
        file = Path.GetFullPath(file);
        var isInfo = Path.GetFileName(file) == InfoFile;
        var directory = Path.GetDirectoryName(file)!;
        if (isInfo && SameFolder(Path.GetDirectoryName(directory)!, folder))
            return Load(directory);
        if (!isInfo && SameFolder(directory, folder))
            return Migrate(file, folder);

        var recording = isInfo ? Load(directory) : Parse(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
        Directory.CreateDirectory(folder);
        var info = recording.InfoJson();
        foreach (var kept in Directory.EnumerateDirectories(folder).Where(IsRecordingFolder))
        {
            if (File.ReadAllText(Path.Combine(kept, InfoFile)) == info && Load(kept) is var other && other.SameMessages(recording))
                return other;
        }

        var copy = recording.WithId(NewId(folder, recording.Created == DateTime.MinValue ? DateTime.Now : recording.Created));
        copy.Save(folder);
        return copy;
    }

    /// <summary>A name for a recording started at <paramref name="time"/>, not yet used in the folder.</summary>
    public static string NewId(string folder, DateTime time)
    {
        var id = time.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        var unused = id;
        for (var n = 2; Directory.Exists(Path.Combine(folder, unused)) || File.Exists(Path.Combine(folder, unused + ".json")); n++)
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

    /// <summary>
    /// Writes the recording to its own folder: the info file, and one file per message. The messages never change
    /// once recorded, so they are written only the first time; after that only the info file is written again.
    /// </summary>
    public void Save(string? folder = null)
    {
        var directory = Path.Combine(folder ?? DefaultFolder, Id);
        var info = Path.Combine(directory, InfoFile);

        // The info file goes last, so a folder left half written is not taken for a recording.
        if (!File.Exists(info))
        {
            Directory.CreateDirectory(directory);
            foreach (var stale in Directory.EnumerateFiles(directory, "*.json"))
                File.Delete(stale);
            for (var i = 0; i < Messages.Count; i++)
                File.WriteAllText(Path.Combine(directory, MessageFile(i)), MessageToJson(Messages[i]).ToJsonString(JsonFormats.Indented));
        }

        var temporary = info + ".tmp";
        File.WriteAllText(temporary, InfoJson());
        File.Move(temporary, info, overwrite: true);
    }

    public void Delete(string? folder = null)
    {
        var directory = Path.Combine(folder ?? DefaultFolder, Id);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// Every recording in the folder, newest first. One kept as a single file by an older version is first
    /// moved to a folder of its own. One that cannot be read is left out.
    /// </summary>
    public static List<Recording> LoadAll(string? folder = null)
    {
        folder ??= DefaultFolder;
        if (!Directory.Exists(folder))
            return [];

        foreach (var file in Directory.EnumerateFiles(folder, "*.json").ToList())
        {
            try
            {
                Migrate(file, folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                AppLog.For("Mqtt").LogWarning("Could not read the recording {File}: {Reason}", file, e.Message);
            }
        }

        var recordings = new List<Recording>();
        foreach (var directory in Directory.EnumerateDirectories(folder).Where(IsRecordingFolder))
        {
            try
            {
                recordings.Add(Load(directory));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                AppLog.For("Mqtt").LogWarning("Could not read the recording {Folder}: {Reason}", directory, e.Message);
            }
        }

        return recordings.OrderByDescending(r => r.Created).ToList();
    }

    /// <summary>Reads the recording kept in a folder of its own.</summary>
    /// <exception cref="FormatException">The folder does not hold a recording.</exception>
    public static Recording Load(string directory)
    {
        directory = Path.TrimEndingDirectorySeparator(directory);
        try
        {
            if (JsonNode.Parse(File.ReadAllText(Path.Combine(directory, InfoFile))) is not JsonObject root)
                throw new FormatException("A recording's info file must be a JSON object.");

            var messages = Directory.EnumerateFiles(directory, "*.json")
                .Where(f => Path.GetFileName(f) != InfoFile)
                .Order(StringComparer.Ordinal)
                .Select(f => JsonNode.Parse(File.ReadAllText(f)) as JsonObject ?? throw new FormatException($"{Path.GetFileName(f)} is not a message."))
                .Select(ParseMessage);
            return FromJson(Path.GetFileName(directory), root, messages);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new FormatException(e.Message, e);
        }
    }

    /// <summary>Reads a recording kept as a single file, as older versions did.</summary>
    /// <exception cref="FormatException">The text is not a recording.</exception>
    public static Recording Parse(string id, string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                throw new FormatException("A recording must be a JSON object.");

            var messages = (root["messages"] as JsonArray ?? []).OfType<JsonObject>().Select(ParseMessage);
            return FromJson(id, root, messages);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new FormatException(e.Message, e);
        }
    }

    // Moves a single-file recording in the folder to a folder of its own.
    private static Recording Migrate(string file, string folder)
    {
        var id = Path.GetFileNameWithoutExtension(file);
        var recording = Parse(id, File.ReadAllText(file));
        if (Directory.Exists(Path.Combine(folder, id)))
            recording = recording.WithId(NewId(folder, recording.Created == DateTime.MinValue ? DateTime.Now : recording.Created));
        recording.Save(folder);
        File.Delete(file);
        return recording;
    }

    private static Recording FromJson(string id, JsonObject root, IEnumerable<RecordedMessage> messages)
    {
        var sorted = messages.OrderBy(m => m.At).ToList();
        var length = TimeSpan.FromSeconds(root["length"]?.GetValue<double>() ?? 0);
        return new Recording
        {
            Id = id,
            Name = root["name"]?.GetValue<string>() ?? id,
            Created = DateTime.TryParse(root["created"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) ? created : DateTime.MinValue,
            Client = root["client"]?.GetValue<string>() ?? "",
            Filters = root["filters"]?.GetValue<string>() ?? "",
            Length = sorted.Count > 0 && sorted[^1].At > length ? sorted[^1].At : length,
            Messages = sorted,
            Fields = root["fields"] is { } fields ? MessageSettings.ParseFields(fields, "fields") : [],
        };
    }

    private static RecordedMessage ParseMessage(JsonObject m)
    {
        var payload = m["base64"]?.GetValue<string>() is { } base64
            ? Convert.FromBase64String(base64)
            : Encoding.UTF8.GetBytes(m["text"]?.GetValue<string>() ?? "");
        return new RecordedMessage(
            TimeSpan.FromSeconds(m["at"]?.GetValue<double>() ?? 0),
            m["topic"]?.GetValue<string>() ?? throw new FormatException("A message has no topic."),
            payload,
            Math.Clamp(m["qos"]?.GetValue<int>() ?? 0, 0, 2),
            m["retain"]?.GetValue<bool>() ?? false);
    }

    /// <summary>The name of the file of the message at <paramref name="index"/>, numbered from 1 so they list in order.</summary>
    public static string MessageFile(int index) => (index + 1).ToString("D6", CultureInfo.InvariantCulture) + ".json";

    private static JsonObject MessageToJson(RecordedMessage m)
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
        return node;
    }

    private string InfoJson()
    {
        var fields = new JsonObject();
        foreach (var field in Fields)
            fields[field.Path] = MessageSettings.FieldToJson(field);

        return new JsonObject
        {
            ["name"] = Name,
            ["created"] = Created.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["client"] = Client,
            ["filters"] = Filters,
            ["length"] = Math.Round(Length.TotalSeconds, 3),
            ["messages"] = Messages.Count,
            ["fields"] = fields,
        }.ToJsonString(JsonFormats.Indented);
    }

    private bool SameMessages(Recording other)
        => Messages.Count == other.Messages.Count
           && Messages.Zip(other.Messages).All(p => MessageToJson(p.First).ToJsonString() == MessageToJson(p.Second).ToJsonString());

    private Recording WithId(string id) => new()
    {
        Id = id,
        Name = Name,
        Created = Created,
        Client = Client,
        Filters = Filters,
        Length = Length,
        Messages = Messages,
        Fields = Fields,
    };

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
