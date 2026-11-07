using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Certificates;
using Joktan.Containers.Resources;
using Joktan.Core;
using Microsoft.Extensions.Logging;

namespace Joktan.Containers.ViewModels;

/// <summary>How the MQTT client linked to a broker is doing, such as "Connected".</summary>
public sealed record BrokerConnection(string Text, bool IsConnected, bool IsPending = false, bool IsFailed = false);

/// <summary>One broker in the list on Data / MQTT, with its container's state and its MQTT client's.</summary>
public partial class MosquittoBrokerRow : ObservableObject
{
    private readonly MosquittoBrokersViewModel _list;

    public MosquittoBrokerRow(MosquittoBroker broker, MosquittoBrokersViewModel list)
    {
        Broker = broker;
        _list = list;
    }

    public MosquittoBroker Broker { get; }

    public string Name => Broker.Name;

    public string PortsText => Broker.PortsText;

    /// <summary>Who may connect, and the certificate it shows, such as "User alice · Certificate localhost".</summary>
    public string Details => string.Join(" · ", new[]
    {
        Broker.HasLogin ? string.Format(Strings.MosquittoBrokerRow_User, Broker.Username) : Strings.MosquittoBrokerRow_AnyoneCanConnect,
        Broker.UsesTls ? string.Format(Strings.MosquittoBrokerRow_Certificate, _list.Certificates.Find(Broker.ServerCertificateId)?.DisplayName ?? Strings.MosquittoBrokerRow_Missing) : null,
    }.OfType<string>());

    /// <summary>Whether Remove is waiting to be confirmed.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingRemove { get; set; }

    public string RemoveText => string.Format(Strings.MosquittoBrokerRow_RemoveItsContainerIsStopped, Name);

    /// <summary>Its service in the brokers' compose project, once read.</summary>
    public ComposeServiceRow? Service => _list.ServiceOf(Name);

    public bool IsRunning => Service?.IsRunning == true;

    public bool IsPaused => Service?.IsPaused == true;

    public bool IsStopped => Service?.IsStopped == true;

    /// <summary>Start shows while it has no container or it is stopped, Stop while it is running or paused.</summary>
    public bool CanStart => !IsRunning && !IsPaused;

    /// <summary>The container's state, such as "Up 5 minutes (healthy)", or "Not created".</summary>
    public string StatusText => Service?.Status ?? Strings.ComposeServiceRow_NotCreated;

    /// <summary>Its MQTT client's state on Dev Tools / Brokers / MQTT, if it has a client.</summary>
    public BrokerConnection? Connection => _list.ConnectionOf?.Invoke(Broker);

    public string ConnectionText => Connection is { } connection
        ? string.Format(Strings.MosquittoBrokerRow_Mqtt, connection.Text)
        : Strings.MosquittoBrokerRow_NoMqttClient;

    public bool IsConnected => Connection?.IsConnected == true;

    public bool IsConnecting => Connection?.IsPending == true;

    public bool IsConnectionFailed => Connection?.IsFailed == true;

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

    internal void CertificatesChanged() => OnPropertyChanged(nameof(Details));

    internal void ServiceChanged()
    {
        OnPropertyChanged(nameof(Service));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StatusText));
    }

    internal void ConnectionChanged()
    {
        OnPropertyChanged(nameof(Connection));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsConnectionFailed));
    }
}

/// <summary>
/// The Mosquitto brokers, shown on Data / MQTT: adding, changing and removing them, and writing
/// them into their own compose project, which is put on the Containers page's list of compose files. Where
/// that project is written, its name and the image are set on Settings / Container Settings.
/// </summary>
public partial class MosquittoBrokersViewModel : ObservableObject
{
    private readonly ContainersViewModel _page;
    private readonly ComposeSettings _settings;
    private readonly CredentialStore _credentials;

