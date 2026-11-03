using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Logs;
using Joktan.Logs.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

/// <summary>
/// The Errors page's search, and read-only access to the code of the apps whose logs it reads. The code is only ever
/// read inside the source folder set for an app on Settings / Log Sources.
/// </summary>
public static class LogTools
{
    private const int MaxGroups = 50;
    private const int MaxStack = 4000;

    private static readonly ToolParameter SourceName = P.OptText("source", "The source's name, from logs_sources; the one chosen on the Errors page unless given.");
    private static readonly ToolParameter AppName = P.OptText("app", "The app, as its logs name it; the source's folder for any app unless given.");

    private static readonly Dictionary<string, TimeWindow> Windows = new()
    {
        ["15m"] = TimeWindow.Last15Minutes, ["1h"] = TimeWindow.LastHour, ["24h"] = TimeWindow.Last24Hours, ["7d"] = TimeWindow.Last7Days,
    };

    public static IEnumerable<McpTool> Create(ErrorsViewModel errors) =>
    [
        Read("logs_sources", "List where logs can be read from, and the source folder set for each app.", [], _ => errors.Sources.Select(s => new
        {
            Name = s.DisplayName,
            Kind = s.Kind.ToString(),
            Folders = s.Folders.Select(f => new { App = f.App.Length > 0 ? f.App : "(any)", f.Folder, Exists = f.Folder.Length > 0 && Directory.Exists(s.FolderFor(f.App.Length > 0 ? f.App : null)) }).ToList(),
        }).ToList()),
        ReadAsync("logs_errors", "Search a source's logs for errors in a window of time, as the Errors page does, and show the groups found. Groups share an exception type and first frame, or a message.",
            [SourceName, P.OptChoice("window", "How far back: 15m, 1h, 24h or 7d; 1h unless from and to are given.", "15m", "1h", "24h", "7d"),
             P.OptText("from", "The start, as yyyy-MM-dd HH:mm in this computer's time, or ISO 8601."), P.OptText("to", "The end, the same way."),
             P.OptText("levels", "Levels, separated by commas: warning, error, critical; error and critical unless given."),
             P.OptText("app", "Only this app's logs."), P.OptText("text", "Only logs with this in their message or stack trace.")], async args =>
            {
                if (args.OptText("source") is { } name)
                    errors.SelectedSource = errors.Sources.FirstOrDefault(s => string.Equals(s.DisplayName, name.Trim(), StringComparison.OrdinalIgnoreCase))
                                            ?? throw new ToolFailure($"No source is called {name}. logs_sources lists them.");
                SetWindow(errors, args);
                SetLevels(errors, args.OptText("levels"));
                errors.AppFilter = args.OptText("app") ?? "";
                errors.TextFilter = args.OptText("text") ?? "";
                await errors.SearchCommand.ExecuteAsync(null);
                if (errors.Groups.Count == 0)
                    Check(errors.Problem);
                return new
                {
                    Source = errors.SelectedSource?.DisplayName,
                    errors.Summary,
                    errors.Problem,
                    Groups = errors.Groups.Take(MaxGroups).Select(g => new
                    {
                        g.Title,
                        Type = g.Group.Latest.ErrorType,
                        g.Message,
                        App = g.Group.App,
                        Module = g.ModuleText,
                        Level = g.LevelText,
                        g.Group.Count,
                        First = g.FirstText,
                        Last = g.LastText,
                        CodeFolder = g.HasFolder ? g.Folder!.Root : null,
                        Frames = g.Frames.Select(f => new { f.Location, InCode = f.IsFound }).ToList(),
                        StackTrace = g.StackTrace is { Length: > MaxStack } stack ? stack[..MaxStack] + "\n…" : g.StackTrace,
                    }).ToList(),
                    MoreGroups = Math.Max(0, errors.Groups.Count - MaxGroups),
                };
            }),
        ReadAsync("source_search", "Search an app's code for a text, ignoring case. Build output, packages and version control folders are left out.",
            [SourceName, AppName, P.Text("text", "What to look for."), P.OptText("files", "Only files matching this, such as *.cs."), P.OptNumber("limit", "How many lines; 50 unless given, at most 200.")], async args =>
            {
                var folder = Folder(errors, args);
                var text = args.Text("text");
                var pattern = args.OptText("files");
                var limit = Math.Clamp(args.Whole("limit") ?? 50, 1, 200);
                var matches = await Task.Run(() => folder.Search(text, pattern, limit));
                return new { Folder = folder.Root, Matches = matches.Select(m => $"{m.File}:{m.Line}: {m.Text}").ToList() };
            }),
        Read("source_read", "Read lines of a file in an app's code. The path is relative to the source folder, or as a stack trace gives it.",
            [SourceName, AppName, P.Text("path", "The file."), P.OptNumber("from", "The first line; 1 unless given."), P.OptNumber("count", $"How many lines; 80 unless given, at most {SourceFolder.MaxLinesRead}.")], args =>
            {
                var folder = Folder(errors, args);
                var path = args.Text("path");
                var file = (Path.IsPathRooted(path) && folder.Resolve(path) is { } inside && File.Exists(inside) ? inside : null)
                           ?? folder.Find(path)
                           ?? throw new ToolFailure($"{path} is not in the source folder {folder.Root}. source_search finds files.");
                try
                {
                    var (lines, total) = folder.Read(file, args.Whole("from") ?? 1, args.Whole("count") ?? 80);
                    return new { File = folder.Relative(file), Lines = total, Text = string.Join('\n', lines.Select(l => $"{l.Number}: {l.Text}")) };
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new ToolFailure($"Could not read {path}: {e.Message}");
                }
            }),
    ];

