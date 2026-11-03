using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Logs.Resources;

namespace Joktan.Logs.ViewModels;

/// <summary>A way of signing in to choose from, by its name on the page.</summary>
public sealed record SignInChoice(ElasticAuthentication Authentication, string Name);

/// <summary>
/// Settings / Log Sources: where the Errors page reads logs from, and where each app's code is. Each change is saved as it
/// is made, once it is valid. API keys and passwords go to the credential store.
/// </summary>
public partial class LogSourcesViewModel : PageViewModel
{
    private readonly LogSourceSettings _settings;
    private readonly CredentialStore _credentials;

    public LogSourcesViewModel(LogSourceSettings settings, TelemetrySettings telemetry, CredentialStore credentials)
        : base(Strings.LogSources_LogSources, Strings.LogSources_SettingsLogSources)
    {
        _settings = settings;
        _credentials = credentials;
        Telemetry = telemetry;
        settings.ReadSecrets(credentials);
        foreach (var source in settings.Sources)
            Sources.Add(new LogSourceViewModel(source, this));
    }

    public LogSourceSettings Settings => _settings;

    public TelemetrySettings Telemetry { get; }

    public ObservableCollection<LogSourceViewModel> Sources { get; } = [];

    public IReadOnlyList<SignInChoice> SignIns { get; } =
    [
        new(ElasticAuthentication.None, Strings.LogSources_NoLogin),
        new(ElasticAuthentication.ApiKey, Strings.LogSources_ApiKey),
        new(ElasticAuthentication.Password, Strings.LogSources_UsernameAndPassword),
    ];

    /// <summary>Raised when a source is added, removed or renamed, or its folders change, for the Errors page.</summary>
    public event Action? Changed;

    /// <summary>Asks for a folder. Set by the view.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>What a test talks through instead of the network, in tests.</summary>
    public HttpMessageHandler? TestHandler { get; init; }

    [RelayCommand]
    private void AddElastic() => Add(new LogSource { Kind = LogSourceKind.Elastic, Name = FreeName(Strings.LogSources_NewElastic) });

    [RelayCommand]
    private void AddFiles() => Add(new LogSource { Kind = LogSourceKind.Files, Name = FreeName(Strings.LogSources_NewFiles) });

    private void Add(LogSource source)
    {
        _settings.Sources.Add(source);
        Sources.Add(new LogSourceViewModel(source, this));
        Save();
    }

    internal void Remove(LogSourceViewModel source)
    {
        if (source.Source.IsBuiltIn)
            return;
        LogSourceSettings.WriteSecret(_credentials, source.Source, "");
        _settings.Sources.Remove(source.Source);
        Sources.Remove(source);
        Save();
    }

    internal bool KeepSecret(LogSource source, string secret) => LogSourceSettings.WriteSecret(_credentials, source, secret);

    internal void ReadSecret(LogSource source) => source.Secret = _credentials.Read(source.Id) ?? "";

