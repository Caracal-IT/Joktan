using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Joktan.Core;

namespace Joktan.TopicDashboards;

/// <summary>What a graph draws its values as.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GraphKind>))]
public enum GraphKind
{
    Line,
    Area,
    Bars,
    Gauge,
    Value,
}

/// <summary>One number a graph draws.</summary>
public sealed class GraphValue
{
    /// <summary>The topic it arrives on.</summary>
    public string Topic { get; set; } = "";

    /// <summary>Where the number is in the payload, as in the topic explorer; empty for the whole payload.</summary>
    public string Path { get; set; } = "";

    /// <summary>The name to show for it; empty to show <see cref="Path"/>.</summary>
    public string Label { get; set; } = "";

    /// <summary>Which of the <see cref="GraphColors"/> it is drawn in.</summary>
    public int Color { get; set; }
}

/// <summary>How the values of a key are shown.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<KeyShow>))]
public enum KeyShow
{
    Line,
    Graph,
    Tab,
    Only,
}

/// <summary>Something that tells a graph's numbers apart, and how its values are shown.</summary>
public sealed class GraphKey
{
    public string Name { get; set; } = "";

    /// <summary>The field of the message it comes from; empty when it comes from the topic or a path.</summary>
    public string Field { get; set; } = "";

    public KeyShow Show { get; set; } = KeyShow.Line;

    /// <summary>The one value kept, when <see cref="Show"/> is <see cref="KeyShow.Only"/>.</summary>
    public string Only { get; set; } = "";
}

/// <summary>One graph on a dashboard: what it subscribes to, what it draws, and how it is laid out.</summary>
public sealed class GraphProfile
{
    public const int MaxValues = 8;
    public const int MaxHeight = 6;

    /// <summary>The most graphs one saved graph is shown as, on one tab.</summary>
    public const int MaxGraphs = 24;

    /// <summary>The most tabs one key makes.</summary>
    public const int MaxTabs = 12;

    /// <summary>What tells its numbers apart, for those keys that have been given a choice.</summary>
    public List<GraphKey> Keys { get; set; } = [];

    /// <summary>The colour of each line that comes from a key, so it stays between runs.</summary>
    public Dictionary<string, int> SeriesColors { get; set; } = [];

    /// <summary>The lines from keys that were removed from the graph, so they stay away when their messages arrive.</summary>
    public List<string> RemovedLines { get; set; } = [];

    /// <summary>The values of its tab and graph keys seen together, so they are back before a message arrives.</summary>
    public List<Dictionary<string, string>> Seen { get; set; } = [];

    public string Name { get; set; } = "";

    /// <summary>The name of the MQTT client it subscribes through.</summary>
    public string Client { get; set; } = "";

    /// <summary>The one topic a graph had in earlier versions, read once and moved into its values.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Topic { get; set; }

    /// <summary>The topics its values arrive on, in the order they are first used. Each can hold wildcards and keys.</summary>
    [JsonIgnore]
    public IEnumerable<string> Topics => Values.Select(v => v.Topic).Distinct();

    /// <summary>The key shown as tabs, if there is one.</summary>
    [JsonIgnore]
    public string? TabKey => Keys.FirstOrDefault(k => k.Show == KeyShow.Tab)?.Name;

    public GraphKind Kind { get; set; } = GraphKind.Line;

    public List<GraphValue> Values { get; set; } = [];

    /// <summary>Shown after each value, such as °C; empty for none.</summary>
    public string Unit { get; set; } = "";

    /// <summary>How far back the graph goes.</summary>
    public int SpanMinutes { get; set; } = 5;

    /// <summary>Places shown after the decimal point; negative for as many as it needs, up to three.</summary>
    public int Decimals { get; set; } = -1;

    /// <summary>The bottom of the scale; null, with <see cref="Maximum"/>, to fit the values.</summary>
    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    /// <summary>How many columns of the dashboard it spans.</summary>
    public int Width { get; set; } = 1;

    /// <summary>How many rows of the dashboard it spans.</summary>
    public int Height { get; set; } = 1;
}

/// <summary>One dashboard: its name, how it is formatted, and its graphs in order.</summary>
public sealed class DashboardProfile
{
    public const int MaxColumns = 6;
    public const int MinRowHeight = 120;
    public const int MaxRowHeight = 480;
    public const int MaxGap = 32;

