using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Logs.Resources;
using Microsoft.Extensions.Logging;

namespace Joktan.Logs;

/// <summary>
/// Reads logs from a file, or from the files in a folder that match a filter. A line that starts with a time begins a
/// log, and the lines after it, such as a stack trace, belong to it. A line that is a JSON object is a log by itself.
/// </summary>
public sealed partial class FileLogReader(string path, string filter, bool subfolders) : ILogReader
{
    public Task<LogResult> ReadAsync(LogQuery query, CancellationToken cancel = default) => Task.Run(() => Read(query, cancel), cancel);

    /// <summary>The patterns in a filter, such as <c>*.log;*.txt</c>.</summary>
    public static IReadOnlyList<string> Patterns(string filter)
        => filter.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private LogResult Read(LogQuery query, CancellationToken cancel)
    {
        IReadOnlyList<string> files;
        if (File.Exists(path))
            files = [path];
        else if (Directory.Exists(path))
            files = Files().ToList();
        else
            return LogResult.Failed(string.Format(Strings.FileLogReader_NotFound, path));

        // The newest are kept, oldest first out, so a large folder needs no more memory than the limit.
        var newest = new PriorityQueue<LogEntry, DateTimeOffset>();
        var more = false;
        string? problem = null;
        var read = 0;
        foreach (var file in files)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                // A file last written before the window began can hold nothing in it.
                if (File.GetLastWriteTimeUtc(file) < query.From.UtcDateTime)
                    continue;

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                foreach (var entry in Parse(Lines(reader)))
                {
                    if (!query.Matches(entry))
                        continue;
                    newest.Enqueue(entry, entry.Time);
                    if (newest.Count > query.Limit)
                    {
                        newest.Dequeue();
                        more = true;
                    }
                }

                read++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problem ??= string.Format(Strings.FileLogReader_CouldNotRead, file, e.Message);
            }
        }

        if (read == 0 && problem is not null)
            return LogResult.Failed(problem);

        var entries = new List<LogEntry>(newest.Count);
        while (newest.Count > 0)
            entries.Add(newest.Dequeue());
        entries.Reverse();
        return new LogResult(entries, more, problem);
    }

    private IEnumerable<string> Files()
    {
        var options = new EnumerationOptions { RecurseSubdirectories = subfolders, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        return Patterns(filter).SelectMany(pattern => Directory.EnumerateFiles(path, pattern, options)).Distinct();
    }

    private static IEnumerable<string> Lines(StreamReader reader)
    {
        while (reader.ReadLine() is { } line)
            yield return line;
    }

    /// <summary>The logs in some lines. Lines before the first log are left out.</summary>
    public static IEnumerable<LogEntry> Parse(IEnumerable<string> lines)
    {
        LogEntry? current = null;
        var more = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith('{') && FromJson(line) is { } json)
            {
                if (current is not null)
                    yield return Finish(current, more);
                current = null;
                yield return json;
            }
            else if (Start().Match(line) is { Success: true } start && ParseTime(start.Groups["time"].Value) is { } time)
            {
                if (current is not null)
                    yield return Finish(current, more);
                current = FromText(time, line[start.Length..]);
            }
            else if (current is not null)
            {
                more.Add(line);
            }
        }

        if (current is not null)
            yield return Finish(current, more);
    }

    // The lines after a log are its exception, whose first line names its type.
    private static LogEntry Finish(LogEntry entry, List<string> more)
    {
        while (more.Count > 0 && more[^1].Trim().Length == 0)
            more.RemoveAt(more.Count - 1);
        if (more.Count == 0)
            return entry;

        var stack = string.Join('\n', more);
        more.Clear();
        return entry with { StackTrace = stack, ErrorType = entry.ErrorType ?? ErrorTypeOf(stack) };
    }

    /// <summary>The exception's type at the start of a stack trace, such as System.InvalidOperationException.</summary>
    public static string? ErrorTypeOf(string? stack)
        => stack is not null && ExceptionType().Match(stack.TrimStart()) is { Success: true } match ? match.Groups["type"].Value : null;

    private static LogEntry FromText(DateTimeOffset time, string rest)
    {
        var level = LogLevel.Information;
        var app = "";
        var module = "";
        var messageStart = 0;
        var words = Word().Matches(rest);
        var levelFound = false;
        for (var i = 0; i < words.Count && i < 6; i++)
        {
            var word = words[i];
            if (!levelFound && LogLevels.Parse(word.Value) is { } named)
            {
                level = named;
                levelFound = true;
                messageStart = word.Index + word.Length;
            }
            else if (levelFound && word.Value.StartsWith("app=", StringComparison.Ordinal))
            {
                app = word.Value[4..];
                messageStart = word.Index + word.Length;
            }
            else if (levelFound && word.Value.StartsWith("module=", StringComparison.Ordinal))
            {
                module = word.Value[7..];
                messageStart = word.Index + word.Length;
            }
            else if (levelFound)
            {
                break;
            }
        }

        return new LogEntry(time, level, app, module, rest[messageStart..].Trim());
    }

    private static LogEntry? FromJson(string line)
    {
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Text(root, "@timestamp", "@t", "timestamp", "time", "Timestamp") is not { } timeText
                || ParseTime(timeText) is not { } time)
                return null;

            var stack = Text(root, "error.stack_trace", "@x", "exception", "Exception", "stack_trace", "exception.stacktrace");
            return new LogEntry(
                time,
                // Serilog's compact format leaves the level out for information.
                LogLevels.Parse(Text(root, "log.level", "@l", "level", "Level", "severity", "severity_text")) ?? LogLevel.Information,
                Text(root, "service.name", "app", "Application", "attributes.app") ?? "",
                Text(root, "module", "log.logger", "SourceContext", "logger", "attributes.module") ?? "",
                Text(root, "message", "@m", "msg", "Message", "RenderedMessage", "body.text", "@mt", "MessageTemplate") ?? "",
                Text(root, "error.type", "exception.type") ?? ErrorTypeOf(stack),
                stack);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A name such as log.level is either nested, {"log": {"level": …}}, or a key with a dot in it.
    private static string? Text(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (Find(root, name) is { } value)
            {
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
        }

        return null;
    }

    private static JsonElement? Find(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        if (element.TryGetProperty(name, out var whole))
            return whole.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? null : whole;

        for (var dot = name.IndexOf('.'); dot > 0; dot = name.IndexOf('.', dot + 1))
        {
            if (element.TryGetProperty(name[..dot], out var inner) && Find(inner, name[(dot + 1)..]) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>The time a log starts with. One without an offset is in this computer's time zone.</summary>
    public static DateTimeOffset? ParseTime(string text)
    {
        var normal = text.Trim().Replace(',', '.');
        normal = OffsetWithoutColon().Replace(normal, "$1:$2");
        normal = SpaceBeforeOffset().Replace(normal, "$1");
        return DateTimeOffset.TryParse(normal, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out var time)
            ? time
            : null;
    }

    [GeneratedRegex(@"^\[?(?<time>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:[.,]\d{1,9})?(?:\s?(?:Z|[+-]\d{2}:?\d{2}))?)\]?")]
    private static partial Regex Start();

    [GeneratedRegex(@"\S+")]
    private static partial Regex Word();

    [GeneratedRegex(@"^(?<type>[A-Za-z_][\w.`+]*(?:Exception|Error|Fault))\b")]
    private static partial Regex ExceptionType();

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex OffsetWithoutColon();

    [GeneratedRegex(@"\s+([+-]\d{2}:\d{2}|Z)$")]
    private static partial Regex SpaceBeforeOffset();
}