    internal bool IsNameFree(string name, LogSource except)
        => _settings.Sources.All(s => s == except || !string.Equals(s.DisplayName, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private string FreeName(string name)
    {
        var candidate = name;
        for (var i = 2; _settings.Sources.Any(s => string.Equals(s.DisplayName, candidate, StringComparison.OrdinalIgnoreCase)); i++)
            candidate = $"{name} {i}";
        return candidate;
    }

    internal void Save()
    {
        _settings.Save();
        Changed?.Invoke();
    }

    /// <summary>Shows a source's folders again, after they were changed from the Errors page.</summary>
    internal void Reload(LogSource source) => Sources.FirstOrDefault(s => s.Source == source)?.ReloadFolders();
}

/// <summary>One source on Settings / Log Sources.</summary>
public partial class LogSourceViewModel : ObservableObject
{
    private readonly LogSourcesViewModel _page;

    public LogSourceViewModel(LogSource source, LogSourcesViewModel page)
    {
        Source = source;
        _page = page;
        _nameText = source.DisplayName;
        _urlText = source.Url;
        _indexText = source.Index;
        _pathText = source.Path;
        _filterText = source.Filter;
        foreach (var folder in source.Folders)
            Folders.Add(new AppFolderViewModel(folder, this));
    }

    public LogSource Source { get; }

    public IReadOnlyList<SignInChoice> SignIns => _page.SignIns;

    public bool IsBuiltIn => Source.IsBuiltIn;

    public bool CanRename => !Source.IsBuiltIn;

    public bool IsJoktanElastic => Source.Kind == LogSourceKind.JoktanElastic;

    public bool IsJoktanFiles => Source.Kind == LogSourceKind.JoktanFiles;

    public bool IsOtherElastic => Source.Kind == LogSourceKind.Elastic;

    public bool IsOtherFiles => Source.Kind == LogSourceKind.Files;

    public bool IsElastic => Source.IsElastic;

    public string KindText => Source.Kind switch
    {
        LogSourceKind.JoktanElastic or LogSourceKind.Elastic => Strings.LogSources_Elasticsearch,
        _ => Strings.LogSources_LogFiles,
    };

    /// <summary>Where Joktan's own logs are read from, which Settings / Telemetry decides.</summary>
    public string JoktanText => Source.Kind == LogSourceKind.JoktanElastic
        ? string.Format(Strings.LogSources_JoktanElasticFrom, _page.Telemetry.ElasticUrl)
        : string.Format(Strings.LogSources_JoktanFilesFrom, AppLog.AppName + "-*.log", _page.Telemetry.EffectiveLogFolder);

    public ObservableCollection<AppFolderViewModel> Folders { get; } = [];

    private string _nameText;

    public string NameText
    {
        get => _nameText;
        set
        {
            if (!SetProperty(ref _nameText, value) || Source.IsBuiltIn)
                return;
            NameError = value.Trim().Length == 0 ? Strings.LogSources_NameNeeded
                : !_page.IsNameFree(value, Source) ? Strings.LogSources_NameInUse
                : null;
            if (NameError is null)
            {
                Source.Name = value.Trim();
                _page.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? NameError { get; set; }

    private string _urlText;

    public string UrlText
    {
        get => _urlText;
        set
        {
            if (!SetProperty(ref _urlText, value))
                return;
            UrlError = TelemetrySettings.IsValidUrl(value) ? null : Strings.LogSources_EnterAWholeAddress;
            if (UrlError is null)
            {
                Source.Url = value.Trim();
                _page.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? UrlError { get; set; }

    // The list sets null while it is changed.
    [AllowNull]
    public SignInChoice SelectedSignIn
    {
        get => SignIns.First(s => s.Authentication == Source.Authentication);
        set
        {
            if (value is null)
                return;
            Source.Authentication = value.Authentication;
            _page.Save();
            _page.ReadSecret(Source);
            OnPropertyChanged();
            OnPropertyChanged(nameof(UsesApiKey));
            OnPropertyChanged(nameof(UsesPassword));
            OnPropertyChanged(nameof(HasSavedSecret));
        }
    }

    public bool UsesApiKey => Source.Kind == LogSourceKind.Elastic && Source.Authentication == ElasticAuthentication.ApiKey;

    public bool UsesPassword => Source.Kind == LogSourceKind.Elastic && Source.Authentication == ElasticAuthentication.Password;

    public string Username
    {
        get => Source.Username;
        set
        {
            Source.Username = value.Trim();
            _page.Save();
            OnPropertyChanged();
        }
    }

    private string _secretText = "";

    /// <summary>A new API key or password, as typed. What is saved is never shown.</summary>
    public string SecretText
    {
        get => _secretText;
        set
        {
            if (!SetProperty(ref _secretText, value) || value.Length == 0)
                return;
            SecretWarning = _page.KeepSecret(Source, value) ? null : Strings.LogSources_CouldNotKeepSecret;
            OnPropertyChanged(nameof(HasSavedSecret));
        }
    }

    public bool HasSavedSecret => Source.Secret.Length > 0;

    [ObservableProperty]
    public partial string? SecretWarning { get; set; }

    /// <summary>Forgets the read key, so Joktan's Elasticsearch is read with the key on Settings / Telemetry.</summary>
    [RelayCommand]
    private void ForgetSecret()
    {
        _page.KeepSecret(Source, "");
        SecretText = "";
        SecretWarning = null;
        OnPropertyChanged(nameof(HasSavedSecret));
    }

    private string _indexText;

    public string IndexText
    {
        get => _indexText;
        set
        {
            if (!SetProperty(ref _indexText, value))
                return;
            IndexError = LogSource.IsValidIndex(value) ? null : Strings.LogSources_IndexRule;
            if (IndexError is null)
            {
                Source.Index = value.Trim();
                _page.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? IndexError { get; set; }

    private string _pathText;

    public string PathText
    {
        get => _pathText;
        set
        {
            if (!SetProperty(ref _pathText, value))
                return;
            PathError = LogSource.IsValidPath(value) ? null : Strings.LogSources_EnterAWholePath;
            if (PathError is null)
            {
                Source.Path = value.Trim();
                _page.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? PathError { get; set; }

    private string _filterText;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetProperty(ref _filterText, value))
                return;
            FilterError = LogSource.IsValidFilter(value) ? null : Strings.LogSources_FilterRule;
            if (FilterError is null)
            {
                Source.Filter = value.Trim();
                _page.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? FilterError { get; set; }

    public bool Subfolders
    {
        get => Source.Subfolders;
        set
        {
            Source.Subfolders = value;
            _page.Save();
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private async Task BrowsePathAsync()
    {
        if (_page.PickFolder is { } pick && await pick() is { } folder)
            PathText = folder;
    }

    [RelayCommand]
    private void AddFolder()
    {
        var folder = new AppFolder();
        Source.Folders.Add(folder);
        Folders.Add(new AppFolderViewModel(folder, this));
        _page.Save();
    }

    internal void ReloadFolders()
    {
        Folders.Clear();
        foreach (var folder in Source.Folders)
            Folders.Add(new AppFolderViewModel(folder, this));
    }

    internal void RemoveFolder(AppFolderViewModel folder)
    {
        Source.Folders.Remove(folder.Folder);
        Folders.Remove(folder);
        _page.Save();
    }

    internal void Save() => _page.Save();

    internal Func<Task<string?>>? PickFolder => _page.PickFolder;

    [RelayCommand]
    private void Remove() => _page.Remove(this);

    /// <summary>How the last test went.</summary>
    [ObservableProperty]
    public partial string? TestResult { get; set; }

    /// <summary>Reads the last day's logs, at any level, to show that the source can be read.</summary>
    [RelayCommand]
    private async Task TestAsync()
    {
        TestResult = Strings.LogSources_Testing;
        var now = DateTimeOffset.Now;
        var levels = Enum.GetValues<Microsoft.Extensions.Logging.LogLevel>().ToHashSet();
        var result = await Source.Reader(_page.Telemetry, _page.TestHandler).ReadAsync(new LogQuery(now.AddDays(-1), now, levels, Limit: 1));
        TestResult = result.Problem is { } problem && result.Entries.Count == 0
            ? string.Format(Strings.LogSources_TestFailed, problem)
            : result.Entries.Count == 0 ? Strings.LogSources_TestNothing : Strings.LogSources_TestWorked;
    }
}

/// <summary>Where one app's code is, on Settings / Log Sources.</summary>
public partial class AppFolderViewModel : ObservableObject
{
    private readonly LogSourceViewModel _source;

    public AppFolderViewModel(AppFolder folder, LogSourceViewModel source)
    {
        Folder = folder;
        _source = source;
        _folderText = folder.Folder;
    }

    public AppFolder Folder { get; }

    public string App
    {
        get => Folder.App;
        set
        {
            Folder.App = value.Trim();
            _source.Save();
            OnPropertyChanged();
        }
    }

    private string _folderText;

    public string FolderText
    {
        get => _folderText;
        set
        {
            if (!SetProperty(ref _folderText, value))
                return;
            FolderError = value.Trim().Length == 0 || LogSource.IsValidPath(value) ? null : Strings.LogSources_EnterAWholePath;
            if (FolderError is null)
            {
                Folder.Folder = value.Trim();
                _source.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? FolderError { get; set; }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (_source.PickFolder is { } pick && await pick() is { } folder)
            FolderText = folder;
    }

    [RelayCommand]
    private void Remove() => _source.RemoveFolder(this);
}