    public string Name { get; set; } = "";

    public int Columns { get; set; } = 3;

    public int RowHeight { get; set; } = 240;

    /// <summary>The space between graphs.</summary>
    public int Gap { get; set; } = 16;

    /// <summary>The colour behind the graphs, as #RRGGBB; empty for the theme's.</summary>
    public string Background { get; set; } = "";

    /// <summary>The colour its tab is filled with, as #RRGGBB; empty for the theme's.</summary>
    public string TabColor { get; set; } = "";

    public List<GraphProfile> Graphs { get; set; } = [];

    /// <summary>The key its graphs make tabs from, as it is shown once per value of that key.</summary>
    [JsonIgnore]
    public string? TabKey => Graphs.Select(g => g.TabKey).FirstOrDefault(k => k is not null);
}

/// <summary>
/// The dashboards, kept in <c>dashboards.json</c> in one folder. That is Joktan's data folder unless
/// another has been chosen. Writes happen on a background thread.
/// </summary>
public sealed class DashboardStore
{
    private const string FileName = "dashboards.json";

    private readonly SettingsWriter _writer;

    private DashboardStore(string folder)
    {
        Folder = folder;
        _writer = new SettingsWriter((path, e) => Failed?.Invoke(path, e));
    }

    public string Folder { get; }

    public List<DashboardProfile> Dashboards { get; set; } = [];

    /// <summary>The dashboard that was open last.</summary>
    public string Selected { get; set; } = "";

    /// <summary>
    /// Whether the folder or its file could not be read, as with a drive that is not plugged in, or an
    /// empty mount point or synced folder in its place. Nothing is saved then, since an empty dashboard
    /// would replace the real ones once the folder is back.
    /// </summary>
    public bool IsUnavailable { get; private set; }

    /// <summary>Called on a background thread with the path of a file that could not be written.</summary>
    public event Action<string, Exception>? Failed;

    /// <summary>Where dashboards are kept when no other folder has been chosen.</summary>
    public static string DefaultFolder => DataFolder.Path;

    // Remembers the chosen folder; it cannot live in the chosen folder itself.
    private static string LocationPath => Path.Combine(DataFolder.Path, "dashboards-location.json");

    private string FilePath => Path.Combine(Folder, FileName);

    /// <summary>The folder chosen for dashboards, or the default.</summary>
    public static string ChosenFolder
    {
        get
        {
            try
            {
                if (File.Exists(LocationPath)
                    && JsonSerializer.Deserialize<LocationFile>(File.ReadAllText(LocationPath)) is { Folder.Length: > 0 } location)
                    return location.Folder;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                // An unreadable choice falls back to the default folder.
            }

            return DefaultFolder;
        }
    }

