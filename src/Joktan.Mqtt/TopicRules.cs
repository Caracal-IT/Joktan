using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Joktan.Mqtt;

public static class TopicRules
{
    private const int MaxBytes = 65535;

    /// <summary>Returns why <paramref name="topic"/> cannot be published to, or null if it can.</summary>
    public static string? Validate(string? topic)
    {
        if (string.IsNullOrEmpty(topic))
            return "The topic is empty";
        if (topic.Contains('+') || topic.Contains('#'))
            return "A topic may not contain + or #";
        if (Encoding.UTF8.GetByteCount(topic) > MaxBytes)
            return "The topic is longer than 65,535 bytes";
        return null;
    }

    /// <summary>Returns why <paramref name="filter"/> cannot be subscribed to, or null if it can.</summary>
    public static string? ValidateFilter(string? filter)
    {
        if (string.IsNullOrEmpty(filter))
            return "The filter is empty";
        if (Encoding.UTF8.GetByteCount(filter) > MaxBytes)
            return "The filter is longer than 65,535 bytes";

        var levels = filter.Split('/');
        for (var i = 0; i < levels.Length; i++)
        {
            var level = levels[i];
            if (level.Contains('#') && (level != "#" || i != levels.Length - 1))
                return $"In {filter}, # must be the whole last level";
            if (level.Contains('+') && level != "+")
                return $"In {filter}, + must be a whole level";
        }

        return null;
    }

    /// <summary>
    /// Whether a filter matches a topic, or every topic another filter matches. As in MQTT, a filter
    /// starting with a wildcard does not match a topic starting with <c>$</c>.
    /// </summary>
    public static bool Matches(string filter, string topic)
    {
        if (filter == topic)
            return true;
        if (filter.Length == 0)
            return false;
        if (topic.StartsWith('$') && filter[0] is '+' or '#')
            return false;
        if (filter == "#")
            return true;

        var filterLevels = filter.Split('/');
        var topicLevels = topic.Split('/');
        for (var i = 0; i < filterLevels.Length; i++)
        {
            if (filterLevels[i] == "#")
                return true;
            if (i >= topicLevels.Length || topicLevels[i] == "#")
                return false;
            if (filterLevels[i] != "+" && filterLevels[i] != topicLevels[i])
                return false;
        }

        return filterLevels.Length == topicLevels.Length;
    }

    /// <summary>The filters, without those another of them already matches.</summary>
    public static string[] WithoutCovered(IEnumerable<string> filters)
    {
        var distinct = filters.Distinct().ToArray();
        return distinct.Where(f => !distinct.Any(other => other != f && Matches(other, f))).ToArray();
    }
}
