using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Joktan.Mqtt;

/// <summary>The numbers at one place in a JSON payload, in the order they are written.</summary>
/// <param name="Path">As for varying fields: <c>sensor.value</c>, <c>readings[*].value</c>; empty for a bare number.</param>
public sealed record NumericField(string Path, IReadOnlyList<double> Values);

/// <summary>
/// Finds the numbers in a JSON payload. An array's numbers are found twice: together under a path
/// with <c>[*]</c>, where they follow one another as they would over time, and one by one under
/// paths such as <c>readings[0].value</c>. Those with <c>[*]</c> and the plain values come first.
/// </summary>
public static class JsonNumbers
{
    // Beyond these a payload is not worth holding up the UI thread for, or keeping in memory.
    private const int MaxPayloadBytes = 1024 * 1024;
    private const int MaxFields = 500;
    private const int MaxItemsByPosition = 20;
    private const int MaxValuesPerField = 60;

    public static IReadOnlyList<NumericField> Find(ReadOnlySpan<byte> json)
    {
        if (json.Length is 0 or > MaxPayloadBytes)
            return [];

        try
        {
            var reader = new Utf8JsonReader(json);
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                return [];

            var found = new Dictionary<string, List<double>>(StringComparer.Ordinal);
            var order = new List<string>();
            Walk(document.RootElement, "", "", found, order);

            var fields = new List<NumericField>(order.Count);
            foreach (var path in order)
            {
                if (!IsOneItem(path))
                    fields.Add(new NumericField(path, found[path]));
            }

            foreach (var path in order)
            {
                if (IsOneItem(path))
                    fields.Add(new NumericField(path, found[path]));
            }

            return fields;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <param name="path">The path with <c>[*]</c> for every array.</param>
    /// <param name="position">The path with each array's position; null past the positions that are named.</param>
    private static void Walk(JsonElement element, string path, string? position, Dictionary<string, List<double>> found, List<string> order)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, Join(path, property.Name), position is null ? null : Join(position, property.Name), found, order);
                break;

            case JsonValueKind.Array:
                var items = path + "[*]";
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, items, position is not null && index < MaxItemsByPosition ? $"{position}[{index}]" : null, found, order);
                    index++;
                }

                break;

            case JsonValueKind.Number when element.TryGetDouble(out var number) && double.IsFinite(number):
                Add(path, number, found, order);
                if (position is not null && position != path)
                    Add(position, number, found, order);
                break;
        }
    }

    private static void Add(string path, double number, Dictionary<string, List<double>> found, List<string> order)
    {
        if (!found.TryGetValue(path, out var values))
        {
            if (order.Count >= MaxFields)
                return;

            found[path] = values = [];
            order.Add(path);
        }

        values.Add(number);
        if (values.Count > MaxValuesPerField)
            values.RemoveAt(0);
    }

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    /// <summary>Whether a path names one item of an array, rather than a value or a whole array.</summary>
    public static bool IsOneItem(string path)
        => path.Contains('[') && !path.Contains("[*]", StringComparison.Ordinal);
}
