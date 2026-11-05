using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Joktan.Logs;

/// <summary>A line of a stack trace that names a file, such as <c>at A.B() in /src/B.cs:line 12</c>.</summary>
public sealed record StackFrame(string Text, string? Method, string File, int Line);

/// <summary>Logs taken to be the same error: the same app, exception type and first frame, or else the same message.</summary>
public sealed partial record ErrorGroup(IReadOnlyList<LogEntry> Entries)
{
    /// <summary>The newest of the logs, whose details are shown.</summary>
    public LogEntry Latest => Entries[0];

    public int Count => Entries.Count;

    public DateTimeOffset First => Entries[^1].Time;

    public DateTimeOffset Last => Entries[0].Time;

    public LogLevel Level => Entries.Max(e => e.Level);

    public string App => Latest.App;

    public IReadOnlyList<StackFrame> Frames => FramesOf(Latest.StackTrace);

    /// <summary>The logs in groups, the one seen last first.</summary>
    public static IReadOnlyList<ErrorGroup> Of(IEnumerable<LogEntry> entries)
        => entries.OrderByDescending(e => e.Time)
            .GroupBy(KeyOf)
            .Select(g => new ErrorGroup(g.ToList()))
            .OrderByDescending(g => g.Last)
            .ToList();

    /// <summary>What two logs share when they are the same error.</summary>
    public static string KeyOf(LogEntry entry)
    {
        var app = entry.App.ToLowerInvariant();
        var frame = FirstMethod(entry.StackTrace);
        if (entry.ErrorType is not null || frame is not null)
            return $"{app}|{entry.ErrorType}|{frame}";
        // Numbers, ids and quoted values differ between logs of the same error.
        return $"{app}|{Varying().Replace(entry.Message, "#")}";
    }

    // The first frame of the stack; its file and line are left out, as they move as the code changes.
    private static string? FirstMethod(string? stack)
    {
        if (stack is null)
            return null;
        foreach (var line in stack.Split('\n'))
        {
            var text = line.Trim();
            if (text.StartsWith("at ", StringComparison.Ordinal))
                return Location().Replace(text, "").Trim();
        }

        return null;
    }

    /// <summary>The lines of a stack trace that name a file and a line, in order.</summary>
    public static IReadOnlyList<StackFrame> FramesOf(string? stack)
    {
        if (stack is null)
            return [];
        var frames = new List<StackFrame>();
        foreach (var line in stack.Split('\n'))
        {
            var text = line.Trim();
            if (DotNetFrame().Match(text) is { Success: true } dotNet)
                frames.Add(new StackFrame(text, dotNet.Groups["method"].Value, dotNet.Groups["file"].Value, int.Parse(dotNet.Groups["line"].Value)));
            else if (OtherFrame().Match(text) is { Success: true } other)
                frames.Add(new StackFrame(text, null, other.Groups["file"].Value, int.Parse(other.Groups["line"].Value)));
        }

        return frames;
    }

    [GeneratedRegex(@"""[^""]*""|'[^']*'|\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b|\d+", RegexOptions.IgnoreCase)]
    private static partial Regex Varying();

    [GeneratedRegex(@"\s+in\s+.+:line\s+\d+$")]
    private static partial Regex Location();

    [GeneratedRegex(@"^at\s+(?<method>.+?)\s+in\s+(?<file>.+):line\s+(?<line>\d{1,7})$")]
    private static partial Regex DotNetFrame();

    // Java, Python, Node and others: a path with an extension, then a line number.
    [GeneratedRegex(@"(?<file>(?:[A-Za-z]:)?[\w./\\ -]*\.\w{1,6})""?(?:,\s*line\s+|:)(?<line>\d{1,7})")]
    private static partial Regex OtherFrame();
}
