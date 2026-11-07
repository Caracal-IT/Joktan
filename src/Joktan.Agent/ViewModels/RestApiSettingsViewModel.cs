using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Agent.Mcp;
using Joktan.Agent.Resources;
using Joktan.Agent.Rest;
using Joktan.Api.ViewModels;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Workflows.Engine;
using Microsoft.Extensions.Logging;

namespace Joktan.Agent.ViewModels;

/// <summary>Settings / REST API: whether the app answers REST calls, the keys that may make them, and which calls answer.</summary>
public partial class RestApiSettingsViewModel : PageViewModel, IDisposable
{
    private readonly RestApiSettings _settings;
    private readonly RestApi _api;
    private readonly Func<DateOnly> _today;
    private readonly ApiClientViewModel? _apiClient;
    private RestServer? _server;
    private IReadOnlyList<RestCategoryViewModel>? _categories;

    /// <param name="tools">The MCP server's tools, made when the calls are first needed.</param>
    /// <param name="apiClient">Dev Tools / API Client, which the calls can be added to as requests; null for none.</param>
    /// <param name="today">The date in the computer's own time zone; the clock's when null.</param>
    public RestApiSettingsViewModel(RestApiSettings settings, Func<IReadOnlyList<McpTool>> tools, ApiClientViewModel? apiClient = null, Func<DateOnly>? today = null)
        : base(Strings.RestApiSettings_RestApi, Strings.RestApiSettings_SettingsRestApi)
    {
        _settings = settings;
        _apiClient = apiClient;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        _api = new RestApi(settings, tools, _today);
        _portText = settings.Port.ToString(CultureInfo.InvariantCulture);
        Keys = new ObservableCollection<ApiKeyViewModel>(settings.Keys.Select(Row));
        Keys.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoKeys));
    }

    /// <summary>The workflows' webhook triggers, which the server answers at /hooks. Set by the app.</summary>
    public WebhookTriggers? Webhooks
    {
        get => _api.Webhooks;
        set => _api.Webhooks = value;
    }

    /// <summary>Where the server listens on this computer, such as http://localhost:47821, while it is on; null otherwise.</summary>
    public string? ListeningUrl => IsListening ? $"http://localhost:{ListeningPort}" : null;

    /// <summary>Starts listening if the API was left on, once the pages the calls act on exist.</summary>
    public void Start() => Apply();

    public bool Enabled
    {
        get => _settings.Enabled;
        set
        {
            if (value == _settings.Enabled)
                return;
            _settings.Enabled = value;
            _settings.Save();
            Apply();
            OnPropertyChanged();
        }
    }

    /// <summary>Whether Swagger UI and the OpenAPI document are served, without a key.</summary>
    public bool SwaggerEnabled
    {
        get => _settings.SwaggerEnabled;
        set
        {
            if (value == _settings.SwaggerEnabled)
                return;
            _settings.SwaggerEnabled = value;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsDocs));
        }
    }

    public string DocsUrl => $"http://localhost:{_settings.Port}{SwaggerUi.Path}";

    public bool ShowsDocs => IsListening && SwaggerEnabled;

    /// <summary>Says so if this build came without Swagger UI.</summary>
    public string? SwaggerProblem => SwaggerUi.IsAvailable ? null : Strings.RestApiSettings_ThisCopyOfJoktanCameWithoutSwagger;

    /// <summary>Opens a web address in the browser. Set by the view.</summary>
    public Func<string, Task>? OpenUrl { get; set; }

    [RelayCommand]
    private async Task OpenDocsAsync()
    {
        if (OpenUrl is not null)
            await OpenUrl(DocsUrl);
    }

    public bool FromOtherComputers
    {
        get => _settings.FromOtherComputers;
        set
        {
            if (value == _settings.FromOtherComputers)
                return;
            _settings.FromOtherComputers = value;
            _settings.Save();
            Apply();
            OnPropertyChanged();
        }
    }

    private string _portText;

    public string PortText
    {
        get => _portText;
        set
        {
            if (!SetProperty(ref _portText, value))
                return;
            var valid = int.TryParse(value, out var port) && RestApiSettings.IsValidPort(port);
            PortError = valid ? null : Strings.RestApiSettings_GiveAPortFrom1024To;
            if (!valid || port == _settings.Port)
                return;
            _settings.Port = port;
            _settings.Save();
            Apply();
            OnPropertyChanged(nameof(Url));
            OnPropertyChanged(nameof(DocsUrl));
        }
    }

    [ObservableProperty]
    public partial string? PortError { get; private set; }

    /// <summary>Why the server is not listening though it should be, or null.</summary>
    [ObservableProperty]
    public partial string? ServerProblem { get; private set; }

    public bool IsListening => _server is not null;

    /// <summary>The port the server is listening on; 0 while it is not.</summary>
    public int ListeningPort => _server?.Port ?? 0;

    public string Url => $"http://localhost:{_settings.Port}{RestApi.Root}";

    public ObservableCollection<ApiKeyViewModel> Keys { get; }

    public bool HasNoKeys => Keys.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewKeyProblem))]
    [NotifyCanExecuteChangedFor(nameof(AddKeyCommand))]
    public partial string NewKeyName { get; set; } = "";

    /// <summary>The last day the new key works on; null for one that never expires.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewKeyProblem))]
    [NotifyCanExecuteChangedFor(nameof(AddKeyCommand))]
    public partial DateTime? NewKeyExpires { get; set; }

    /// <summary>The first day a new key can be made to expire on.</summary>
    public DateTime FirstExpiryDate => _today().ToDateTime(TimeOnly.MinValue);

    /// <summary>Why the new key cannot be made, or null.</summary>
    public string? NewKeyProblem
        => IsNameTaken ? string.Format(Strings.RestApiSettings_ThereIsAlreadyAKeyCalled, NewKeyName.Trim())
            : HasExpiryPassed ? Strings.RestApiSettings_ThatDayHasPassed
            : null;

    private bool IsNameTaken => Keys.Any(k => k.Name.Equals(NewKeyName.Trim(), StringComparison.OrdinalIgnoreCase));

    private bool HasExpiryPassed => NewKeyExpires is { } date && DateOnly.FromDateTime(date) < _today();

    private bool CanAddKey => NewKeyName.Trim().Length > 0 && NewKeyProblem is null;

    /// <summary>The key just made, shown until it is dismissed, and never again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewSecret))]
    public partial string? NewSecret { get; private set; }

    public bool HasNewSecret => NewSecret is not null;

    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>Puts text on the clipboard. Set by the view.</summary>
    public Func<string, Task>? CopyText { get; set; }

    /// <summary>Narrows the calls listed to those whose name or description holds it.</summary>
    [ObservableProperty]
    public partial string CallFilter { get; set; } = "";

    /// <summary>The categories with calls listed, after <see cref="CallFilter"/>. The tools are made the first time this is read.</summary>
    public ObservableCollection<RestCategoryViewModel> Categories
    {
        get
        {
            if (_categories is null)
            {
                _categories = _api.Calls
                    .GroupBy(t => t.Category)
                    .Select(g => new RestCategoryViewModel(g.Key, g.Select(t => new RestCallViewModel(t, _settings)).ToList()))
                    .ToList();
                _shownCategories = new ObservableCollection<RestCategoryViewModel>(_categories);
            }

            return _shownCategories!;
        }
    }

    private ObservableCollection<RestCategoryViewModel>? _shownCategories;

    /// <summary>The calls listed, after <see cref="CallFilter"/>, in the order of the tree.</summary>
    public IReadOnlyList<RestCallViewModel> Calls => Categories.SelectMany(c => c.Calls).ToList();

    public bool HasNoCallsListed => Categories.Count == 0;

    partial void OnCallFilterChanged(string value)
    {
        var filter = value.Trim();
        var shown = Categories;
        shown.Clear();
        foreach (var category in _categories!)
        {
            if (!category.Filter(filter))
                continue;
            // What a filter finds is opened, so that it shows.
            category.IsExpanded = filter.Length > 0 || category.IsExpanded;
            shown.Add(category);
        }

        OnPropertyChanged(nameof(HasNoCallsListed));
    }

    /// <summary>What happened when the calls were added to the API Client.</summary>
    [ObservableProperty]
    public partial string? ApiClientMessage { get; private set; }

    public bool CanAddToApiClient => _apiClient is not null;

    /// <summary>Adds every call, on or off, to Dev Tools / API Client, keeping the requests that are there already.</summary>
    [RelayCommand]
    private void AddToApiClient()
    {
        if (_apiClient is null)
            return;
        var (added, existing) = _apiClient.AddRequests(ApiClientExport.Requests(_api.Calls), ApiClientExport.Environment, ApiClientExport.Variables(_settings.Port));
        ApiClientMessage = _apiClient.Error is { } problem
            ? string.Format(Strings.RestApiSettings_CouldNotAddThemToThe, problem)
            : string.Format(Strings.RestApiSettings_AddedRequestsToTheApiClient, added, ApiClientExport.Folder, existing, ApiClientExport.Environment, ApiClientExport.KeyVariable);
    }

    [RelayCommand(CanExecute = nameof(CanAddKey))]
    private void AddKey()
    {
        var expires = NewKeyExpires is { } date ? DateOnly.FromDateTime(date) : (DateOnly?)null;
        var (key, secret) = ApiKey.Create(NewKeyName.Trim(), expires, DateTimeOffset.Now);
        _settings.Keys.Add(key);
        _settings.Save();
        Keys.Add(Row(key));
        NewSecret = secret;
        Message = null;
        NewKeyName = "";
        NewKeyExpires = null;
    }

    [RelayCommand]
    private void RemoveKey(ApiKeyViewModel row)
    {
        _settings.Keys.Remove(row.Key);
        _settings.Save();
        Keys.Remove(row);
        OnPropertyChanged(nameof(NewKeyProblem));
        AddKeyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CopySecretAsync()
    {
        if (NewSecret is not { } secret || CopyText is null)
            return;
        await CopyText(secret);
        Message = Strings.RestApiSettings_CopiedKeepItSomewhereSafe;
    }

    [RelayCommand]
    private void DismissSecret()
    {
        NewSecret = null;
        Message = null;
    }

    /// <summary>Switches on every call listed, after the filter.</summary>
    [RelayCommand]
    private void TurnListedCallsOn() => TurnListedCalls(true);

    [RelayCommand]
    private void TurnListedCallsOff() => TurnListedCalls(false);

    private void TurnListedCalls(bool on)
    {
        foreach (var call in Calls)
            call.IsOn = on;
    }

    private ApiKeyViewModel Row(ApiKey key) => new(key, _settings.Save, _today);

    /// <summary>Listens, or stops, as the settings now say.</summary>
    private void Apply()
    {
        _server?.Dispose();
        _server = null;
        ServerProblem = null;
        if (_settings.Enabled)
        {
            try
            {
                _server = new RestServer(_settings.FromOtherComputers, _settings.Port,
                    request => Dispatcher.UIThread.InvokeAsync(() => _api.AnswerAsync(request)));
                AppLog.For("REST API").LogInformation("Listening on port {Port}", _server.Port);
            }
            catch (SocketException e)
            {
                ServerProblem = string.Format(Strings.RestApiSettings_CouldNotListenOnPort, _settings.Port, e.Message);
            }
        }

        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(ListeningPort));
        OnPropertyChanged(nameof(ShowsDocs));
    }

    public void Dispose()
    {
        _server?.Dispose();
        _server = null;
    }
}
