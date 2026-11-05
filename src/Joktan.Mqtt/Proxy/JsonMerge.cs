using System.Text.Json.Nodes;

namespace Joktan.Mqtt.Proxy;

/// <summary>Merges one JSON value into another, as the proxy does with an intercepted message.</summary>
public static class JsonMerge
{
    /// <summary>
    /// A copy of <paramref name="target"/> with <paramref name="overlay"/> merged in: objects are
    /// merged property by property, at every depth; anything else in the overlay, arrays included,
    /// replaces what was there.
    /// </summary>
    public static JsonNode? Merge(JsonNode? target, JsonNode? overlay)
    {
        if (target is not JsonObject into || overlay is not JsonObject from)
            return overlay?.DeepClone();

        var result = (JsonObject)into.DeepClone();
        foreach (var (key, value) in from)
            result[key] = result[key] is JsonObject && value is JsonObject ? Merge(result[key], value) : value?.DeepClone();
        return result;
    }
}
