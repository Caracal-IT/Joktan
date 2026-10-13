using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Joktan.Mqtt;

/// <summary>
/// Replaces fields of a JSON body with generated values. A field is addressed by its path from
/// the top of the body, for example "ts", "sensor.value" or "readings[0].ts".
/// </summary>
public static class VaryingFields
{
    /// <summary>Returns the first rule whose field is not in the body, or null.</summary>
    public static string? FindMissing(JsonNode body, IEnumerable<FieldRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!TryLocate(body, rule.Path, out _, out _, out _))
                return rule.Path;
        }

        return null;
    }

    /// <summary>
    /// Returns a copy of the body with every rule applied. Counters read and advance their
    /// position in <paramref name="counters"/>, keyed by field path.
    /// </summary>
    public static JsonNode Apply(
        JsonNode body, IReadOnlyList<FieldRule> rules, IDictionary<string, long> counters, DateTimeOffset now)
    {
        var copy = body.DeepClone();
        foreach (var rule in rules)
        {
            if (!TryLocate(copy, rule.Path, out var parent, out var key, out var index))
                continue;

            var value = Generate(rule, counters, now);
            if (parent is JsonObject o)
                o[key!] = value;
            else
                ((JsonArray)parent!)[index] = value;
        }

        return copy;
    }

    /// <summary>Lists the path of every value in the body that a rule could address.</summary>
    public static IReadOnlyList<string> ListPaths(JsonNode? body)
    {
        var paths = new List<string>();
        Walk(body, "");
        return paths;

        void Walk(JsonNode? node, string path)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (name, child) in o)
                        Walk(child, path.Length == 0 ? name : $"{path}.{name}");
                    break;
                case JsonArray a:
                    for (var i = 0; i < a.Count; i++)
                        Walk(a[i], $"{path}[{i}]");
                    break;
                default:
                    if (path.Length > 0)
                        paths.Add(path);
                    break;
            }
        }
    }

    private static JsonNode? Generate(FieldRule rule, IDictionary<string, long> counters, DateTimeOffset now)
    {
        switch (rule.Type)
        {
            case FieldType.UnixSeconds:
                return now.ToUnixTimeSeconds();

            case FieldType.UnixMilliseconds:
                return now.ToUnixTimeMilliseconds();

            case FieldType.IsoTimestamp:
                return now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

            case FieldType.Guid:
                return Guid.NewGuid().ToString();

            case FieldType.Counter:
                var current = counters.TryGetValue(rule.Path, out var stored) ? stored : rule.Start;
                counters[rule.Path] = current + rule.Step;
                return current;

            case FieldType.RandomInteger:
                var min = (long)Math.Ceiling(rule.Min);
                var max = (long)Math.Floor(rule.Max);
                return max < min ? min : Random.Shared.NextInt64(min, max + 1);

            case FieldType.RandomNumber:
                var number = rule.Min + Random.Shared.NextDouble() * (rule.Max - rule.Min);
                return Math.Round(number, Math.Clamp(rule.Decimals, 0, 15));

            case FieldType.OneOf:
                return rule.Values.Count == 0
                    ? null
                    : rule.Values[Random.Shared.Next(rule.Values.Count)]?.DeepClone();

            default:
                return null;
        }
    }

    // Finds the container that holds the addressed value, and the value's name or index in it.
    private static bool TryLocate(JsonNode root, string path, out JsonNode? parent, out string? key, out int index)
    {
        parent = null;
        key = null;
        index = -1;

        JsonNode? current = root;
        foreach (var segment in path.Split('.'))
        {
            var bracket = segment.IndexOf('[');
            var name = bracket < 0 ? segment : segment[..bracket];
            if (name.Length == 0)
                return false;

            if (current is not JsonObject o || !o.ContainsKey(name))
                return false;
            parent = o;
            key = name;
            index = -1;
            current = o[name];

            var rest = bracket < 0 ? "" : segment[bracket..];
            while (rest.Length > 0)
            {
                var close = rest.IndexOf(']');
                if (rest[0] != '[' || close < 0
                    || !int.TryParse(rest[1..close], NumberStyles.None, CultureInfo.InvariantCulture, out var i))
                    return false;
                if (current is not JsonArray a || i >= a.Count)
                    return false;

                parent = a;
                key = null;
                index = i;
                current = a[i];
                rest = rest[(close + 1)..];
            }
        }

        return parent is not null;
    }
}
