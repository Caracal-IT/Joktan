using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Joktan.Mqtt;

/// <summary>
/// Turns a folder into messages: every .json file below it is a message whose topic is the path
/// of the folder that holds it.
/// </summary>
public static class FolderScanner
{
    public static List<FolderMessage> Scan(string root)
    {
        return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(MessageSettings.FileSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(path => Read(root, path))
            .OrderBy(m => m.Topic, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FolderMessage Read(string root, string path)
    {
        var relative = Path.GetRelativePath(root, Path.GetDirectoryName(path)!);
        var folder = relative == "."
            ? ""
            : relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

        string text = "";
        JsonNode? body = null;
        var settings = MessageSettings.Default;
        string? error = null;

        try
        {
            text = File.ReadAllText(path);
            body = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (body is null)
                error ??= "The file holds no JSON value";
        }
        catch (JsonException)
        {
            error ??= "Not valid JSON";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error ??= $"Cannot be read: {e.Message}";
        }

        var settingsPath = MessageSettings.PathFor(path);
        if (body is not null && File.Exists(settingsPath))
        {
            try
            {
                var parsed = MessageSettings.Parse(File.ReadAllText(settingsPath));
                if (VaryingFields.FindMissing(body, parsed.Fields) is { } missing)
                    error ??= $"Settings name a field that is not in the body: {missing}";
                else if (parsed.Arrays.Select(a => VaryingFields.CheckArray(body, a)).FirstOrDefault(e => e is not null) is { } arrayError)
                    error ??= arrayError;
                else
                    settings = parsed;
                if (parsed.TopicFields.FirstOrDefault(f => !TopicTemplate.IsValidName(f.Path)) is { } badName)
                    error ??= $"\"{badName.Path}\" cannot be a topic field name; use letters, digits, _, - and .";
            }
            catch (FormatException e)
            {
                error ??= $"Settings file is not valid: {e.Message}";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error ??= $"Settings file cannot be read: {e.Message}";
            }
        }

        // A topic from the settings replaces the folder path, so the file may then sit anywhere.
        var topic = TopicTemplate.Effective(folder, settings.Topic);
        if (topic.Length == 0)
            error ??= "Not inside a topic folder, and the settings set no topic";
        else if (TopicTemplate.Check(topic, settings.TopicFields) is { } topicError)
            error ??= topicError;

        return new FolderMessage
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            Folder = folder,
            Topic = topic,
            Text = text,
            Body = body,
            Settings = settings,
            Error = error,
        };
    }
}
