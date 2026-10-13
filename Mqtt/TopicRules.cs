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
}