    private static SourceFolder Folder(ErrorsViewModel errors, ToolArgs args)
    {
        var source = args.OptText("source") is { } name
            ? errors.Sources.FirstOrDefault(s => string.Equals(s.DisplayName, name.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new ToolFailure($"No source is called {name}. logs_sources lists them.")
            : errors.SelectedSource ?? throw new ToolFailure("No source is chosen. logs_sources lists them.");
        var app = args.OptText("app");
        // With no app named, a source that has one folder only means that one.
        var only = source.Folders.Where(f => f.Folder.Trim().Length > 0).ToList();
        var root = source.FolderFor(app) ?? (app is null && only.Count == 1 ? source.FolderFor(only[0].App) : null) ?? throw new ToolFailure($"No source folder is set for {app ?? "any app"} on {source.DisplayName}. The user sets it on Settings / Log Sources or the Errors page.");
        var folder = new SourceFolder(root);
        return folder.Exists ? folder : throw new ToolFailure($"The source folder {folder.Root} is not there.");
    }

    private static void SetWindow(ErrorsViewModel errors, ToolArgs args)
    {
        if (args.OptText("from") is { } from)
        {
            errors.SelectedWindow = errors.Windows.First(w => w.Window == TimeWindow.Custom);
            errors.FromText = from;
            errors.ToText = args.OptText("to") ?? DateTimeOffset.Now.ToString(ErrorsViewModel.TimeFormat);
            Check(errors.WindowError);
            return;
        }

        var window = args.OptText("window") ?? "1h";
        var chosen = Windows.TryGetValue(window, out var named) ? named : throw new ToolFailure($"The window must be 15m, 1h, 24h or 7d, not {window}.");
        errors.SelectedWindow = errors.Windows.First(w => w.Window == chosen);
    }

    private static void SetLevels(ErrorsViewModel errors, string? levels)
    {
        if (levels is null)
        {
            (errors.ShowWarnings, errors.ShowErrors, errors.ShowCritical) = (false, true, true);
            return;
        }

        var named = levels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(l => l.ToLowerInvariant()).ToHashSet();
        if (named.Except(["warning", "error", "critical"]).FirstOrDefault() is { } unknown)
            throw new ToolFailure($"There is no level called {unknown}. Use warning, error or critical.");
        (errors.ShowWarnings, errors.ShowErrors, errors.ShowCritical) = (named.Contains("warning"), named.Contains("error"), named.Contains("critical"));
    }
}