    public MosquittoBrokersViewModel(ContainersViewModel page, ComposeSettings settings, CertificateStore certificates, CredentialStore credentials)
    {
        _page = page;
        _settings = settings;
        _credentials = credentials;
        Certificates = certificates;
        foreach (var broker in settings.Brokers)
            Items.Add(new MosquittoBrokerRow(broker, this));
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBrokers));

        _folderText = settings.BrokersFolder;
        _projectText = settings.BrokersProject;
        _imageText = settings.MosquittoImage;
        certificates.Changed += () =>
        {
            foreach (var row in Items)
                row.CertificatesChanged();
            Editor?.CertificatesChanged();
        };
    }

    public CertificateStore Certificates { get; }

    public ObservableCollection<MosquittoBrokerRow> Items { get; } = [];

    public bool HasBrokers => Items.Count > 0;

    /// <summary>The brokers as saved.</summary>
    public IReadOnlyList<MosquittoBroker> Brokers => _settings.Brokers;

    /// <summary>The folder the project is written to.</summary>
    public string Folder => _settings.EffectiveBrokersFolder;

    public string ComposeFile => MosquittoProject.ComposeFileIn(Folder);

    public string WrittenToText => string.Format(Strings.MosquittoBrokers_WrittenTo, ComposeFile);

    /// <summary>The broker being added or changed, shown in place of the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial BrokerEditorViewModel? Editor { get; set; }

    public bool IsEditing => Editor is not null;

    /// <summary>Why the brokers could not be written, removed or saved.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    internal bool CanRun => !_page.IsBusy;

    private bool CanAdd => CanRun && Editor is null;

    /// <summary>The page's group for the brokers' compose file, once it is on the list.</summary>
    public ComposeProjectViewModel? Project => _page.Projects.FirstOrDefault(p => SamePath(p.File, ComposeFile));

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        Error = null;
        Editor = new BrokerEditorViewModel(null, this);
    }

    internal void Edit(MosquittoBrokerRow row)
    {
        Error = null;
        Editor = new BrokerEditorViewModel(row.Broker, this);
    }

    internal void CloseEditor() => Editor = null;

    internal bool CanKeepPasswords => _credentials.IsAvailable;

    internal bool HasSavedPassword(MosquittoBroker broker) => _credentials.Read(broker.Account) is { Length: > 0 };

    /// <summary>The password of a broker with a login, from the credential store; null for none.</summary>
    public string? PasswordOf(MosquittoBroker broker) => broker.HasLogin ? _credentials.Read(broker.Account) : null;

    /// <summary>A name no broker has yet, made from <paramref name="wanted"/> by adding a number if needed.</summary>
    internal string FreeName(string wanted = "mosquitto")
    {
        var name = wanted;
        for (var i = 2; Brokers.Any(b => b.Name == name); i++)
            name = $"{wanted}-{i}";
        return name;
    }

    /// <summary>The first port from <paramref name="from"/> up that no other broker publishes and is not in <paramref name="alsoTaken"/>.</summary>
    /// <param name="except">The broker being changed, whose own ports are free to it.</param>
    internal int FreePort(int from, string? except = null, IEnumerable<int>? alsoTaken = null)
    {
        var taken = Brokers.Where(b => b.Id != except).SelectMany(b => b.HostPorts).Concat(alsoTaken ?? []).ToHashSet();
        var port = from;
        while (taken.Contains(port))
            port++;
        return port;
    }

    /// <summary>Raised when a broker has been added or changed, for its MQTT client.</summary>
    public event Action<MosquittoBroker>? Saved;

    /// <summary>Raised when a broker has been removed.</summary>
    public event Action<MosquittoBroker>? Removed;

    /// <summary>Whether a compose project is the brokers', which is written again before it is started or built.</summary>
    public bool IsTheirs(ComposeProjectViewModel project) => SamePath(project.File, ComposeFile);

    /// <summary>Writes the brokers' compose project. Returns what went wrong, or null.</summary>
    public string? Write()
    {
        try
        {
            MosquittoProject.Write(Folder, _settings.BrokersProject, _settings.MosquittoImage, _settings.Brokers, PasswordOf, Certificates);
            return null;
        }
        catch (FormatException e)
        {
            AppLog.For("Containers").LogWarning("The brokers could not be written: {Problem}", e.Message);
            return e.Message;
        }
    }

    /// <summary>
    /// Saves a broker that was added or changed, and writes the project again. A broker whose name
    /// changed has its old container removed first, and one that was running is started again so
    /// the change takes effect.
    /// </summary>
    /// <param name="password">The password typed, or null to keep the one saved.</param>
    internal async Task<string?> SaveAsync(MosquittoBroker broker, MosquittoBroker? original, string? password)
    {
        var oldRow = original is null ? null : RowOf(original.Name);
        var wasRunning = oldRow?.IsRunning == true;
        if (original is not null && original.Name != broker.Name && oldRow is { HasContainer: true } && Project is { } oldProject
            && !await _page.RunCommandAsync(oldProject, Strings.ComposeProject_Removing, ["rm", "--stop", "--force", original.Name]))
            return oldProject.Error;

        if (!broker.HasLogin)
            _credentials.Delete(broker.Account);
        else if (password is { Length: > 0 } && !_credentials.Write(broker.Account, password))
            return Strings.MosquittoBrokers_ThePasswordCouldNotBeKept;

        var at = _settings.Brokers.FindIndex(b => b.Id == broker.Id);
        if (at < 0)
        {
            _settings.Brokers.Add(broker);
            Items.Add(new MosquittoBrokerRow(broker, this));
        }
        else
        {
            _settings.Brokers[at] = broker;
            Items[Items.IndexOf(Items.First(r => r.Broker.Id == broker.Id))] = new MosquittoBrokerRow(broker, this);
        }

        _page.SaveSettings();
        AppLog.For("Containers").LogInformation("Saved the broker {Broker}", broker.Name);
        Editor = null;
        Saved?.Invoke(broker);

        Error = Write();
        if (Error is not null)
            return null;

        var project = _page.AddProject(ComposeFile);
        project.ForgetName();
        await project.RefreshStatusAsync();
        if (wasRunning)
            await _page.RunCommandAsync(project, Strings.ComposeProject_Starting, ["up", "-d", broker.Name]);
        return null;
    }

    /// <summary>Removes a broker, stopping and removing its container first. Its password is forgotten.</summary>
    internal async Task RemoveAsync(MosquittoBrokerRow row)
    {
        Error = null;
        var project = Project;
        if (project is not null && RowOf(row.Name) is { HasContainer: true }
            && !await _page.RunCommandAsync(project, Strings.ComposeProject_Removing, ["rm", "--stop", "--force", row.Name]))
        {
            Error = project.Error;
            return;
        }

        if (row.Broker.HasLogin)
            _credentials.Delete(row.Broker.Account);
        _settings.Brokers.RemoveAll(b => b.Id == row.Broker.Id);
        Items.Remove(row);
        _page.SaveSettings();
        AppLog.For("Containers").LogInformation("Removed the broker {Broker}", row.Name);
        Removed?.Invoke(row.Broker);

        if (_settings.Brokers.Count > 0)
        {
            Error = Write();
            if (project is not null)
                await project.RefreshStatusAsync();
            return;
        }

        // With no brokers left there is nothing to show: the file goes, and its group with it.
        if (project is not null)
            _page.RemoveProject(project);
        Error = Delete(Folder);
    }

    private ComposeServiceRow? RowOf(string service) => Project?.Services.FirstOrDefault(s => s.Service == service);

    internal ComposeServiceRow? ServiceOf(string service) => RowOf(service);

    /// <summary>The state of the MQTT client linked to a broker, or null when it has none. Set by the app.</summary>
    public Func<MosquittoBroker, BrokerConnection?>? ConnectionOf { get; set; }

    /// <summary>Shows the brokers' services again, after their project was read.</summary>
    public void ServicesChanged()
    {
        foreach (var row in Items)
            row.ServiceChanged();
    }

    /// <summary>Shows the brokers' MQTT clients' states again, after one changed.</summary>
    public void ConnectionsChanged()
    {
        foreach (var row in Items)
            row.ConnectionChanged();
    }

    /// <summary>Starts one broker, writing its project first and putting it on the page if it is not there.</summary>
    internal async Task StartAsync(MosquittoBrokerRow row)
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

    internal async Task StopAsync(MosquittoBrokerRow row)
    {
        Error = null;
        if (Project is { } project && !await _page.RunCommandAsync(project, Strings.ComposeProject_Stopping, ["stop", row.Name]))
            Error = project.Error;
    }

    private static string? Delete(string folder)
    {
        try
        {
            MosquittoProject.Delete(folder);
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

    // Settings / Container Settings: where the project is written, what it is called, and the image.

    /// <summary>Asks for a folder for the project; null when cancelled. Set by the settings view.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

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

    public string DefaultFolder => MosquittoProject.DefaultFolder;

    [ObservableProperty]
    public partial string? LocationError { get; set; }

    /// <summary>Whether the folder or project name typed differs from what is in use.</summary>
    public bool HasLocationChanges => FolderText.Trim() != _settings.BrokersFolder || ProjectText.Trim() != _settings.BrokersProject;

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
        if (PickFolder is not null && await PickFolder() is { } folder)
            FolderText = folder;
    }

    /// <summary>
    /// Moves the brokers' project to the folder and name typed. The brokers must be down first, as
    /// containers made under the old name would be left behind.
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
                LocationError = string.Format(Strings.MosquittoBrokers_TakeTheBrokersDownFirst, project.Name);
                return;
            }
        }

        var oldFolder = Folder;
        _settings.BrokersFolder = FolderText.Trim();
        _settings.BrokersProject = ProjectText.Trim();
        _page.SaveSettings();
        LocationChanged();
        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(ComposeFile));
        OnPropertyChanged(nameof(WrittenToText));
        AppLog.For("Containers").LogInformation("The brokers' project is now {Project} in {Folder}", _settings.BrokersProject, Folder);

        if (project is not null)
            _page.RemoveProject(project);
        if (!SamePath(oldFolder, Folder))
            LocationError = Delete(oldFolder);
        if (_settings.Brokers.Count == 0)
            return;

        LocationError = Write() ?? LocationError;
        var moved = _page.AddProject(ComposeFile);
        await moved.RefreshStatusAsync();
    }

    private string _imageText;

    /// <summary>The image the brokers are built from. Saved once valid, and used the next time they are started.</summary>
    public string ImageText
    {
        get => _imageText;
        set
        {
            if (!SetProperty(ref _imageText, value))
                return;
            ImageError = MosquittoProject.CheckImage(value);
            if (ImageError is null && value.Trim() != _settings.MosquittoImage)
            {
                _settings.MosquittoImage = value.Trim();
                _page.SaveSettings();
            }
        }
    }

    [ObservableProperty]
    public partial string? ImageError { get; set; }
}
