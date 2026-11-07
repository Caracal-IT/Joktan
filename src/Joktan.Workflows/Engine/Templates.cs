using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Joktan.Core;

namespace Joktan.Workflows.Engine;

/// <summary>
/// Fills in <c>{{paths}}</c> from a run's data, such as <c>{{input.id}}</c>, <c>{{vars.reply.body.items[0].name}}</c>
/// or <c>{{last}}</c>. Only paths that start with one of <see cref="Roots"/> are filled in; any other
/// <c>{{name}}</c> is left for the API Client's environment.
/// </summary>
public static partial class Templates
{
    public const string Input = "input";
    public const string Vars = "vars";
    public const string Last = "last";
    public const string Run = "run";
    public const string Message = "message";

    /// <summary>The names a path may start with.</summary>
    public static readonly IReadOnlySet<string> Roots = new HashSet<string>(StringComparer.Ordinal) { Input, Vars, Last, Run, Message };

    /// <summary>Text with each path replaced: text values as they are, other values as compact JSON, missing ones as nothing.</summary>
    public static string Render(string text, JsonObject data)
        => Reference().Replace(text, m => IsOurs(m.Groups[1].Value) ? AsText(Resolve(data, m.Groups[1].Value)) : m.Value);

    /// <summary>
    /// The value text stands for: what a path alone names, kept as JSON; otherwise the text with paths filled in,
    /// read as JSON if it is JSON, and as text if not. Empty text is null.
    /// </summary>
    public static JsonNode? Value(string text, JsonObject data)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return null;
        if (Reference().Match(trimmed) is { Success: true } whole && whole.Length == trimmed.Length && IsOurs(whole.Groups[1].Value))
            return Resolve(data, whole.Groups[1].Value)?.DeepClone();
        return Parse(Render(text, data));
    }

    /// <summary>Text read as JSON if it is JSON, and as a text value if not.</summary>
    public static JsonNode? Parse(string text)
    {
        if (text.Trim().Length == 0)
            return JsonValue.Create(text);
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return JsonValue.Create(text);
        }
    }

    /// <summary>A value as text: a text value as it is, anything else as compact JSON, and null as nothing.</summary>
    public static string AsText(JsonNode? node)
        => node switch
        {
            null => "",
            JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
            _ => node.ToJsonString(JsonFormats.Compact),
        };

    /// <summary>The value at a path such as <c>vars.items[2].name</c>, or null when there is none.</summary>
    public static JsonNode? Resolve(JsonNode? root, string path)
    {
        var node = root;
        foreach (var part in Parts(path))
        {
            node = part switch
            {
                int index when node is JsonArray array => index >= 0 && index < array.Count ? array[index] : null,
                string name when node is JsonObject obj => obj.TryGetPropertyValue(name, out var child) ? child : null,
                _ => null,
            };
            if (node is null)
                return null;
        }

        return node;
    }

    /// <summary>Why a path cannot be read, or null.</summary>
    public static string? Check(string path)
    {
        var parts = Parts(path).ToList();
        if (parts.Count == 0 || parts[0] is not string first)
            return "A path starts with a name, such as vars.";
        return Roots.Contains(first) ? null : $"A path starts with one of {string.Join(", ", Roots)}.";
    }

    /// <summary>
    /// Every path into a value, as it would be written, such as <c>{{input.order.id}}</c>: each object's fields, and
    /// a list by its first item. For offering what a sample input holds.
    /// </summary>
    public static IReadOnlyList<string> PathsOf(JsonNode? node, string root = Input, int most = 300)
    {
        var paths = new List<string>();
        Walk(node, root);
        return paths;

        void Walk(JsonNode? value, string path)
        {
            if (paths.Count >= most)
                return;
            paths.Add("{{" + path + "}}");
            switch (value)
            {
                case JsonObject obj:
                    foreach (var (name, child) in obj)
                    {
                        // A name a path cannot hold, such as one with a dot, is left out.
                        if (name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
                            Walk(child, $"{path}.{name}");
                    }

                    break;
                case JsonArray { Count: > 0 } list:
                    Walk(list[0], $"{path}[0]");
                    break;
            }
        }
    }

    /// <summary>The paths a text uses.</summary>
    public static IEnumerable<string> PathsIn(string text)
        => Reference().Matches(text).Select(m => m.Groups[1].Value).Where(IsOurs);

    private static bool IsOurs(string path) => Parts(path).FirstOrDefault() is string first && Roots.Contains(first);

    // Names and indexes: a.b[2].c gives a, b, 2, c.
    private static IEnumerable<object> Parts(string path)
    {
        foreach (var segment in path.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bracket = segment.IndexOf('[');
            var name = bracket < 0 ? segment : segment[..bracket];
            if (name.Length > 0)
                yield return name;
            while (bracket >= 0)
            {
                var close = segment.IndexOf(']', bracket);
                if (close < 0)
                    yield break;
                var inside = segment[(bracket + 1)..close].Trim();
                yield return int.TryParse(inside, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : inside.Trim('"', '\'');
                bracket = segment.IndexOf('[', close);
            }
        }
    }

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}")]
    private static partial Regex Reference();
}

/// <summary>Lengths of time written as 30s, 5m, 2h, 1d or 250ms; a number alone is seconds.</summary>
public static class Durations
{
    public static bool TryParse(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var trimmed = text?.Trim().ToLowerInvariant() ?? "";
        if (trimmed.Length == 0)
            return false;

        var (number, unit) = trimmed.EndsWith("ms", StringComparison.Ordinal) ? (trimmed[..^2], 0.001)
            : trimmed[^1] switch
            {
                's' => (trimmed[..^1], 1.0),
                'm' => (trimmed[..^1], 60.0),
                'h' => (trimmed[..^1], 3600.0),
                'd' => (trimmed[..^1], 86400.0),
                _ => (trimmed, 1.0),
            };
        if (!double.TryParse(number.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0 || double.IsInfinity(value))
            return false;
        var seconds = value * unit;
        if (seconds > TimeSpan.MaxValue.TotalSeconds / 2)
            return false;
        duration = TimeSpan.FromSeconds(seconds);
        return true;
    }

    /// <summary>A length of time as it would be typed: 90 seconds as 1m 30s.</summary>
    public static string Text(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1))
            return $"{(int)duration.TotalMilliseconds}ms";
        var parts = new List<string>();
        if (duration.Days > 0) parts.Add($"{duration.Days}d");
        if (duration.Hours > 0) parts.Add($"{duration.Hours}h");
        if (duration.Minutes > 0) parts.Add($"{duration.Minutes}m");
        if (duration.Seconds > 0 || parts.Count == 0) parts.Add($"{duration.Seconds}s");
        return string.Join(" ", parts);
    }
}
