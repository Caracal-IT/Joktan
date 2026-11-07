using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Resources;

namespace Joktan.Workflows.ViewModels;

/// <summary>Settings / Workflow Settings: where workflows are kept, how many run at once, the time limits, and the go command.</summary>
public sealed partial class WorkflowSettingsViewModel : PageViewModel
{
    private readonly WorkflowEngine _engine;
    private WorkflowSettings Settings => _engine.Settings;

    public WorkflowSettingsViewModel(WorkflowEngine engine) : base(Strings.Settings_Title, Strings.Settings_Subtitle)
    {
        _engine = engine;
        FolderText = Settings.Folder;
        engine.Library.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(ErrorWorkflowChoices));
            OnPropertyChanged(nameof(ErrorWorkflowChoice));
        });
        if (engine.Services.Connections is { } connections)
        {
            connections.Changed += ConnectionsChanged;
            ConnectionsChanged();
        }
    }

    // ---- Database connections ----

    /// <summary>The databases workflows reach besides those on Data / Database.</summary>
    public ObservableCollection<ConnectionRowViewModel> Connections { get; } = [];

    public bool HasConnections => Connections.Count > 0;

    public bool CanKeepConnections => _engine.Services.Connections is not null;

    /// <summary>The connection being made or changed, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingConnection))]
    public partial ConnectionEditorViewModel? ConnectionEditor { get; set; }

    public bool IsEditingConnection => ConnectionEditor is not null;

    [RelayCommand]
    private void AddConnection() => EditConnection(null);

    internal void EditConnection(DatabaseConnectionSettings? existing)
    {
        if (_engine.Services.Connections is not { } connections)
            return;
        ConnectionEditor = new ConnectionEditorViewModel(connections, existing, null, _ => ConnectionEditor = null, () => ConnectionEditor = null);
    }

    internal void RemoveConnection(DatabaseConnectionSettings connection) => _engine.Services.Connections?.Remove(connection.Id);

    private void ConnectionsChanged()
    {
        Connections.Clear();
        foreach (var connection in _engine.Services.Connections?.All ?? [])
            Connections.Add(new ConnectionRowViewModel(connection, this));
        OnPropertyChanged(nameof(HasConnections));
    }

    public string DefaultFolder => WorkflowSettings.DefaultFolder;

    public string CurrentFolder => _engine.Library.Folder;

    /// <summary>Picks a folder, or null when none was chosen. Set by the view.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    [ObservableProperty]
    public partial string FolderText { get; set; }

    [ObservableProperty]
    public partial string? FolderMessage { get; set; }

    public decimal MaxRunning
    {
        get => Settings.MaxRunning;
        set => Change(() => Settings.MaxRunning = (int)Math.Clamp(value, WorkflowSettings.MinRunning, WorkflowSettings.MaxRunningLimit));
    }

    public decimal ShortTimeLimitSeconds
    {
        get => Settings.ShortTimeLimitSeconds;
        set => Change(() => Settings.ShortTimeLimitSeconds = (int)Math.Clamp(value, WorkflowSettings.MinSeconds, WorkflowSettings.MaxSeconds));
    }

    public decimal ScriptTimeLimitSeconds
    {
        get => Settings.ScriptTimeLimitSeconds;
        set => Change(() => Settings.ScriptTimeLimitSeconds = (int)Math.Clamp(value, WorkflowSettings.MinSeconds, WorkflowSettings.MaxSeconds));
    }

    public decimal KeepRuns
    {
        get => Settings.KeepRuns;
        set => Change(() => Settings.KeepRuns = (int)Math.Clamp(value, WorkflowSettings.MinKept, WorkflowSettings.MaxKept));
    }

    public decimal MaxSteps
    {
        get => Settings.MaxSteps;
        set => Change(() => Settings.MaxSteps = (int)Math.Clamp(value, WorkflowSettings.MinSteps, WorkflowSettings.MaxStepsLimit));
    }

    public bool ResumeLongRuns
    {
        get => Settings.ResumeLongRuns;
        set => Change(() => Settings.ResumeLongRuns = value);
    }

    public bool MqttTriggers
    {
        get => Settings.MqttTriggers;
        set
        {
            Change(() => Settings.MqttTriggers = value);
            _engine.RefreshTriggers();
        }
    }

    public bool ScheduleTriggers
    {
        get => Settings.ScheduleTriggers;
        set
        {
            Change(() => Settings.ScheduleTriggers = value);
            _engine.RefreshTriggers();
        }
    }

    public bool WebhookTriggers
    {
        get => Settings.WebhookTriggers;
        set => Change(() => Settings.WebhookTriggers = value);
    }

    /// <summary>What a failed run of a workflow that names no error workflow of its own starts: none, or a workflow.</summary>
    public IReadOnlyList<WorkflowChoice> ErrorWorkflowChoices
        => [new WorkflowChoice("", Strings.Settings_NoErrorWorkflow), .. _engine.Library.All.Select(w => new WorkflowChoice(w.Id, w.Name))];

    public WorkflowChoice? ErrorWorkflowChoice
    {
        get => ErrorWorkflowChoices.FirstOrDefault(c => c.Id == Settings.ErrorWorkflow) ?? ErrorWorkflowChoices[0];
        set
        {
            // The list empties and fills again when the workflows change; that is not a choice.
            if (value is null || value.Id == Settings.ErrorWorkflow)
                return;
            Change(() => Settings.ErrorWorkflow = value.Id);
            OnPropertyChanged();
        }
    }

    /// <summary>Where C# scripts' NuGet packages come from, one per line: feeds' addresses, or folders of .nupkg files.</summary>
    public string NuGetFeeds
    {
        get => Lines(Settings.NuGetFeeds);
        set => Change(() => Settings.NuGetFeeds = FromLines(value));
    }

    /// <summary>The folder scripts work in, unless a step names its own.</summary>
    public string WorkingFolder
    {
        get => Settings.WorkingFolder;
        set => Change(() => Settings.WorkingFolder = value);
    }

    [RelayCommand]
    private async Task BrowseWorkingFolder()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
        {
            WorkingFolder = folder;
            OnPropertyChanged(nameof(WorkingFolder));
        }
    }

    /// <summary>Adds a folder to a list: nuget, csharp, javascript or go.</summary>
    [RelayCommand]
    private async Task AddFolder(string list)
    {
        if (PickFolder is null || await PickFolder() is not { } folder)
            return;
        var target = list switch
        {
            "nuget" => Settings.NuGetFeeds,
            "javascript" => Settings.JavaScriptLibraries,
            "go" => Settings.GoLibraries,
            _ => Settings.CSharpLibraries,
        };
        target.RemoveAll(l => l.Trim().Length == 0);
        if (!target.Contains(folder))
            target.Add(folder);
        Settings.Save();
        OnPropertyChanged(list switch
        {
            "nuget" => nameof(NuGetFeeds),
            "javascript" => nameof(JavaScriptLibraries),
            "go" => nameof(GoLibraries),
            _ => nameof(CSharpLibraries),
        });
    }

    /// <summary>The usings every C# script has, one per line.</summary>
    public string CSharpImports
    {
        get => Lines(Settings.CSharpImports);
        set => Change(() => Settings.CSharpImports = FromLines(value));
    }

    public string CSharpLibraries
    {
        get => Lines(Settings.CSharpLibraries);
        set => Change(() => Settings.CSharpLibraries = FromLines(value));
    }

    public string JavaScriptLibraries
    {
        get => Lines(Settings.JavaScriptLibraries);
        set => Change(() => Settings.JavaScriptLibraries = FromLines(value));
    }

    public string GoLibraries
    {
        get => Lines(Settings.GoLibraries);
        set => Change(() => Settings.GoLibraries = FromLines(value));
    }

    private static string Lines(IEnumerable<string> items) => string.Join("\n", items);

    // Kept as typed, blank lines and all, so that typing a new line is not undone; blanks are skipped where they are used.
    private static List<string> FromLines(string text) => [.. text.Replace("\r", "").Split('\n')];

    public string GoCommand
    {
        get => Settings.GoCommand;
        set => Change(() => Settings.GoCommand = value);
    }

    [ObservableProperty]
    public partial string? GoVersion { get; set; }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
            FolderText = folder;
    }

    [RelayCommand]
    private void UseDefaultFolder() => FolderText = "";

    /// <summary>Reads workflows from the folder typed in; an empty box is the default folder.</summary>
    [RelayCommand]
    private void ApplyFolder()
    {
        var folder = FolderText.Trim();
        try
        {
            var path = folder.Length == 0 ? WorkflowSettings.DefaultFolder : Path.GetFullPath(folder);
            Directory.CreateDirectory(path);
            Settings.Folder = folder;
            Settings.Save();
            _engine.Library.UseFolder(path);
            FolderMessage = _engine.Library.Error ?? string.Format(Strings.Settings_ReadWorkflows, _engine.Library.All.Count, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            FolderMessage = string.Format(Strings.Settings_FolderCannotBeUsed, e.Message);
        }

        OnPropertyChanged(nameof(CurrentFolder));
    }

    /// <summary>Runs the go command to show which version it is, or why it cannot be found.</summary>
    [RelayCommand]
    private async Task CheckGo()
    {
        GoVersion = Strings.Settings_Checking;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Settings.Go, "version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                GoVersion = Strings.Settings_GoNotFound;
                return;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            GoVersion = process.ExitCode == 0 ? output.Trim() : Strings.Settings_GoNotFound;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            GoVersion = Strings.Settings_GoNotFound;
        }
    }

    private void Change(Action change, [CallerMemberName] string name = "")
    {
        change();
        Settings.Save();
        OnPropertyChanged(name);
    }
}

/// <summary>A database connection on Settings / Workflow Settings.</summary>
public sealed partial class ConnectionRowViewModel(DatabaseConnectionSettings connection, WorkflowSettingsViewModel page) : ViewModelBase
{
    public DatabaseConnectionSettings Connection { get; } = connection;

    public string Name => Connection.Name;

    public string ConnectionText => Connection.ConnectionText;

    [RelayCommand]
    private void Edit() => page.EditConnection(Connection);

    [RelayCommand]
    private void Remove() => page.RemoveConnection(Connection);
}
