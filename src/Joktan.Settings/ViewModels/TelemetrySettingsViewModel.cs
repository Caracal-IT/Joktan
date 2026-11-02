using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Settings.Resources;
using Microsoft.Extensions.Logging;

namespace Joktan.Settings.ViewModels;

/// <summary>A level of log to choose from, by its name on the page.</summary>
public sealed record LogLevelChoice(LogLevel Level, string Name);

/// <summary>A way of signing in to choose from, by its name on the page.</summary>
public sealed record ElasticAuthenticationChoice(ElasticAuthentication Authentication, string Name);

/// <summary>
/// Settings / Telemetry: where logs go, and the endpoint Prometheus scrapes. Each change is saved as it is
/// made, once it is valid. API keys and passwords go to the credential store.
/// </summary>
public partial class TelemetrySettingsViewModel : PageViewModel
{
    private readonly TelemetrySettings _settings;
    private readonly CredentialStore _credentials;

    public TelemetrySettingsViewModel(TelemetrySettings settings, CredentialStore credentials)
        : base(Strings.TelemetrySettings_Telemetry, Strings.TelemetrySettings_SettingsTelemetry)
    {
        _settings = settings;
        _credentials = credentials;
        settings.ReadSecret(credentials);
        AppLog.Configure(settings);
        _elasticUrlText = settings.ElasticUrl;
        _dataStreamText = settings.ElasticDataStream;
        _metricsPortText = settings.MetricsPort.ToString();
        _logFolderText = settings.LogFolder;
        _logFileDaysText = settings.LogFileDays.ToString();
        _collectorEndpointText = settings.CollectorEndpoint;
    }

    public IReadOnlyList<LogLevelChoice> Levels { get; } =
    [
        new(LogLevel.Debug, Strings.TelemetrySettings_Debug),
        new(LogLevel.Information, Strings.TelemetrySettings_Information),
        new(LogLevel.Warning, Strings.TelemetrySettings_Warning),
        new(LogLevel.Error, Strings.TelemetrySettings_Error),
    ];

    public IReadOnlyList<ElasticAuthenticationChoice> Authentications { get; } =
    [
        new(ElasticAuthentication.None, Strings.TelemetrySettings_NoLogin),
        new(ElasticAuthentication.ApiKey, Strings.TelemetrySettings_ApiKey),
        new(ElasticAuthentication.Password, Strings.TelemetrySettings_UsernameAndPassword),
    ];

