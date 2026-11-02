using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Settings.Resources;
using Microsoft.Extensions.Logging;

namespace Joktan.Settings.ViewModels;

/// <summary>A level of log to choose from, by its name on the page.</summary>
public sealed record LogLevelChoice(LogLevel Level, string Name);

/// <summary>Settings / Logging: whether logs are sent, which ones, and where to. Each change is saved as it is made.</summary>
public partial class LoggingSettingsViewModel : PageViewModel
{
    private readonly LoggingSettings _settings;

    public LoggingSettingsViewModel(LoggingSettings settings)
        : base(Strings.LoggingSettings_Logging, Strings.LoggingSettings_SettingsLogging)
    {
        _settings = settings;
        _endpointText = settings.Endpoint;
        AppLog.Configure(settings);
    }

    public IReadOnlyList<LogLevelChoice> Levels { get; } =
    [
        new(LogLevel.Debug, Strings.LoggingSettings_Debug),
        new(LogLevel.Information, Strings.LoggingSettings_Information),
        new(LogLevel.Warning, Strings.LoggingSettings_Warning),
        new(LogLevel.Error, Strings.LoggingSettings_Error),
    ];

    public bool Enabled
    {
        get => _settings.Enabled;
        set
        {
            _settings.Enabled = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

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

    private string _endpointText;

    /// <summary>The endpoint as typed. It is saved once it is valid, and used from the next start.</summary>
    public string EndpointText
    {
        get => _endpointText;
        set
        {
            if (!SetProperty(ref _endpointText, value))
                return;
            EndpointError = LoggingSettings.IsValidEndpoint(value) ? null : Strings.LoggingSettings_EnterAWholeAddress;
            if (EndpointError is null)
            {
                _settings.Endpoint = value.Trim();
                _settings.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? EndpointError { get; set; }
}
