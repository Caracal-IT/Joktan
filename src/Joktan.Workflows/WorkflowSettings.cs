using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joktan.Core;

namespace Joktan.Workflows;

/// <summary>How workflows run, set on Settings / Workflow Settings, kept in <c>settings/workflows.json</c>.</summary>
public sealed class WorkflowSettings
{
    public const int MinRunning = 1;
    public const int MaxRunningLimit = 100;
    public const int MinSeconds = 1;
    public const int MaxSeconds = 24 * 60 * 60;
    public const int MinKept = 10;
    public const int MaxKept = 10_000;

    public static string DefaultFolder => Path.Combine(DataFolder.For("workflows"), "definitions");

    /// <summary>Where the <c>.workflow.json</c> files are; empty for the default.</summary>
    public string Folder { get; set; } = "";

    /// <summary>How many runs may be under way at once; more wait their turn.</summary>
    public int MaxRunning { get; set; } = 10;

    /// <summary>How long a short workflow may take before it is stopped.</summary>
    public int ShortTimeLimitSeconds { get; set; } = 60;

    /// <summary>How long a C# or Go script may run.</summary>
    public int ScriptTimeLimitSeconds { get; set; } = 30;

    public const int MinSteps = 10;
    public const int MaxStepsLimit = 10_000_000;

    /// <summary>The most steps one run may take, so that a loop with no way out fails rather than running for ever.</summary>
    public int MaxSteps { get; set; } = 100_000;

    /// <summary>How deep workflows may call workflows that call workflows.</summary>
    public const int MaxCallDepth = 10;

    /// <summary>How many finished runs are kept to look back on.</summary>
    public int KeepRuns { get; set; } = 200;

    /// <summary>Whether long workflows that were under way carry on by themselves when Joktan starts; otherwise they wait, paused.</summary>
    public bool ResumeLongRuns { get; set; } = true;

    /// <summary>Whether MQTT messages start the workflows that ask for them.</summary>
    public bool MqttTriggers { get; set; } = true;

    /// <summary>Whether workflows start on their schedules.</summary>
    public bool ScheduleTriggers { get; set; } = true;

    public const string NuGetOrg = "https://api.nuget.org/v3-flatcontainer/";

    /// <summary>Where C# scripts' NuGet packages come from, in order: feeds' package base addresses, or folders of .nupkg files.</summary>
    public List<string> NuGetFeeds { get; set; } = [NuGetOrg];

    /// <summary>The one feed of settings saved before there could be several; read, and made the first.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NuGetFeed { get; set; }

    /// <summary>The folder scripts work in, unless a step names its own; empty for none.</summary>
    public string WorkingFolder { get; set; } = "";

    /// <summary>Where libraries are kept once fetched, so each is fetched once.</summary>
    public static string LibrariesFolder => Path.Combine(DataFolder.For("workflows"), "libraries");

    /// <summary>Namespaces every C# script uses, as if each began with their using lines.</summary>
    public List<string> CSharpImports { get; set; } = [];

    /// <summary>Libraries every C# script has, as a step lists its own: NuGet packages or .dll files.</summary>
    public List<string> CSharpLibraries { get; set; } = [];

    /// <summary>Script files or addresses run before every JavaScript script.</summary>
    public List<string> JavaScriptLibraries { get; set; } = [];

    /// <summary>Modules every Go program can import.</summary>
    public List<string> GoLibraries { get; set; } = [];

    /// <summary>Databases that workflows reach, besides those on Data / Database; their passwords are in the credential store.</summary>
    public List<DatabaseConnectionSettings> DatabaseConnections { get; set; } = [];

    /// <summary>How wide the designer's side panels are, and whether each is collapsed into its tabs.</summary>
    public double DesignerLeftWidth { get; set; } = 260;

    public double DesignerRightWidth { get; set; } = 380;

    public bool DesignerLeftCollapsed { get; set; }

    public bool DesignerRightCollapsed { get; set; }

    /// <summary>The go command, for Go scripts.</summary>
    public string GoCommand { get; set; } = "go";

    /// <summary>The go command to run, which is go when none is set.</summary>
    /// <summary>The feeds to use, the blank lines of the setting left out; nuget.org when there are none.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Feeds => NuGetFeeds.Select(f => f.Trim()).Where(f => f.Length > 0).ToList() is { Count: > 0 } feeds ? feeds : [NuGetOrg];

    [JsonIgnore]
    public string Go => GoCommand.Trim() is { Length: > 0 } command ? command : "go";

    [JsonIgnore]
    public string EffectiveFolder => Folder.Trim() is { Length: > 0 } folder ? folder : DefaultFolder;

    public static string RunsFolder => Path.Combine(DataFolder.For("workflows"), "runs");

    /// <summary>Where Go scripts are built, by the hash of their code.</summary>
    public static string GoBuildFolder => Path.Combine(DataFolder.For("workflows"), "go");

    private static string FilePath => Path.Combine(DataFolder.For("settings"), "workflows.json");

    public static WorkflowSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<WorkflowSettings>(File.ReadAllText(FilePath)) is { } settings)
            {
                settings.MaxRunning = Math.Clamp(settings.MaxRunning, MinRunning, MaxRunningLimit);
                settings.ShortTimeLimitSeconds = Math.Clamp(settings.ShortTimeLimitSeconds, MinSeconds, MaxSeconds);
                settings.ScriptTimeLimitSeconds = Math.Clamp(settings.ScriptTimeLimitSeconds, MinSeconds, MaxSeconds);
                settings.KeepRuns = Math.Clamp(settings.KeepRuns, MinKept, MaxKept);
                settings.MaxSteps = Math.Clamp(settings.MaxSteps, MinSteps, MaxStepsLimit);
                settings.Folder ??= "";
                settings.CSharpImports ??= [];
                settings.CSharpLibraries ??= [];
                settings.JavaScriptLibraries ??= [];
                settings.GoLibraries ??= [];
                settings.DatabaseConnections = settings.DatabaseConnections?.Where(c => c is not null).ToList() ?? [];
                settings.NuGetFeeds ??= [];
                if (settings.NuGetFeed is { } old)
                {
                    if (old.Trim().Length > 0 && !settings.NuGetFeeds.Contains(old.Trim()))
                        settings.NuGetFeeds.Insert(0, old.Trim());
                    settings.NuGetFeed = null;
                }

                settings.WorkingFolder ??= "";
                settings.GoCommand = string.IsNullOrWhiteSpace(settings.GoCommand) ? "go" : settings.GoCommand;
                return settings;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings leave the defaults.
        }

        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder.For("settings"));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings that cannot be saved are used until the app closes.
        }
    }
}
