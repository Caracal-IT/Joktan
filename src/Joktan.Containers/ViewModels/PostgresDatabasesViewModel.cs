using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Containers.Resources;
using Joktan.Core;
using Microsoft.Extensions.Logging;

namespace Joktan.Containers.ViewModels;

/// <summary>One database in the list on Data / Database, with its container's state.</summary>
public partial class PostgresDatabaseRow : ObservableObject
{
    private readonly PostgresDatabasesViewModel _list;

    public PostgresDatabaseRow(PostgresDatabase database, PostgresDatabasesViewModel list)
    {
        Database = database;
        _list = list;
    }

    public PostgresDatabase Database { get; }

    public string Name => Database.Name;

    /// <summary>How to connect, such as "postgres://postgres@localhost:5432/app".</summary>
    public string ConnectionText => Database.ConnectionText;

    public string DataFolderText => string.Format(Strings.PostgresDatabaseRow_Data, Database.DataFolder);

    /// <summary>Whether Remove is waiting to be confirmed.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingRemove { get; set; }

    public string RemoveText => string.Format(Strings.PostgresDatabaseRow_RemoveItsContainerIsStopped, Name, Database.DataFolder);

    /// <summary>Its service in the databases' compose project, once read.</summary>
    public ComposeServiceRow? Service => _list.ServiceOf(Name);

    public bool IsRunning => Service?.IsRunning == true;

    public bool IsPaused => Service?.IsPaused == true;

    public bool IsStopped => Service?.IsStopped == true;

    /// <summary>Start shows while it has no container or it is stopped, Stop while it is running or paused.</summary>
    public bool CanStart => !IsRunning && !IsPaused;

    /// <summary>The container's state, such as "Up 5 minutes (healthy)", or "Not created".</summary>
    public string StatusText => Service?.Status ?? Strings.ComposeServiceRow_NotCreated;

    private bool CanRun => _list.CanRun;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task StartAsync() => _list.StartAsync(this);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task StopAsync() => _list.StopAsync(this);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Edit() => _list.Edit(this);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Remove() => IsConfirmingRemove = true;

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ConfirmRemoveAsync()
    {
        IsConfirmingRemove = false;
        return _list.RemoveAsync(this);
    }

    internal void BusyChanged()
    {
        EditCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ConfirmRemoveCommand.NotifyCanExecuteChanged();
    }

    internal void ServiceChanged()
    {
        OnPropertyChanged(nameof(Service));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StatusText));
    }
}

/// <summary>
/// The PostgreSQL servers, shown on Data / Database: adding, changing and removing them, and writing
/// them into their own compose project, which is put on the Containers page's list of compose files.
/// Where that project is written, its name, the image and the folder new databases keep their data
/// in are set on Settings / Container Settings.
/// </summary>
public partial class PostgresDatabasesViewModel : ObservableObject
{
    private readonly ContainersViewModel _page;
    private readonly ComposeSettings _settings;
    private readonly CredentialStore _credentials;

