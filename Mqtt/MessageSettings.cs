using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Joktan.Mqtt;

public enum FieldType
{
    UnixSeconds,
    UnixMilliseconds,
    IsoTimestamp,
    Guid,
    Counter,
    RandomInteger,
    RandomNumber,
    OneOf,
}

/// <summary>
/// A field of the message body that gets a new value on every send.
/// </summary>
public sealed record FieldRule(string Path, FieldType Type)
{
    public long Start { get; init; } = 1;

    public long Step { get; init; } = 1;

    public double Min { get; init; }

    public double Max { get; init; }

    public int Decimals { get; init; } = 2;

    public IReadOnlyList<JsonNode?> Values { get; init; } = [];
}

/// <summary>
/// How a folder message is sent. Stored beside the message as "&lt;name&gt;.send.json".
/// </summary>
public sealed record MessageSettings
{
    public const string FileSuffix = ".send.json";

    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss";

    private static readonly string[] DayNames = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    public static readonly MessageSettings Default = new();

    public bool Enabled { get; init; } = true;

    public int Qos { get; init; }

    public bool Retain { get; init; }

    public MessageSchedule Schedule { get; init; } = MessageSchedule.Manual;

    public IReadOnlyList<FieldRule> Fields { get; init; } = [];

    /// <summary>The settings file that belongs to a message file.</summary>
    public static string PathFor(string messagePath)
        => messagePath[..^".json".Length] + FileSuffix;

