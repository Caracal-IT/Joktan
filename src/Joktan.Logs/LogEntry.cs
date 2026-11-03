using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Joktan.Logs;

/// <summary>One log, from Elasticsearch or a file.</summary>
/// <param name="App">The app that wrote it, such as <c>joktan</c>; empty when the log does not say.</param>
/// <param name="Module">The module or logger; empty when the log does not say.</param>
/// <param name="StackTrace">The exception and its stack, as written; null when there is none.</param>
public sealed record LogEntry(
    DateTimeOffset Time,
    LogLevel Level,
    string App,
    string Module,
    string Message,
    string? ErrorType = null,
    string? StackTrace = null);

/// <summary>What to look for: a window of time, the levels, and optionally one app and some text.</summary>
public sealed record LogQuery(DateTimeOffset From, DateTimeOffset To, IReadOnlySet<LogLevel> Levels, string? App = null, string? Text = null, int Limit = LogQuery.DefaultLimit)
{
    public const int DefaultLimit = 500;

    public bool Matches(LogEntry entry)
        => entry.Time >= From && entry.Time <= To && Levels.Contains(entry.Level)
           && (string.IsNullOrWhiteSpace(App) || string.Equals(entry.App, App.Trim(), StringComparison.OrdinalIgnoreCase))
           && (string.IsNullOrWhiteSpace(Text) || entry.Message.Contains(Text.Trim(), StringComparison.OrdinalIgnoreCase)
               || (entry.StackTrace?.Contains(Text.Trim(), StringComparison.OrdinalIgnoreCase) ?? false));
}

/// <summary>The newest logs found, at most the limit asked for.</summary>
/// <param name="Truncated">Whether there were more than the limit.</param>
/// <param name="Problem">Why nothing could be read, shown on the page; null when it worked.</param>
public sealed record LogResult(IReadOnlyList<LogEntry> Entries, bool Truncated = false, string? Problem = null)
{
    public static LogResult Failed(string problem) => new([], false, problem);
}

/// <summary>Somewhere logs can be read from.</summary>
public interface ILogReader
{
    Task<LogResult> ReadAsync(LogQuery query, CancellationToken cancel = default);
}

/// <summary>The names levels go by in other apps' logs.</summary>
public static class LogLevels
{
    /// <summary>The levels a search can ask for, lowest first.</summary>
    public static IReadOnlyList<LogLevel> Searchable { get; } = [LogLevel.Warning, LogLevel.Error, LogLevel.Critical];

    /// <summary>What a search asks for unless told otherwise.</summary>
    public static IReadOnlySet<LogLevel> Errors { get; } = new HashSet<LogLevel> { LogLevel.Error, LogLevel.Critical };

    private static readonly Dictionary<string, LogLevel> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trace"] = LogLevel.Trace, ["trc"] = LogLevel.Trace, ["verbose"] = LogLevel.Trace, ["vrb"] = LogLevel.Trace,
        ["debug"] = LogLevel.Debug, ["dbg"] = LogLevel.Debug,
        ["information"] = LogLevel.Information, ["info"] = LogLevel.Information, ["inf"] = LogLevel.Information,
        ["warning"] = LogLevel.Warning, ["warn"] = LogLevel.Warning, ["wrn"] = LogLevel.Warning,
        ["error"] = LogLevel.Error, ["err"] = LogLevel.Error, ["eror"] = LogLevel.Error,
        ["critical"] = LogLevel.Critical, ["crit"] = LogLevel.Critical, ["crt"] = LogLevel.Critical, ["fatal"] = LogLevel.Critical, ["ftl"] = LogLevel.Critical,
    };

    /// <summary>The level a word names, such as ERR or fatal, or null when it names none.</summary>
    public static LogLevel? Parse(string? word)
        => word is not null && Words.TryGetValue(word.Trim().Trim('[', ']', '(', ')', ':', '|', '<', '>'), out var level) ? level : null;

    /// <summary>The words a level is written as, for looking it up in Elasticsearch.</summary>
    public static IEnumerable<string> NamesOf(LogLevel level)
    {
        foreach (var (word, named) in Words)
        {
            if (named == level)
                yield return word;
        }
    }
}