    public PostgresDatabasesViewModel(ContainersViewModel page, ComposeSettings settings, CredentialStore credentials)
    {
        _page = page;
        _settings = settings;
        _credentials = credentials;
        foreach (var database in settings.Databases)
            Items.Add(new PostgresDatabaseRow(database, this));
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDatabases));

        _folderText = settings.DatabasesFolder;
        _projectText = settings.DatabasesProject;
        _imageText = settings.PostgresImage;
        _dataRootText = settings.DatabasesDataRoot;
    }

    public ObservableCollection<PostgresDatabaseRow> Items { get; } = [];

    public bool HasDatabases => Items.Count > 0;

    /// <summary>The databases as saved.</summary>
    public IReadOnlyList<PostgresDatabase> Databases => _settings.Databases;

    /// <summary>The folder the project is written to.</summary>
    public string Folder => _settings.EffectiveDatabasesFolder;

    public string ComposeFile => PostgresProject.ComposeFileIn(Folder);

    public string WrittenToText => string.Format(Strings.PostgresDatabases_WrittenTo, ComposeFile);

    /// <summary>The folder new databases keep their data in, each in a folder named after it.</summary>
    public string DataRoot => _settings.EffectiveDatabasesDataRoot;

    public string DataRootInfo => string.Format(Strings.PostgresDatabases_DataIsKeptIn, DataRoot);

    /// <summary>The database being added or changed, shown in place of the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial DatabaseEditorViewModel? Editor { get; set; }

    public bool IsEditing => Editor is not null;

    /// <summary>Why the databases could not be written, removed or saved.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    internal bool CanRun => !_page.IsBusy;

    private bool CanAdd => CanRun && Editor is null;

    /// <summary>The page's group for the databases' compose file, once it is on the list.</summary>
    public ComposeProjectViewModel? Project => _page.Projects.FirstOrDefault(p => SamePath(p.File, ComposeFile));

    /// <summary>Asks for a folder, with this title; null when cancelled. Set by the views, for a data folder and for the settings' folders.</summary>
    public Func<string, Task<string?>>? PickFolder { get; set; }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        Error = null;
        Editor = new DatabaseEditorViewModel(null, this);
    }

    internal void Edit(PostgresDatabaseRow row)
    {
        Error = null;
        Editor = new DatabaseEditorViewModel(row.Database, this);
    }

    internal void CloseEditor() => Editor = null;

    internal bool CanKeepPasswords => _credentials.IsAvailable;

    internal bool HasSavedPassword(PostgresDatabase database) => _credentials.Read(database.Account) is { Length: > 0 };

    /// <summary>The password of a database, from the credential store; null for none.</summary>
    public string? PasswordOf(PostgresDatabase database) => _credentials.Read(database.Account);

    /// <summary>A name no database has yet, made from <paramref name="wanted"/> by adding a number if needed.</summary>
    internal string FreeName(string wanted = "postgres")
    {
        var name = wanted;
        for (var i = 2; Databases.Any(d => d.Name == name); i++)
            name = $"{wanted}-{i}";
        return name;
    }

    /// <summary>The first port from <paramref name="from"/> up that no other database publishes.</summary>
    /// <param name="except">The database being changed, whose own port is free to it.</param>
    internal int FreePort(int from = PostgresDatabase.ContainerPort, string? except = null)
    {
        var taken = Databases.Where(d => d.Id != except).Select(d => d.Port).ToHashSet();
        var port = from;
        while (taken.Contains(port))
            port++;
        return port;
    }

    /// <summary>Whether a compose project is the databases', which is written again before it is started.</summary>
    public bool IsTheirs(ComposeProjectViewModel project) => SamePath(project.File, ComposeFile);

    /// <summary>Writes the databases' compose project and makes their data folders. Returns what went wrong, or null.</summary>
    public string? Write()
    {
        try
        {
            PostgresProject.Write(Folder, _settings.DatabasesProject, _settings.PostgresImage, _settings.Databases, PasswordOf);
            return null;
        }
        catch (FormatException e)
        {
            AppLog.For("Containers").LogWarning("The databases could not be written: {Problem}", e.Message);
            return e.Message;
        }
    }

    /// <summary>
    /// Saves a database that was added or changed, and writes the project again. A database whose
    /// name changed has its old container removed first, and one that was running is started again
    /// so the change takes effect. The data stays where it is.
    /// </summary>
    /// <param name="password">The password typed, or null to keep the one saved.</param>
    internal async Task<string?> SaveAsync(PostgresDatabase database, PostgresDatabase? original, string? password)
    {
        var oldRow = original is null ? null : RowOf(original.Name);
        var wasRunning = oldRow?.IsRunning == true;
        if (original is not null && original.Name != database.Name && oldRow is { HasContainer: true } && Project is { } oldProject
            && !await _page.RunCommandAsync(oldProject, Strings.ComposeProject_Removing, ["rm", "--stop", "--force", original.Name]))
            return oldProject.Error;

        if (password is { Length: > 0 } && !_credentials.Write(database.Account, password))
            return Strings.PostgresDatabases_ThePasswordCouldNotBeKept;

        var at = _settings.Databases.FindIndex(d => d.Id == database.Id);
        if (at < 0)
        {
            _settings.Databases.Add(database);
            Items.Add(new PostgresDatabaseRow(database, this));
        }
        else
        {
            _settings.Databases[at] = database;
            Items[Items.IndexOf(Items.First(r => r.Database.Id == database.Id))] = new PostgresDatabaseRow(database, this);
        }

        _page.SaveSettings();
        AppLog.For("Containers").LogInformation("Saved the database {Database}", database.Name);
        Editor = null;

        Error = Write();
        if (Error is not null)
            return null;

        var project = _page.AddProject(ComposeFile);
        project.ForgetName();
        await project.RefreshStatusAsync();
        if (wasRunning)
            await _page.RunCommandAsync(project, Strings.ComposeProject_Starting, ["up", "-d", database.Name]);
        return null;
    }

    /// <summary>Removes a database, stopping and removing its container first. Its password is forgotten; its data folder is kept.</summary>
    internal async Task RemoveAsync(PostgresDatabaseRow row)
    {
        Error = null;
        var project = Project;
        if (project is not null && RowOf(row.Name) is { HasContainer: true }
            && !await _page.RunCommandAsync(project, Strings.ComposeProject_Removing, ["rm", "--stop", "--force", row.Name]))
        {
            Error = project.Error;
            return;
        }

        _credentials.Delete(row.Database.Account);
        _settings.Databases.RemoveAll(d => d.Id == row.Database.Id);
        Items.Remove(row);
        _page.SaveSettings();
        AppLog.For("Containers").LogInformation("Removed the database {Database}, keeping its data in {Folder}", row.Name, row.Database.DataFolder);

        if (_settings.Databases.Count > 0)
        {
            Error = Write();
            if (project is not null)
                await project.RefreshStatusAsync();
            return;
        }

        // With no databases left there is nothing to show: the file goes, and its group with it.
        if (project is not null)
            _page.RemoveProject(project);
        Error = Delete(Folder);
    }

    private ComposeServiceRow? RowOf(string service) => Project?.Services.FirstOrDefault(s => s.Service == service);

    internal ComposeServiceRow? ServiceOf(string service) => RowOf(service);

    /// <summary>Shows the databases' services again, after their project was read.</summary>
    public void ServicesChanged()
    {
        foreach (var row in Items)
            row.ServiceChanged();
    }

    /// <summary>Starts one database, writing its project first and putting it on the page if it is not there.</summary>
    internal async Task StartAsync(PostgresDatabaseRow row)
    {
        Error = null;
        if (Project is not { } project)
        {
            if (Write() is { } problem)
            {
                Error = problem;
                return;
            }

            project = _page.AddProject(ComposeFile);
        }

        if (!await _page.RunCommandAsync(project, Strings.ComposeProject_Starting, ["up", "-d", row.Name]))
            Error = project.Error;
    }

    internal async Task StopAsync(PostgresDatabaseRow row)
    {
        Error = null;
        if (Project is { } project && !await _page.RunCommandAsync(project, Strings.ComposeProject_Stopping, ["stop", row.Name]))
            Error = project.Error;
    }

    private static string? Delete(string folder)
    {
        try
        {
            PostgresProject.Delete(folder);
            return null;
        }
        catch (FormatException e)
        {
            return e.Message;
        }
    }

    internal void BusyChanged()
    {
        AddCommand.NotifyCanExecuteChanged();
        ApplyLocationCommand.NotifyCanExecuteChanged();
        Editor?.BusyChanged();
        foreach (var row in Items)
            row.BusyChanged();
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    // Settings / Container Settings: where the project is written, what it is called, the image, and
    // where new databases keep their data.

    private string _folderText;

    /// <summary>The folder typed, applied with Apply; empty for the default.</summary>
    public string FolderText
    {
        get => _folderText;
        set
        {
            if (SetProperty(ref _folderText, value))
                LocationChanged();
        }
    }

    private string _projectText;

    /// <summary>The project name typed, applied with Apply.</summary>
    public string ProjectText
    {
        get => _projectText;
        set
        {
            if (SetProperty(ref _projectText, value))
                LocationChanged();
        }
    }

    public string DefaultFolder => PostgresProject.DefaultFolder;

    [ObservableProperty]
    public partial string? LocationError { get; set; }

    /// <summary>Whether the folder or project name typed differs from what is in use.</summary>
    public bool HasLocationChanges => FolderText.Trim() != _settings.DatabasesFolder || ProjectText.Trim() != _settings.DatabasesProject;

    private void LocationChanged()
    {
        LocationError = MosquittoProject.CheckFolder(FolderText) ?? MosquittoProject.CheckProjectName(ProjectText);
        OnPropertyChanged(nameof(HasLocationChanges));
        ApplyLocationCommand.NotifyCanExecuteChanged();
    }

    private bool CanApplyLocation => CanRun && HasLocationChanges;

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (PickFolder is not null && await PickFolder(Strings.PostgresDatabases_ChooseTheComposeFolder) is { } folder)
            FolderText = folder;
    }

    /// <summary>
    /// Moves the databases' project to the folder and name typed. The databases must be down first,
    /// as containers made under the old name would be left behind. Their data stays where it is.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyLocation))]
    private async Task ApplyLocationAsync()
    {
        if ((MosquittoProject.CheckFolder(FolderText) ?? MosquittoProject.CheckProjectName(ProjectText)) is { } problem)
        {
            LocationError = problem;
            return;
        }

        var project = Project;
        if (project is not null)
        {
            await project.RefreshStatusAsync();
            if (project.Services.Any(s => s.HasContainer))
            {
                LocationError = string.Format(Strings.PostgresDatabases_TakeTheDatabasesDownFirst, project.Name);
                return;
            }
        }

        var oldFolder = Folder;
        _settings.DatabasesFolder = FolderText.Trim();
        _settings.DatabasesProject = ProjectText.Trim();
        _page.SaveSettings();
        LocationChanged();
        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(ComposeFile));
        OnPropertyChanged(nameof(WrittenToText));
        AppLog.For("Containers").LogInformation("The databases' project is now {Project} in {Folder}", _settings.DatabasesProject, Folder);

        if (project is not null)
            _page.RemoveProject(project);
        if (!SamePath(oldFolder, Folder))
            LocationError = Delete(oldFolder);
        if (_settings.Databases.Count == 0)
            return;

        LocationError = Write() ?? LocationError;
        var moved = _page.AddProject(ComposeFile);
        await moved.RefreshStatusAsync();
    }

    private string _imageText;

    /// <summary>The image the databases run. Saved once valid, and used the next time they are started.</summary>
    public string ImageText
    {
        get => _imageText;
        set
        {
            if (!SetProperty(ref _imageText, value))
                return;
            ImageError = MosquittoProject.CheckImage(value);
            if (ImageError is null && value.Trim() != _settings.PostgresImage)
            {
                _settings.PostgresImage = value.Trim();
                _page.SaveSettings();
            }
        }
    }

    [ObservableProperty]
    public partial string? ImageError { get; set; }

    public string DefaultDataRoot => PostgresProject.DefaultDataRoot;

    private string _dataRootText;

    /// <summary>The folder new databases keep their data in; empty for the default. Saved once valid.</summary>
    public string DataRootText
    {
        get => _dataRootText;
        set
        {
            if (!SetProperty(ref _dataRootText, value))
                return;
            DataRootError = value.Trim().Length == 0 ? null : PostgresDatabase.CheckDataFolder(value);
            if (DataRootError is null && value.Trim() != _settings.DatabasesDataRoot)
            {
                _settings.DatabasesDataRoot = value.Trim();
                _page.SaveSettings();
                OnPropertyChanged(nameof(DataRoot));
                OnPropertyChanged(nameof(DataRootInfo));
            }
        }
    }

    [ObservableProperty]
    public partial string? DataRootError { get; set; }

    [RelayCommand]
    private async Task BrowseDataRootAsync()
    {
        if (PickFolder is not null && await PickFolder(Strings.PostgresDatabases_ChooseTheDataFolder) is { } folder)
            DataRootText = folder;
    }
}
