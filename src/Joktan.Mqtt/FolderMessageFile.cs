using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Joktan.Core;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

/// <summary>Makes a new folder message: a JSON file in the folder for its topic.</summary>
public static class FolderMessageFile
{
    /// <summary>
    /// Writes <paramref name="body"/>, indented, to <c>&lt;folder&gt;/&lt;topic&gt;/&lt;name&gt;.json</c>
    /// and returns its path. Every failure is reported as a <see cref="FormatException"/> whose message
    /// can be shown as it is: no folder, a topic with wildcards or not allowed, a file name not
    /// allowed, a body that is not JSON, or a file that is already there.
    /// </summary>
    public static string Create(string? folder, string topic, string name, string body)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new FormatException(Strings.FolderMessageFile_ChooseAFolderFirst);
        topic = topic.Trim().Trim('/');
        if (topic.Length == 0)
            throw new FormatException(Strings.FolderMessageFile_EnterATopic);
        if (topic.Contains('+') || topic.Contains('#'))
            throw new FormatException(Strings.FolderMessageFile_ATopicForAMessageHasNoWildcards);
        if (TopicRules.Validate(topic) is { } topicProblem)
            throw new FormatException(topicProblem);
        var levels = topic.Split('/');
        if (levels.Any(l => l.Length == 0 || l is "." or ".." || l.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new FormatException(Strings.FolderMessageFile_EachLevelOfTheTopicBecomesAFolder);

        name = name.Trim();
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            name = name[..^5];
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.StartsWith('.')
            || name.EndsWith(".send", StringComparison.OrdinalIgnoreCase))
            throw new FormatException(Strings.FolderMessageFile_EnterAFileNameWithoutFolders);

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(body);
        }
        catch (JsonException e)
        {
            throw new FormatException(string.Format(Strings.FolderMessageFile_TheBodyIsNotValidJson, e.Message), e);
        }

        var directory = Path.Combine([folder.Trim(), .. levels]);
        var path = Path.Combine(directory, name + ".json");
        if (File.Exists(path))
            throw new FormatException(string.Format(Strings.FolderMessageFile_IsAlreadyThere, Path.GetRelativePath(folder.Trim(), path)));

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, (parsed?.ToJsonString(JsonFormats.Indented) ?? "null") + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new FormatException(string.Format(Strings.FolderMessageFile_CannotBeWritten, path, e.Message), e);
        }

        return path;
    }
}
