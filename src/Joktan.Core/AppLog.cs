using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Joktan.Core;

/// <summary>
/// The application's log. Goes nowhere until telemetry starts, so modules and tests can log without
/// setting anything up. Passwords, topics and message bodies are never logged.
/// </summary>
public static class AppLog
{
    /// <summary>The name of the app on every log.</summary>
    public const string AppName = "joktan";

    private const string CategoryPrefix = "Joktan.";

    private static ILoggerFactory _factory = NullLoggerFactory.Instance;
    private static LoggingSettings _settings = new();

    /// <summary>Where logs go from now on; null for nowhere.</summary>
    public static void Use(ILoggerFactory? factory) => _factory = factory ?? NullLoggerFactory.Instance;

    /// <summary>Which logs are sent from now on.</summary>
    public static void Configure(LoggingSettings settings) => _settings = settings;

    /// <summary>Whether a log of this level is sent.</summary>
    public static bool IsOn(LogLevel level) => _settings.Enabled && level >= _settings.MinimumLevel;

    /// <summary>The logger for a module, such as "Mqtt".</summary>
    public static ILogger For(string module) => _factory.CreateLogger(CategoryPrefix + module);

    /// <summary>The module a log came from, in lowercase, given its logger's category.</summary>
    public static string ModuleOf(string? category)
        => (category is not null && category.StartsWith(CategoryPrefix, System.StringComparison.Ordinal) ? category[CategoryPrefix.Length..] : category ?? "").ToLowerInvariant();
}
