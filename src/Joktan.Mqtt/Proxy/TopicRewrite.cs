using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.Proxy;

/// <summary>
/// The topics of intercepted messages. A folder message's topic may have fields, such as
/// <c>plant/{line}/status</c>, each standing for one level. An intercepted message may be sent to
/// the broker on another topic, filled in as the payload is: a field of its own topic takes the
/// level it stood for, and any other takes the value at that path in the payload sent, so
/// <c>lab/{line}/{id}</c> takes <c>id</c> from the body, after its varying fields are applied.
/// </summary>
public static partial class TopicRewrite
{
    /// <summary>Whether a topic is one of a template's, its fields standing for any one level.</summary>
    public static bool MatchesTemplate(string template, string topic)
    {
        var parts = template.Split('/');
        var levels = topic.Split('/');
        return parts.Length == levels.Length && parts.Zip(levels).All(p => p.First == p.Second || IsField(p.First));
    }

    /// <summary>
    /// The topic <paramref name="to"/> makes for <paramref name="topic"/>, one of <paramref name="from"/>'s:
    /// each field in <paramref name="to"/> takes the level it stood for in the topic, or else the
    /// value at that path in <paramref name="payload"/>. Null when a field has neither.
    /// </summary>
    public static string? Rewrite(string from, string to, string topic, JsonNode? payload = null)
    {
        var values = new Dictionary<string, string>();
        foreach (var (part, level) in from.Split('/').Zip(topic.Split('/')))
        {
            if (IsField(part))
                values[part[1..^1]] = level;
        }

        var missing = false;
        var result = Field().Replace(to, m =>
        {
            var name = m.Groups[1].Value;
            if (values.TryGetValue(name, out var value))
                return value;
            if (VaryingFields.TryGetValue(payload, name, out var node))
                return Text(node);
            missing = true;
            return m.Value;
        });
        return missing ? null : result;
    }

    /// <summary>The fields of a template, in order.</summary>
    public static IReadOnlyList<string> Fields(string template) => Field().Matches(template).Select(m => m.Groups[1].Value).ToList();

    /// <summary>
    /// Whether a broker's topic of <paramref name="brokerTopic"/> can be turned back into the
    /// message's own: only when every field of the message's topic is in it.
    /// </summary>
    public static bool CanReverse(string messageTopic, string brokerTopic)
    {
        var fields = Fields(brokerTopic).ToHashSet();
        return Fields(messageTopic).All(fields.Contains);
    }

    // A string goes in without its quotes; anything else as its JSON text.
    private static string Text(JsonNode? value)
        => value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value?.ToJsonString() ?? "null";

    /// <summary>
    /// What is wrong with the topic an intercepted message is sent to the broker on, or null.
    /// Empty keeps the message's own. It has no wildcards; its fields are filled in as it is sent.
    /// </summary>
    public static string? Check(string brokerTopic)
    {
        brokerTopic = brokerTopic.Trim();
        if (brokerTopic.Length == 0)
            return null;
        if (brokerTopic.Contains('+') || brokerTopic.Contains('#'))
            return Strings.TopicRewrite_NoWildcards;
        return TopicRules.Validate(Field().Replace(brokerTopic, "x"));
    }

    /// <summary>A topic filter for every topic of a template.</summary>
    public static string AsFilter(string template) => Field().Replace(template, "+");

    /// <summary>Whether a device's filter can take in any topic of a template.</summary>
    public static bool Overlaps(string filter, string template)
    {
        var levels = filter.Split('/');
        var parts = template.Split('/');
        for (var i = 0; i < levels.Length; i++)
        {
            if (levels[i] == "#")
                return true;
            if (i >= parts.Length || (levels[i] != "+" && !IsField(parts[i]) && levels[i] != parts[i]))
                return false;
        }

        return levels.Length == parts.Length;
    }

    private static bool IsField(string part) => part.Length > 2 && part[0] == '{' && part[^1] == '}';

    [GeneratedRegex(@"\{([^{}/]+)\}")]
    private static partial Regex Field();
}