    /// <summary>Remembers the folder for dashboards; the default folder is remembered as no choice.</summary>
    public static void SaveChosenFolder(string folder)
    {
        try
        {
            if (SameFolder(folder, DefaultFolder))
            {
                File.Delete(LocationPath);
                return;
            }

            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllText(LocationPath, JsonSerializer.Serialize(new LocationFile { Folder = folder }, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A choice that cannot be saved is used until the app closes.
        }
    }

    public static bool SameFolder(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a folder already holds dashboards.</summary>
    public static bool HasDashboards(string folder) => File.Exists(Path.Combine(folder, FileName));

    public static DashboardStore Load() => Load(ChosenFolder);

    /// <summary>
    /// Whether what is open can be written to a folder by itself. Not to the one that could not be read: what
    /// is open there is only a stand-in, and the real ones may be about to come back.
    /// </summary>
    public bool MayCopyTo(string folder) => !IsUnavailable || !SameFolder(folder, Folder);

    public static DashboardStore Load(string folder)
    {
        var store = new DashboardStore(folder);
        try
        {
            // A folder is only ever chosen with a file put in it, so one without its file is not what was chosen.
            store.IsUnavailable = !File.Exists(store.FilePath) && !SameFolder(folder, DefaultFolder);
            if (File.Exists(store.FilePath) && JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(store.FilePath)) is { } file)
            {
                store.Dashboards = file.Dashboards ?? [];
                store.Selected = file.Selected ?? "";
            }
        }
        catch (JsonException)
        {
            // Saving would replace the file, so what could not be read is kept beside it.
            store.KeepUnreadable();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            store.IsUnavailable = true;
        }

        store.Clean();
        return store;
    }

    public void Save()
    {
        if (IsUnavailable)
            return;

        try
        {
            Directory.CreateDirectory(Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Failed?.Invoke(Folder, e);
            return;
        }

        _writer.Write(FilePath, Serialize());
    }

    /// <summary>
    /// Writes these dashboards into another folder and returns a store that keeps them there. What is in
    /// this store's folder stays as it is. Throws if dashboards have since appeared there, as the store
    /// returned would go on to replace them.
    /// </summary>
    public async Task<DashboardStore> CopyToAsync(string folder)
    {
        var content = Serialize();
        await Task.Run(() =>
        {
            Directory.CreateDirectory(folder);
            using var file = new FileStream(Path.Combine(folder, FileName), FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(file);
            writer.Write(content);
        });

        return new DashboardStore(folder) { Dashboards = Dashboards, Selected = Selected };
    }

    /// <summary>Completes once everything written so far is on disk.</summary>
    public Task FlushAsync() => _writer.FlushAsync();

    private string Serialize()
        => JsonSerializer.Serialize(new StoreFile { Dashboards = Dashboards, Selected = Selected }, JsonFormats.Indented);

    private void KeepUnreadable()
    {
        try
        {
            File.Copy(FilePath, FilePath + ".unreadable", overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // It was worth trying.
        }
    }

    // A file edited by hand can hold anything, so what is drawn from it is brought within limits.
    private void Clean()
    {
        Dashboards = Dashboards.Where(d => d is not null).ToList();
        foreach (var dashboard in Dashboards)
        {
            dashboard.Name ??= "";
            dashboard.Background ??= "";
            dashboard.TabColor ??= "";
            dashboard.Columns = Math.Clamp(dashboard.Columns, 1, DashboardProfile.MaxColumns);
            dashboard.RowHeight = Math.Clamp(dashboard.RowHeight, DashboardProfile.MinRowHeight, DashboardProfile.MaxRowHeight);
            dashboard.Gap = Math.Clamp(dashboard.Gap, 0, DashboardProfile.MaxGap);
            dashboard.Graphs = (dashboard.Graphs ?? []).Where(g => g is not null).ToList();
            foreach (var graph in dashboard.Graphs)
            {
                graph.Name ??= "";
                graph.Client ??= "";
                graph.Unit ??= "";
                graph.SpanMinutes = Math.Clamp(graph.SpanMinutes, 1, 24 * 60);
                graph.Width = Math.Clamp(graph.Width, 1, DashboardProfile.MaxColumns);
                graph.Height = Math.Clamp(graph.Height, 1, GraphProfile.MaxHeight);
                graph.Values = (graph.Values ?? []).Where(v => v is not null).Take(GraphProfile.MaxValues).ToList();
                foreach (var value in graph.Values)
                {
                    if (string.IsNullOrEmpty(value.Topic))
                        value.Topic = graph.Topic ?? "";
                    value.Path ??= "";
                    value.Label ??= "";
                    value.Color = Math.Clamp(value.Color, 0, GraphColors.Count - 1);
                }

                graph.Keys = (graph.Keys ?? []).Where(k => k is { Name.Length: > 0 }).ToList();
                foreach (var key in graph.Keys)
                {
                    key.Field ??= "";
                    key.Only ??= "";
                }

                graph.SeriesColors = (graph.SeriesColors ?? []).ToDictionary(c => c.Key, c => Math.Clamp(c.Value, 0, GraphColors.Count - 1));
                graph.RemovedLines = (graph.RemovedLines ?? []).Where(l => l is not null).ToList();
                graph.Seen = (graph.Seen ?? []).Where(s => s is not null).Take(GraphProfile.MaxGraphs * GraphProfile.MaxTabs).ToList();

                graph.Topic = null;
                if (graph.Minimum is not { } minimum || graph.Maximum is not { } maximum || minimum >= maximum)
                    graph.Minimum = graph.Maximum = null;
            }
        }
    }

    private sealed class LocationFile
    {
        public string Folder { get; set; } = "";
    }

    private sealed class StoreFile
    {
        public List<DashboardProfile>? Dashboards { get; set; }

        public string? Selected { get; set; }
    }
}
