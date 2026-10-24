using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Compose;

namespace Joktan.ViewModels;

/// <summary>A service of the compose project and its container, if it has one.</summary>
public sealed record ComposeServiceRow(string Service, ComposeContainer? Container)
{
    public string Name => Container?.Name ?? "";

    public string Status => Container?.Status ?? "Not created";

    public string Ports => Container?.Ports ?? "";

    public bool IsRunning => Container?.IsRunning == true;

    public bool IsStopped => Container is { IsRunning: false };
}

/// <summary>
/// Dev Tools / Containers: starts, stops and takes down a compose project with Docker or Podman,
/// and shows its containers' logs. Specified in docs/features/dev-tools/containers/spec.md.
/// </summary>
public partial class ContainersViewModel : PageViewModel
{
    public const string AllServices = "All services";

    private const int MaxLogLines = 5000;
    private const int MaxOutputLines = 1000;

    private readonly ComposeSettings _settings;
    private readonly string? _dataFolder;
    private readonly ConcurrentQueue<string> _incomingLogs = new();
    private readonly ConcurrentQueue<string> _incomingOutput = new();
    private readonly DispatcherTimer _flushTimer;
    private readonly DispatcherTimer _statusTimer;
    private ComposeTool? _tool;
    private CancellationTokenSource? _command;
    private CancellationTokenSource? _logs;
    private bool _refreshing;
    private bool _refreshAgain;
    private string? _statusError;

    // The logs are paused while a command runs, since a recreated container is not followed, and
    // continue afterwards from when they paused. They also continue after a command when they
    // ended because every container stopped.
    private bool _wantLogs;
    private DateTimeOffset? _logsEndedAt;