    public bool LogsToFile
    {
        get => _settings.LogsToFile;
        set
        {
            _settings.LogsToFile = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool LogsToElastic
    {
        get => _settings.LogsToElastic;
        set
        {
            _settings.LogsToElastic = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool LogsToCollector
    {
        get => _settings.LogsToCollector;
        set
        {
            _settings.LogsToCollector = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    private string _logFolderText;

    /// <summary>The folder as typed; empty for logs/ in the data folder. It applies once it is valid.</summary>
    public string LogFolderText
    {
        get => _logFolderText;
        set
        {
            if (!SetProperty(ref _logFolderText, value))
                return;
            LogFolderError = TelemetrySettings.IsValidFolder(value) ? null : Strings.TelemetrySettings_EnterAWholePath;
            if (LogFolderError is null)
            {
                _settings.LogFolder = value.Trim();
                _settings.Save();
                OnPropertyChanged(nameof(LogFolderInUse));
            }
        }
    }

    [ObservableProperty]
    public partial string? LogFolderError { get; set; }

    /// <summary>Where log files are going.</summary>
    public string LogFolderInUse => string.Format(Strings.TelemetrySettings_FilesGoTo, _settings.EffectiveLogFolder);

    private string _logFileDaysText;

    public string LogFileDaysText
    {
        get => _logFileDaysText;
        set
        {
            if (!SetProperty(ref _logFileDaysText, value))
                return;
            var valid = int.TryParse(value, out var days) && TelemetrySettings.IsValidDays(days);
            LogFileDaysError = valid ? null : Strings.TelemetrySettings_DaysRule;
            if (valid)
            {
                _settings.LogFileDays = days;
                _settings.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? LogFileDaysError { get; set; }

    private string _collectorEndpointText;

    /// <summary>The collector's address as typed; empty for the usual one. It is used from the next start.</summary>
    public string CollectorEndpointText
    {
        get => _collectorEndpointText;
        set
        {
            if (!SetProperty(ref _collectorEndpointText, value))
                return;
            var valid = value.Trim().Length == 0 || TelemetrySettings.IsValidUrl(value);
            CollectorEndpointError = valid ? null : Strings.TelemetrySettings_EnterAWholeAddress;
            if (valid)
            {
                _settings.CollectorEndpoint = value.Trim();
                _settings.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? CollectorEndpointError { get; set; }

    // The list sets null while it is changed.
    [AllowNull]
    public LogLevelChoice SelectedLevel
    {
        get => Levels.FirstOrDefault(l => l.Level == _settings.MinimumLevel) ?? Levels[1];
        set
        {
            if (value is null)
                return;
            _settings.MinimumLevel = value.Level;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    private string _elasticUrlText;

    public string ElasticUrlText
    {
        get => _elasticUrlText;
        set
        {
            if (!SetProperty(ref _elasticUrlText, value))
                return;
            ElasticUrlError = TelemetrySettings.IsValidUrl(value) ? null : Strings.TelemetrySettings_EnterAWholeAddress;
            if (ElasticUrlError is null)
            {
                _settings.ElasticUrl = value.Trim();
                _settings.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? ElasticUrlError { get; set; }

    // The list sets null while it is changed.
    [AllowNull]
    public ElasticAuthenticationChoice SelectedAuthentication
    {
        get => Authentications.First(a => a.Authentication == _settings.ElasticAuthentication);
        set
        {
            if (value is null)
                return;
            _settings.ElasticAuthentication = value.Authentication;
            _settings.Save();
            _settings.ReadSecret(_credentials);
            OnPropertyChanged();
            OnPropertyChanged(nameof(UsesApiKey));
            OnPropertyChanged(nameof(UsesPassword));
            OnPropertyChanged(nameof(HasSavedSecret));
        }
    }

    public bool UsesApiKey => _settings.ElasticAuthentication == ElasticAuthentication.ApiKey;

    public bool UsesPassword => _settings.ElasticAuthentication == ElasticAuthentication.Password;

    public string ElasticUsername
    {
        get => _settings.ElasticUsername;
        set
        {
            _settings.ElasticUsername = value.Trim();
            _settings.Save();
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
            SecretWarning = _settings.WriteSecret(_credentials, value) ? null : Strings.TelemetrySettings_CouldNotKeepSecret;
            OnPropertyChanged(nameof(HasSavedSecret));
        }
    }

    public bool HasSavedSecret => _settings.Secret.Length > 0;

    [ObservableProperty]
    public partial string? SecretWarning { get; set; }

    private string _dataStreamText;

    public string DataStreamText
    {
        get => _dataStreamText;
        set
        {
            if (!SetProperty(ref _dataStreamText, value))
                return;
            DataStreamError = TelemetrySettings.IsValidDataStream(value) ? null : Strings.TelemetrySettings_DataStreamRule;
            if (DataStreamError is null)
            {
                _settings.ElasticDataStream = value;
                _settings.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? DataStreamError { get; set; }

    /// <summary>How the last test went.</summary>
    [ObservableProperty]
    public partial string? TestResult { get; set; }

    /// <summary>Why the last send failed, if it did.</summary>
    public string? SendProblem => ElasticLogSink.LastProblem is { } problem ? string.Format(Strings.TelemetrySettings_LastSendFailed, problem) : null;

    /// <summary>Why the last write to a log file failed, if it did.</summary>
    public string? FileProblem => FileLogSink.LastProblem is { } problem ? string.Format(Strings.TelemetrySettings_LastWriteFailed, problem) : null;

    [RelayCommand]
    private async Task TestAsync()
    {
        TestResult = Strings.TelemetrySettings_Testing;
        var (reached, text) = await ElasticLogSink.TestAsync(_settings.ElasticUrl, _settings.Authorization(), TestHandler);
        TestResult = (reached, text) switch
        {
            (true, "") => Strings.TelemetrySettings_ReachedNoVersion,
            (true, _) => string.Format(Strings.TelemetrySettings_ReachedVersion, text),
            (false, "401") => Strings.TelemetrySettings_LoginRefused,
            _ => string.Format(Strings.TelemetrySettings_CouldNotReach, text),
        };
        Refresh();
    }

    /// <summary>Brings the problems up to date. Called when the page is shown, and after a test.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(SendProblem));
        OnPropertyChanged(nameof(FileProblem));
        OnPropertyChanged(nameof(MetricsProblem));
    }

    /// <summary>What the test talks through instead of the network, in tests.</summary>
    public HttpMessageHandler? TestHandler { get; init; }

    public bool MetricsEnabled
    {
        get => _settings.MetricsEndpointEnabled;
        set
        {
            _settings.MetricsEndpointEnabled = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    private string _metricsPortText;

    public string MetricsPortText
    {
        get => _metricsPortText;
        set
        {
            if (!SetProperty(ref _metricsPortText, value))
                return;
            var valid = int.TryParse(value, out var port) && TelemetrySettings.IsValidPort(port);
            MetricsPortError = valid ? null : Strings.TelemetrySettings_PortRule;
            if (valid)
            {
                _settings.MetricsPort = port;
                _settings.Save();
                OnPropertyChanged(nameof(MetricsAddress));
            }
        }
    }

    [ObservableProperty]
    public partial string? MetricsPortError { get; set; }

    public bool MetricsFromOtherComputers
    {
        get => _settings.MetricsFromOtherComputers;
        set
        {
            _settings.MetricsFromOtherComputers = value;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetricsAddress));
        }
    }

    /// <summary>Why the endpoint could not start, if it could not.</summary>
    public string? MetricsProblem => AppLog.MetricsProblem is { } problem ? string.Format(Strings.TelemetrySettings_EndpointProblem, problem) : null;

    /// <summary>Where Prometheus finds the metrics.</summary>
    public string MetricsAddress => string.Format(
        _settings.MetricsFromOtherComputers ? Strings.TelemetrySettings_ScrapeFromOthers : Strings.TelemetrySettings_ScrapeAt,
        _settings.MetricsPort);
}
