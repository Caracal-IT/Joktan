using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Agent.Rest;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Website.Resources;
using Microsoft.Extensions.Logging;

namespace Joktan.Website.ViewModels;

/// <summary>Settings / Website: whether the website where people answer workflows' questions is up, and where.</summary>
public partial class WebsiteSettingsViewModel : PageViewModel, IDisposable
{
    private readonly WebsiteSettings _settings;
    private readonly Website _site;
    private RestServer? _server;

    /// <param name="usersProblem">Why nobody can sign in, such as the users having no database; null when they can.</param>
    public WebsiteSettingsViewModel(WebsiteSettings settings, Website site, Func<string?>? usersProblem = null)
        : base(Strings.WebsiteSettings_Title, Strings.WebsiteSettings_Subtitle)
    {
        _settings = settings;
        _site = site;
        UsersProblem = usersProblem ?? (() => null);
        _portText = settings.Port.ToString(CultureInfo.InvariantCulture);
        _sessionHoursText = settings.SessionLength.TotalHours.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Starts listening if the website was left on.</summary>
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
            var valid = int.TryParse(value, out var port) && WebsiteSettings.IsValidPort(port);
            PortError = valid ? null : Strings.WebsiteSettings_PortRange;
            if (!valid || port == _settings.Port)
                return;
            _settings.Port = port;
            _settings.Save();
            Apply();
        }
    }

    [ObservableProperty]
    public partial string? PortError { get; private set; }

    private string _sessionHoursText;

    public string SessionHoursText
    {
        get => _sessionHoursText;
        set
        {
            if (!SetProperty(ref _sessionHoursText, value))
                return;
            var valid = int.TryParse(value, out var hours) && WebsiteSettings.IsValidSessionHours(hours);
            SessionHoursError = valid ? null : Strings.WebsiteSettings_SessionHoursRange;
            if (!valid || hours == _settings.SessionHours)
                return;
            _settings.SessionHours = hours;
            _settings.Save();
        }
    }

    [ObservableProperty]
    public partial string? SessionHoursError { get; private set; }

    /// <summary>Why the website is not listening though it should be, or null.</summary>
    [ObservableProperty]
    public partial string? ServerProblem { get; private set; }

    public bool IsListening => _server is not null;

    /// <summary>Where this computer opens the website.</summary>
    public string Url => $"http://localhost:{_server?.Port ?? _settings.Port}";

    /// <summary>Where the website is while it is on, for the page builder's preview; null while it is off.</summary>
    public string? ListeningUrl => IsListening ? Url : null;

    /// <summary>Where other computers open it, while they may.</summary>
    public string? NetworkUrl => IsListening && _settings.FromOtherComputers ? $"http://{HostName()}:{_server!.Port}" : null;

    public bool HasNetworkUrl => NetworkUrl is not null;

    public Func<string?> UsersProblem { get; }

    /// <summary>Why nobody can sign in just now, shown while the website is on.</summary>
    public string? SignInProblem => UsersProblem();

    /// <summary>Opens a web address in the browser. Set by the view.</summary>
    public Func<string, Task>? OpenUrl { get; set; }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (OpenUrl is not null)
            await OpenUrl(Url);
    }

    /// <summary>Shows again whether people can sign in, as when the page is shown.</summary>
    public void Refresh() => OnPropertyChanged(nameof(SignInProblem));

    private static string HostName()
    {
        try
        {
            return Dns.GetHostName();
        }
        catch (SocketException)
        {
            return "localhost";
        }
    }

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
                    request => Dispatcher.UIThread.InvokeAsync(() => _site.AnswerAsync(request)));
                AppLog.For("Website").LogInformation("Listening on port {Port}", _server.Port);
            }
            catch (SocketException e)
            {
                ServerProblem = string.Format(Strings.WebsiteSettings_CouldNotListen, _settings.Port, e.Message);
            }
        }

        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(Url));
        OnPropertyChanged(nameof(ListeningUrl));
        OnPropertyChanged(nameof(NetworkUrl));
        OnPropertyChanged(nameof(HasNetworkUrl));
        OnPropertyChanged(nameof(SignInProblem));
        Changed?.Invoke();
    }

    /// <summary>Raised after the website starts or stops listening.</summary>
    public event Action? Changed;

    public void Dispose()
    {
        _server?.Dispose();
        _server = null;
    }
}