    public ContainersViewModel(string? dataFolder = null)
        : base("Containers", "Dev Tools / Containers")
    {
        _dataFolder = dataFolder;
        _settings = ComposeSettings.Load(dataFolder);
        ComposeFile = _settings.ComposeFile;
        SelectedEngine = _settings.Engine;
        _flushTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Flush());
        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, async (_, _) => await RefreshStatusAsync());
    }

    public IReadOnlyList<ContainerEngine> Engines { get; } = Enum.GetValues<ContainerEngine>();

    public string? DefaultComposeFile { get; } = ComposeSettings.FindRepositoryComposeFile();

    /// <summary>The compose file in use: the one chosen, or the repository's.</summary>
    public string? EffectiveComposeFile => ComposeFile.Trim() is { Length: > 0 } chosen ? Path.GetFullPath(chosen) : DefaultComposeFile;

    [ObservableProperty]
    public partial string ComposeFile { get; set; }

    [ObservableProperty]
    public partial ContainerEngine SelectedEngine { get; set; }

    /// <summary>Which engine and command are used, once known.</summary>
    [ObservableProperty]
    public partial string? EngineText { get; set; }

    public ObservableCollection<ComposeServiceRow> Services { get; } = [];

    /// <summary>"All services" followed by each service, for the logs.</summary>
    public ObservableCollection<string> LogServices { get; } = [AllServices];

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpCommand), nameof(StopCommand), nameof(RestartCommand), nameof(DownCommand), nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>What is running now, such as "Starting".</summary>
    [ObservableProperty]
    public partial string? Activity { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>What the commands run from this page wrote, each after its command line.</summary>
    public ObservableCollection<string> Output { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogsButtonText))]
    public partial bool IsFollowingLogs { get; set; }

    public string LogsButtonText => IsFollowingLogs ? "Stop" : "Show logs";

    [ObservableProperty]
    public partial string SelectedLogService { get; set; } = AllServices;

    [ObservableProperty]
    public partial bool ShowTimestamps { get; set; }

    /// <summary>How many earlier lines of each container's log to show first.</summary>
    [ObservableProperty]
    public partial int TailLines { get; set; } = 200;

    /// <summary>Whether the log list keeps the newest line in view.</summary>
    [ObservableProperty]
    public partial bool AutoScroll { get; set; } = true;

    /// <summary>Raised on the UI thread after lines are added to <see cref="LogLines"/>.</summary>
    public event Action? LogLinesAdded;

    /// <summary>Asks the user for a compose file; null when they cancel. Set by the view.</summary>
    public Func<Task<string?>>? PickComposeFile { get; set; }

    /// <summary>Starts checking the containers every few seconds. Called while the page is shown.</summary>
    public void Activate()
    {
        _statusTimer.Start();
        _ = RefreshStatusAsync();
    }

    public void Deactivate() => _statusTimer.Stop();

    /// <summary>Stops the log stream and any running command. Called when the app exits.</summary>
    public void Shutdown()
    {
        _statusTimer.Stop();
        _logs?.Cancel();
        _command?.Cancel();
    }

    partial void OnComposeFileChanged(string value) => SettingsChanged();

    partial void OnSelectedEngineChanged(ContainerEngine value) => SettingsChanged();

    private void SettingsChanged()
    {
        OnPropertyChanged(nameof(EffectiveComposeFile));
        if (_settings is null || (_settings.ComposeFile == ComposeFile && _settings.Engine == SelectedEngine))
            return;

        _settings.ComposeFile = ComposeFile;
        _settings.Engine = SelectedEngine;
        try
        {
            _settings.Save(_dataFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = $"Could not save the settings: {e.Message}";
        }

        _tool = null;
        _wantLogs = false;
        _logs?.Cancel();
        IsFollowingLogs = false;
        Services.Clear();
        LogServices.Clear();
        LogServices.Add(AllServices);
        SelectedLogService = AllServices;
        _ = RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (PickComposeFile is not null && await PickComposeFile() is { } file)
            ComposeFile = file;
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        _tool = null;
        return RefreshStatusAsync();
    }

    private bool CanRun => !IsBusy;

    /// <summary>Creates and starts the containers, building images where the file says so.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task UpAsync(string? service) => RunCommandAsync("Starting", ["up", "-d", .. Only(service)]);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task StopAsync(string? service) => RunCommandAsync("Stopping", ["stop", .. Only(service)]);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RestartAsync(string? service) => RunCommandAsync("Restarting", ["restart", .. Only(service)]);

    /// <summary>Stops and removes the containers and networks; volumes are kept.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DownAsync() => RunCommandAsync("Taking down", ["down"]);

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _command?.Cancel();

    [RelayCommand]
    private void ClearOutput() => Output.Clear();

    [RelayCommand]
    private void ToggleLogs()
    {
        if (IsFollowingLogs)
        {
            _wantLogs = false;
            _logs?.Cancel();
            // While a command runs the logs are paused, so there is nothing to cancel.
            if (_logs is null)
                IsFollowingLogs = false;
        }
        else
        {
            _wantLogs = true;
            _ = FollowLogsAsync(resume: false);
        }
    }

    [RelayCommand]
    private void ClearLogs() => LogLines.Clear();

    // Changing what the logs show starts them again with the new choice.
    partial void OnSelectedLogServiceChanged(string value)
    {
        // The list sets null while it is refilled.
        if (value is null)
            SelectedLogService = AllServices;
        else
            RestartLogs();
    }

    partial void OnShowTimestampsChanged(bool value) => RestartLogs();

    private void RestartLogs()
    {
        if (!IsFollowingLogs)
            return;
        _logs?.Cancel();
        _ = FollowLogsAsync(resume: false);
    }

    private static string[] Only(string? service) => string.IsNullOrEmpty(service) ? [] : [service];

    private async Task<ComposeTool?> ToolAsync()
    {
        if (_tool is not null)
            return _tool;

        if (EffectiveComposeFile is not { } file)
        {
            ShowStatusError("Choose a compose file.");
            return null;
        }

        if (!File.Exists(file))
        {
            ShowStatusError($"{file} does not exist.");
            return null;
        }

        try
        {
            _tool = await ComposeTool.ResolveAsync(SelectedEngine, file);
            EngineText = $"{_tool.EngineName} compose, with {_tool.Executable}";
            return _tool;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            EngineText = null;
            ShowStatusError(e.Message);
            return null;
        }
    }

    private async Task RunCommandAsync(string activity, string[] arguments)
    {
        if (IsBusy || await ToolAsync() is not { } tool)
            return;

        IsBusy = true;
        Activity = activity;
        Error = null;
        _command = new CancellationTokenSource();
        if (_logs is { } logs)
        {
            // Cleared first, so the stream ending does not count as the user stopping it.
            _logs = null;
            logs.Cancel();
            _logsEndedAt = DateTimeOffset.UtcNow;
        }

        AddOutput("");
        AddOutput("$ " + tool.CommandLine(arguments));
        try
        {
            var exitCode = await tool.RunAsync(arguments, AddOutput, _command.Token);
            if (exitCode != 0)
                Error = $"{tool.CommandLine(arguments)} failed with exit code {exitCode}. See the output below.";
        }
        catch (OperationCanceledException)
        {
            AddOutput("Cancelled.");
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Error = e.Message;
        }
        finally
        {
            _command.Dispose();
            _command = null;
            Activity = null;
            IsBusy = false;
        }

        await RefreshStatusAsync();
        if (_wantLogs && _logs is null)
            _ = FollowLogsAsync(resume: true);
    }

    private async Task RefreshStatusAsync()
    {
        // A check already under way may have started before a command changed the containers.
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }

        _refreshing = true;
        do
        {
            _refreshAgain = false;
            await ReadStatusAsync();
        }
        while (_refreshAgain);

        _refreshing = false;
    }

    private async Task ReadStatusAsync()
    {
        try
        {
            if (await ToolAsync() is not { } tool)
            {
                Services.Clear();
                Summary = "";
                return;
            }

            var services = (await tool.ReadAsync(["config", "--services"]))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var containers = ComposeContainer.Parse(await tool.ReadAsync(["ps", "--all", "--format", "json"]));

            var rows = services.Select(s => new ComposeServiceRow(s, containers.FirstOrDefault(c => c.Service == s)))
                .Concat(containers.Where(c => !services.Contains(c.Service)).Select(c => new ComposeServiceRow(c.Service, c)))
                .ToList();
            if (!rows.SequenceEqual(Services))
            {
                Services.Clear();
                foreach (var row in rows)
                    Services.Add(row);
            }

            foreach (var service in rows.Select(r => r.Service).Where(s => !LogServices.Contains(s)))
                LogServices.Add(service);

            var running = rows.Count(r => r.IsRunning);
            Summary = rows.Count == 0 ? "The file has no services." : $"{running} of {rows.Count} running";

            // A failed command's message stays until the next command; only a status problem clears.
            if (Error is not null && Error == _statusError)
                Error = null;
            _statusError = null;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            ShowStatusError($"Could not read the containers: {e.Message}");
        }
    }

    private async Task FollowLogsAsync(bool resume)
    {
        if (await ToolAsync() is not { } tool)
            return;

        _logs?.Cancel();
        var cancel = _logs = new CancellationTokenSource();
        List<string> arguments = ["logs", "--follow", "--no-color"];
        if (resume && _logsEndedAt is { } since)
            arguments.AddRange(["--since", since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")]);
        else
            arguments.AddRange(["--tail", Math.Max(0, TailLines).ToString()]);
        if (ShowTimestamps)
            arguments.Add("--timestamps");
        if (SelectedLogService != AllServices)
            arguments.AddRange(["--no-log-prefix", SelectedLogService]);

        IsFollowingLogs = true;
        _flushTimer.Start();
        if (!resume)
        {
            _incomingLogs.Clear();
            LogLines.Clear();
            _incomingLogs.Enqueue($"── {tool.CommandLine(arguments)}");
        }

        try
        {
            var exitCode = await tool.RunAsync(arguments, _incomingLogs.Enqueue, cancel.Token);
            _logsEndedAt = DateTimeOffset.UtcNow;
            if (_logs == cancel)
            {
                _incomingLogs.Enqueue(exitCode == 0
                    ? "── The logs ended because no container is running. They continue when one is started from this page."
                    : $"── The logs stopped with exit code {exitCode}.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _incomingLogs.Enqueue($"── {e.Message}");
        }

        if (_logs == cancel)
        {
            _logs = null;
            IsFollowingLogs = false;
        }

        cancel.Dispose();
    }

    private void ShowStatusError(string message) => Error = _statusError = message;

    private void AddOutput(string line) => _incomingOutput.Enqueue(line);

    private void Flush()
    {
        Drain(_incomingOutput, Output, MaxOutputLines);
        if (Drain(_incomingLogs, LogLines, MaxLogLines))
            LogLinesAdded?.Invoke();

        if (!IsFollowingLogs && !IsBusy && _incomingLogs.IsEmpty && _incomingOutput.IsEmpty)
            _flushTimer.Stop();
    }

    private static bool Drain(ConcurrentQueue<string> incoming, ObservableCollection<string> lines, int max)
    {
        var added = false;
        while (incoming.TryDequeue(out var line))
        {
            lines.Add(line);
            added = true;
        }

        while (lines.Count > max)
            lines.RemoveAt(0);
        return added;
    }

    partial void OnIsBusyChanged(bool value)
    {
        if (value)
            _flushTimer.Start();
    }
}
