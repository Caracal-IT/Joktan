using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Logs.Resources;
using Microsoft.Extensions.Logging;

namespace Joktan.Logs.ViewModels;

/// <summary>How far back to look.</summary>
public enum TimeWindow
{
    Last15Minutes,
    LastHour,
    Last24Hours,
    Last7Days,
    Custom,
}

/// <summary>A window to choose from, by its name on the page.</summary>
public sealed record TimeWindowChoice(TimeWindow Window, string Name);

/// <summary>
/// Dev Tools / Errors: looks for errors in a source's logs over a window of time, groups them, follows their stack
/// traces into the code, and hands one to the agent to judge. Nothing is read until Search is pressed. Specified in
/// docs/features/dev-tools/errors/spec.md.
/// </summary>
public partial class ErrorsViewModel : PageViewModel
{
    public const string TimeFormat = "yyyy-MM-dd HH:mm";

    private readonly LogSourcesViewModel _sources;
    private CancellationTokenSource? _search;

    public ErrorsViewModel(LogSourcesViewModel sources)
        : base(Strings.Errors_Errors, Strings.Errors_DevToolsErrors)
    {
        _sources = sources;
        sources.Changed += SourcesChanged;
        SelectedSource = Sources.FirstOrDefault();
        SelectedWindow = Windows[1];
        var now = DateTimeOffset.Now;
        _fromText = now.AddHours(-1).ToString(TimeFormat, CultureInfo.InvariantCulture);
        _toText = now.ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>The time now; replaced in tests.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;

    /// <summary>What searches talk through instead of the network, in tests.</summary>
    public HttpMessageHandler? Handler { get; init; }

    public IEnumerable<LogSource> Sources => _sources.Settings.Sources;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial LogSource? SelectedSource { get; set; }

    public IReadOnlyList<TimeWindowChoice> Windows { get; } =
    [
        new(TimeWindow.Last15Minutes, Strings.Errors_Last15Minutes),
        new(TimeWindow.LastHour, Strings.Errors_LastHour),
        new(TimeWindow.Last24Hours, Strings.Errors_Last24Hours),
        new(TimeWindow.Last7Days, Strings.Errors_Last7Days),
        new(TimeWindow.Custom, Strings.Errors_Custom),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom))]
    public partial TimeWindowChoice SelectedWindow { get; set; } = null!;

    partial void OnSelectedWindowChanged(TimeWindowChoice value)
    {
        // The list sets null while it is changed.
        if (value is null)
            SelectedWindow = Windows[1];
    }

    public bool IsCustom => SelectedWindow?.Window == TimeWindow.Custom;

    private string _fromText;

    /// <summary>The start of a custom window, as typed, in this computer's time.</summary>
    public string FromText
    {
        get => _fromText;
        set
        {
            if (SetProperty(ref _fromText, value))
                CheckWindow();
        }
    }

    private string _toText;

    public string ToText
    {
        get => _toText;
        set
        {
            if (SetProperty(ref _toText, value))
                CheckWindow();
        }
    }

    [ObservableProperty]
    public partial string? WindowError { get; set; }

    [ObservableProperty]
    public partial bool ShowWarnings { get; set; }

    [ObservableProperty]
    public partial bool ShowErrors { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowCritical { get; set; } = true;

    /// <summary>Only this app's logs, when given.</summary>
    [ObservableProperty]
    public partial string AppFilter { get; set; } = "";

    /// <summary>Only logs with this in their message or stack trace, when given.</summary>
    [ObservableProperty]
    public partial string TextFilter { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<ErrorGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    public partial ErrorGroupViewModel? SelectedGroup { get; set; }

    partial void OnSelectedGroupChanged(ErrorGroupViewModel? value) => value?.SelectFirstFrame();

    /// <summary>What the last search found, such as "12 errors in 3 groups".</summary>
    [ObservableProperty]
    public partial string? Summary { get; set; }

    /// <summary>Why the last search failed, or what it could not read.</summary>
    [ObservableProperty]
    public partial string? Problem { get; set; }

    /// <summary>The window the last search looked at.</summary>
    public (DateTimeOffset From, DateTimeOffset To)? SearchedWindow { get; private set; }

    /// <summary>Puts a request in the agent's message box and opens the panel. Set by the app.</summary>
    public Action<string>? AskAgent { get; set; }

    /// <summary>Opens a file in the app that opens it. Set by the view.</summary>
    public Func<string, Task>? OpenFile { get; set; }

    /// <summary>Asks for a folder. Set by the view.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>The window chosen, or null with the reason when a custom one is not right.</summary>
    public (DateTimeOffset From, DateTimeOffset To)? Window()
    {
        var now = Now();
        return SelectedWindow.Window switch
        {
            TimeWindow.Last15Minutes => (now.AddMinutes(-15), now),
            TimeWindow.LastHour => (now.AddHours(-1), now),
            TimeWindow.Last24Hours => (now.AddDays(-1), now),
            TimeWindow.Last7Days => (now.AddDays(-7), now),
            _ => CheckWindow(),
        };
    }

    private (DateTimeOffset From, DateTimeOffset To)? CheckWindow()
    {
        var from = ParseTime(FromText);
        var to = ParseTime(ToText);
        WindowError = from is null || to is null ? string.Format(Strings.Errors_TimeRule, TimeFormat)
            : from >= to ? Strings.Errors_FromBeforeTo
            : null;
        return WindowError is null ? (from!.Value, to!.Value) : null;
    }

    /// <summary>A time as typed, such as 2026-11-03 14:30, in this computer's time; seconds may be given.</summary>
    public static DateTimeOffset? ParseTime(string text)
        => DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var time) ? time : null;

    public IReadOnlySet<LogLevel> Levels()
    {
        var levels = new HashSet<LogLevel>();
        if (ShowWarnings)
            levels.Add(LogLevel.Warning);
        if (ShowErrors)
            levels.Add(LogLevel.Error);
        if (ShowCritical)
            levels.Add(LogLevel.Critical);
        return levels;
    }

    private bool CanSearch => !IsBusy && SelectedSource is not null;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        Problem = null;
        if (SelectedSource is not { } source)
            return;
        if (Window() is not { } window)
        {
            Problem = WindowError;
            return;
        }

        var levels = Levels();
        if (levels.Count == 0)
        {
            Problem = Strings.Errors_ChooseALevel;
            return;
        }

        _search?.Cancel();
        _search = new CancellationTokenSource();
        var cancel = _search.Token;
        IsBusy = true;
        Summary = Strings.Errors_Searching;
        try
        {
            var query = new LogQuery(window.From, window.To, levels, AppFilter, TextFilter);
            var result = await source.Reader(_sources.Telemetry, Handler).ReadAsync(query, cancel);
            Groups.Clear();
            foreach (var group in ErrorGroup.Of(result.Entries))
                Groups.Add(new ErrorGroupViewModel(group, source, this));
            SearchedWindow = window;
            Problem = result.Problem;
            Summary = SummaryOf(result);
            SelectedGroup = Groups.FirstOrDefault();
            AppLog.For("Logs").LogInformation("Searched {Source} for errors: {Count} found", source.DisplayName, result.Entries.Count);
        }
        catch (OperationCanceledException)
        {
            Summary = Strings.Errors_Cancelled;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string SummaryOf(LogResult result)
    {
        if (result.Entries.Count == 0)
            return result.Problem is null ? Strings.Errors_NothingFound : "";
        var logs = result.Entries.Count == 1 ? Strings.Errors_OneLog : string.Format(Strings.Errors_Logs, result.Entries.Count);
        var groups = Groups.Count == 1 ? Strings.Errors_OneGroup : string.Format(Strings.Errors_Groups, Groups.Count);
        var text = string.Format(Strings.Errors_Found, logs, groups);
        return result.Truncated ? text + " " + string.Format(Strings.Errors_Newest, LogQuery.DefaultLimit) : text;
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _search?.Cancel();

    /// <summary>Hands the chosen error to the agent: what it is, its stack trace, and where the code is.</summary>
    [RelayCommand]
    private void Ask(ErrorGroupViewModel? group)
    {
        if ((group ?? SelectedGroup) is { } chosen)
            AskAgent?.Invoke(chosen.Prompt());
    }

    /// <summary>Remembers where the chosen error's app keeps its code, on Settings / Log Sources too.</summary>
    [RelayCommand]
    private async Task ChooseFolderAsync(ErrorGroupViewModel? group)
    {
        if ((group ?? SelectedGroup) is not { } chosen || PickFolder is not { } pick || await pick() is not { } folder)
            return;
        SetFolder(chosen.Source, chosen.Group.App, folder);
    }

    /// <summary>Sets where an app's code is, for a source, and follows the stack traces again.</summary>
    public void SetFolder(LogSource source, string app, string folder)
    {
        var entry = source.Folders.FirstOrDefault(f => string.Equals(f.App.Trim(), app.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            source.Folders.Add(entry = new AppFolder { App = app.Trim() });
        entry.Folder = folder;
        _sources.Reload(source);
        _sources.Save();
    }

    [RelayCommand]
    private async Task OpenAsync(FrameViewModel? frame)
    {
        if (frame?.FullPath is { } path && OpenFile is { } open)
            await open(path);
    }

    // A source removed from settings goes from the list; one renamed shows its new name.
    private void SourcesChanged()
    {
        var chosen = SelectedSource;
        OnPropertyChanged(nameof(Sources));
        SelectedSource = chosen is not null && Sources.Contains(chosen) ? chosen : Sources.FirstOrDefault();
        foreach (var group in Groups)
            group.FollowFrames();
    }
}

/// <summary>One group of errors on the Errors page.</summary>
public partial class ErrorGroupViewModel : ObservableObject
{
    private const int MaxOccurrences = 100;
    private const int MaxStackInPrompt = 6000;
    private const int LinesAround = 6;

    private readonly ErrorsViewModel _page;

    public ErrorGroupViewModel(ErrorGroup group, LogSource source, ErrorsViewModel page)
    {
        Group = group;
        Source = source;
        _page = page;
        FollowFrames();
    }

    public ErrorGroup Group { get; }

    public LogSource Source { get; }

    /// <summary>The exception's type, or else the message.</summary>
    public string Title => Group.Latest.ErrorType ?? FirstLine(Group.Latest.Message);

    public string Message => Group.Latest.Message;

    public bool HasType => Group.Latest.ErrorType is not null;

    public string? StackTrace => Group.Latest.StackTrace;

    public bool HasStackTrace => StackTrace is not null;

    public string CountText => Group.Count.ToString(CultureInfo.CurrentCulture);

    public string LastText => Group.Last.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string FirstText => Group.First.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string LevelText => Group.Level.ToString();

    public bool IsCritical => Group.Level >= LogLevel.Critical;

    public bool IsWarning => Group.Level <= LogLevel.Warning;

    public string AppText => Group.App.Length > 0 ? Group.App : Strings.Errors_UnknownApp;

    public string ModuleText => Group.Latest.Module;

    /// <summary>When each log of the group was written, newest first.</summary>
    public IReadOnlyList<string> Occurrences => Group.Entries.Take(MaxOccurrences)
        .Select(e => e.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).ToList();

    public bool HasMoreOccurrences => Group.Count > MaxOccurrences;

    public ObservableCollection<FrameViewModel> Frames { get; } = [];

    public bool HasFrames => Frames.Count > 0;

    [ObservableProperty]
    public partial FrameViewModel? SelectedFrame { get; set; }

    partial void OnSelectedFrameChanged(FrameViewModel? value) => Snippet = value?.Snippet();

    /// <summary>The code around the chosen frame's line.</summary>
    [ObservableProperty]
    public partial string? Snippet { get; set; }

    /// <summary>The app's source folder, if one is set.</summary>
    public SourceFolder? Folder { get; private set; }

    public string FolderText => Folder is { } folder
        ? folder.Exists ? string.Format(Strings.Errors_CodeIn, folder.Root) : string.Format(Strings.Errors_FolderMissing, folder.Root)
        : string.Format(Strings.Errors_NoFolder, AppText, Source.DisplayName);

    public bool HasFolder => Folder is { Exists: true };

    /// <summary>Finds each frame's file in the source folder, as it is set now.</summary>
    public void FollowFrames()
    {
        var chosen = SelectedFrame?.Frame;
        Folder = Source.FolderFor(Group.App) is { } root ? new SourceFolder(root) : null;
        Frames.Clear();
        foreach (var frame in Group.Frames)
            Frames.Add(new FrameViewModel(frame, Folder is { Exists: true } folder ? Find(folder, frame.File) : null, Folder));
        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(FolderText));
        OnPropertyChanged(nameof(HasFolder));
        OnPropertyChanged(nameof(HasFrames));
        SelectedFrame = Frames.FirstOrDefault(f => f.Frame == chosen);
        if (SelectedFrame is null)
            SelectFirstFrame();
    }

    private static string? Find(SourceFolder folder, string file)
    {
        try
        {
            return folder.Find(file);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Chooses the first frame that is in the code, or else the first.</summary>
    public void SelectFirstFrame() => SelectedFrame ??= Frames.FirstOrDefault(f => f.IsFound) ?? Frames.FirstOrDefault();

    /// <summary>What the agent is asked: to judge whether this is a bug, reading the code with its tools.</summary>
    public string Prompt()
    {
        var latest = Group.Latest;
        var stack = latest.StackTrace is { Length: > MaxStackInPrompt } trace ? trace[..MaxStackInPrompt] + "\n…" : latest.StackTrace;
        var window = _page.SearchedWindow is { } searched
            ? string.Format(Strings.Errors_PromptWindow, searched.From.ToString(ErrorsViewModel.TimeFormat, CultureInfo.InvariantCulture), searched.To.ToString(ErrorsViewModel.TimeFormat, CultureInfo.InvariantCulture))
            : "";
        var code = HasFolder ? string.Format(Strings.Errors_PromptCode, Source.DisplayName, Group.App) : Strings.Errors_PromptNoCode;
        return string.Format(Strings.Errors_Prompt,
            Source.DisplayName, AppText, Group.Count, window, FirstText, LastText, LevelText,
            latest.ErrorType ?? "-", latest.Message, stack ?? Strings.Errors_PromptNoStack, code).Trim();
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 160 ? line[..160] + "…" : line;
    }

    /// <summary>The lines either side of a frame's, as shown under the stack trace.</summary>
    internal static int Around => LinesAround;
}

/// <summary>A frame of a stack trace, and the file it is in here, when it was found.</summary>
public sealed class FrameViewModel(StackFrame frame, string? fullPath, SourceFolder? folder)
{
    public StackFrame Frame { get; } = frame;

    public string? FullPath { get; } = fullPath;

    public bool IsFound => FullPath is not null;

    /// <summary>The file and line, relative to the source folder when found there.</summary>
    public string Location => FullPath is not null && folder is not null
        ? $"{folder.Relative(FullPath)}:{Frame.Line}"
        : $"{Frame.File}:{Frame.Line}";

    public string Method => Frame.Method ?? Frame.Text;

    /// <summary>The code around the frame's line, or why there is none.</summary>
    public string Snippet()
    {
        if (FullPath is null || folder is null)
            return Strings.Errors_NotInTheCode;
        try
        {
            var (lines, total) = folder.Read(FullPath, Frame.Line - ErrorGroupViewModel.Around, ErrorGroupViewModel.Around * 2 + 1);
            return Frame.Line > total ? string.Format(Strings.Errors_LineMoved, Frame.Line, total) : SourceFolder.Around(lines, Frame.Line);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            return string.Format(Strings.Errors_CouldNotReadFile, e.Message);
        }
    }
}
