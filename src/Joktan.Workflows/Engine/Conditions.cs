using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Engine;

/// <summary>Compares two values as a condition step does: as numbers when both are numbers, otherwise as JSON or text.</summary>
public static class Conditions
{
    public static bool Compare(JsonNode? left, CompareOperator op, JsonNode? right) => op switch
    {
        CompareOperator.Equals => AreEqual(left, right),
        CompareOperator.NotEquals => !AreEqual(left, right),
        CompareOperator.GreaterThan => Order(left, right) > 0,
        CompareOperator.GreaterOrEqual => Order(left, right) >= 0,
        CompareOperator.LessThan => Order(left, right) < 0,
        CompareOperator.LessOrEqual => Order(left, right) <= 0,
        CompareOperator.Contains => Contains(left, right),
        CompareOperator.StartsWith => Templates.AsText(left).StartsWith(Templates.AsText(right), StringComparison.Ordinal),
        CompareOperator.IsEmpty => IsEmpty(left),
        CompareOperator.IsNotEmpty => !IsEmpty(left),
        _ => throw new InvalidOperationException($"{op} is worked out by running C#."),
    };

    /// <summary>Whether the operator needs a right side.</summary>
    public static bool TakesRight(CompareOperator op) => op is not (CompareOperator.IsEmpty or CompareOperator.IsNotEmpty or CompareOperator.CSharp);

    /// <summary>Whether a value is true, as a condition written in C# or a JSON value can be.</summary>
    public static bool IsTrue(JsonNode? node) => node switch
    {
        null => false,
        JsonValue value when value.GetValueKind() == JsonValueKind.True => true,
        JsonValue value when value.GetValueKind() == JsonValueKind.False => false,
        JsonValue value when value.GetValueKind() == JsonValueKind.String => bool.TryParse(value.GetValue<string>(), out var flag) ? flag : value.GetValue<string>().Length > 0,
        JsonValue when Number(node) is { } number => number != 0,
        _ => true,
    };

    private static bool AreEqual(JsonNode? left, JsonNode? right)
    {
        if (Number(left) is { } a && Number(right) is { } b)
            return a == b;
        if (left is JsonObject or JsonArray || right is JsonObject or JsonArray)
            return JsonNode.DeepEquals(left, right);
        return Templates.AsText(left) == Templates.AsText(right);
    }

    private static int Order(JsonNode? left, JsonNode? right)
    {
        if (Number(left) is { } a && Number(right) is { } b)
            return a.CompareTo(b);
        return string.CompareOrdinal(Templates.AsText(left), Templates.AsText(right));
    }

    private static bool Contains(JsonNode? left, JsonNode? right) => left switch
    {
        JsonArray array => array.Any(item => AreEqual(item, right)),
        JsonObject obj => obj.ContainsKey(Templates.AsText(right)),
        _ => Templates.AsText(left).Contains(Templates.AsText(right), StringComparison.Ordinal),
    };

    private static bool IsEmpty(JsonNode? node) => node switch
    {
        null => true,
        JsonArray array => array.Count == 0,
        JsonObject obj => obj.Count == 0,
        JsonValue value when value.GetValueKind() == JsonValueKind.Null => true,
        _ => Templates.AsText(node).Length == 0,
    };

    // A number, or text that is one, so that what a person typed compares with what a message held.
    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        return value.GetValueKind() switch
        {
            // Read from its JSON, since a value made from an int will not give itself as a double.
            JsonValueKind.Number => double.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture),
            JsonValueKind.String when double.TryParse(value.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