    /// <exception cref="FormatException">The text is not a valid settings file.</exception>
    public static MessageSettings Parse(string json)
    {
        try
        {
            var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
            if (JsonNode.Parse(json, documentOptions: options) is not JsonObject root)
                throw new FormatException("it must be a JSON object");

            var qos = Int(root, "qos") ?? 0;
            if (qos is < 0 or > 2)
                throw new FormatException("\"qos\" must be 0, 1 or 2");

            return new MessageSettings
            {
                Enabled = Bool(root, "enabled") ?? true,
                Qos = qos,
                Retain = Bool(root, "retain") ?? false,
                Schedule = root["schedule"] is { } schedule ? ParseSchedule(schedule) : MessageSchedule.Manual,
                Fields = root["fields"] is { } fields ? ParseFields(fields) : [],
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new FormatException("it is not valid JSON or a value has the wrong type", e);
        }
    }

    public string ToJson()
    {
        var root = new JsonObject
        {
            ["enabled"] = Enabled,
            ["qos"] = Qos,
            ["retain"] = Retain,
            ["schedule"] = ScheduleToJson(Schedule),
        };

        var fields = new JsonObject();
        foreach (var field in Fields)
            fields[field.Path] = FieldToJson(field);
        root["fields"] = fields;

        return root.ToJsonString(JsonFormats.Indented);
    }

    public static JsonObject ScheduleToJson(MessageSchedule s)
    {
        var node = new JsonObject { ["type"] = CamelCase(s.Type.ToString()) };
        switch (s.Type)
        {
            case ScheduleType.Once:
                node["at"] = s.At?.ToString(DateFormat, CultureInfo.InvariantCulture);
                break;

            case ScheduleType.Interval:
                node["every"] = s.Every;
                node["unit"] = CamelCase(s.Unit.ToString());
                if (s.Start is { } start)
                    node["start"] = start.ToString(DateFormat, CultureInfo.InvariantCulture);
                if (s.End is { } end)
                    node["end"] = end.ToString(DateFormat, CultureInfo.InvariantCulture);
                if (s.MaxSends is { } max)
                    node["maxSends"] = max;
                break;

            case ScheduleType.Daily:
                node["times"] = new JsonArray(s.Times.Order()
                    .Select(t => (JsonNode)t.ToString("HH:mm", CultureInfo.InvariantCulture)).ToArray());
                node["days"] = new JsonArray(s.Days.Select(d => (JsonNode)DayNames[(int)d]).ToArray());
                break;
        }

        return node;
    }

    private static MessageSchedule ParseSchedule(JsonNode node)
    {
        if (node is not JsonObject o)
            throw new FormatException("\"schedule\" must be an object");

        var type = Enum(o, "type", ScheduleType.Manual);
        switch (type)
        {
            case ScheduleType.Once:
                return new MessageSchedule
                {
                    Type = type,
                    At = Date(o, "at") ?? throw new FormatException("a \"once\" schedule needs \"at\""),
                };

            case ScheduleType.Interval:
                var every = Int(o, "every") ?? throw new FormatException("an \"interval\" schedule needs \"every\"");
                if (every < 1)
                    throw new FormatException("\"every\" must be 1 or more");
                var max = Int(o, "maxSends");
                if (max < 1)
                    throw new FormatException("\"maxSends\" must be 1 or more");
                return new MessageSchedule
                {
                    Type = type,
                    Every = every,
                    Unit = Enum(o, "unit", IntervalUnit.Seconds),
                    Start = Date(o, "start"),
                    End = Date(o, "end"),
                    MaxSends = max,
                };

            case ScheduleType.Daily:
                var times = (o["times"] as JsonArray ?? [])
                    .Select(t => TimeOnly.TryParseExact(t?.GetValue<string>(), ["HH:mm", "H:mm", "HH:mm:ss"],
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                        ? time
                        : throw new FormatException($"\"{t}\" is not a time of day such as 08:30"))
                    .ToArray();
                var days = (o["days"] as JsonArray ?? [])
                    .Select(d => Array.IndexOf(DayNames, d?.GetValue<string>().ToLowerInvariant()) is >= 0 and var i
                        ? (DayOfWeek)i
                        : throw new FormatException($"\"{d}\" is not a day such as mon"))
                    .Distinct()
                    .ToArray();
                if (times.Length == 0)
                    throw new FormatException("a \"daily\" schedule needs at least one time");
                if (days.Length == 0)
                    throw new FormatException("a \"daily\" schedule needs at least one day");
                return new MessageSchedule { Type = type, Times = times, Days = days };

            default:
                return MessageSchedule.Manual;
        }
    }

    private static List<FieldRule> ParseFields(JsonNode node)
    {
        if (node is not JsonObject o)
            throw new FormatException("\"fields\" must be an object");

        var rules = new List<FieldRule>();
        foreach (var (path, value) in o)
        {
            if (value is not JsonObject f)
                throw new FormatException($"field \"{path}\" must be an object with a \"type\"");
            if (f["type"] is null)
                throw new FormatException($"field \"{path}\" needs a \"type\"");

            var rule = new FieldRule(path, Enum(f, "type", FieldType.Guid))
            {
                Start = Long(f, "start") ?? 1,
                Step = Long(f, "step") ?? 1,
                Min = Double(f, "min") ?? 0,
                Max = Double(f, "max") ?? 0,
                Decimals = Int(f, "decimals") ?? 2,
                Values = (f["values"] as JsonArray)?.Select(v => v?.DeepClone()).ToArray() ?? [],
            };

            if (CheckRule(rule) is { } problem)
                throw new FormatException($"field \"{path}\": {problem}");
            rules.Add(rule);
        }

        return rules;
    }

    /// <summary>Returns what is wrong with a rule's settings, or null.</summary>
    public static string? CheckRule(FieldRule rule) => rule.Type switch
    {
        FieldType.RandomInteger or FieldType.RandomNumber when rule.Min > rule.Max
            => "\"min\" must not be greater than \"max\"",
        FieldType.RandomNumber when rule.Decimals is < 0 or > 15
            => "\"decimals\" must be from 0 to 15",
        FieldType.OneOf when rule.Values.Count == 0
            => "\"values\" must list at least one value",
        _ => null,
    };

    private static JsonObject FieldToJson(FieldRule f)
    {
        var node = new JsonObject { ["type"] = CamelCase(f.Type.ToString()) };
        switch (f.Type)
        {
            case FieldType.Counter:
                node["start"] = f.Start;
                node["step"] = f.Step;
                break;
            case FieldType.RandomInteger:
                node["min"] = (long)f.Min;
                node["max"] = (long)f.Max;
                break;
            case FieldType.RandomNumber:
                node["min"] = f.Min;
                node["max"] = f.Max;
                node["decimals"] = f.Decimals;
                break;
            case FieldType.OneOf:
                node["values"] = new JsonArray(f.Values.Select(v => v?.DeepClone()).ToArray());
                break;
        }

        return node;
    }

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static bool? Bool(JsonObject o, string key) => o[key]?.GetValue<bool>();

    private static int? Int(JsonObject o, string key) => o[key]?.GetValue<int>();

    private static long? Long(JsonObject o, string key) => o[key]?.GetValue<long>();

    private static double? Double(JsonObject o, string key) => o[key]?.GetValue<double>();

    private static T Enum<T>(JsonObject o, string key, T fallback) where T : struct, Enum
    {
        if (o[key] is not { } node)
            return fallback;

        var text = node.GetValue<string>();
        return System.Enum.TryParse<T>(text, ignoreCase: true, out var value) && System.Enum.IsDefined(value)
            ? value
            : throw new FormatException($"\"{text}\" is not a valid \"{key}\"");
    }

    private static DateTime? Date(JsonObject o, string key)
    {
        if (o[key] is not { } node)
            return null;

        var text = node.GetValue<string>();
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
            ? value
            : throw new FormatException($"\"{text}\" is not a date and time such as 2026-11-02T08:30:00");
    }
}
