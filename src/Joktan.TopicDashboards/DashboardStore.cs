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

/// <summary>One graph on a dashboard: what it subscribes to, what it draws, and how it is laid out.</summary>
public sealed class GraphProfile
{
    public const int MaxValues = 8;
    public const int MaxHeight = 6;

    public string Name { get; set; } = "";

    /// <summary>The name of the MQTT client it subscribes through.</summary>
    public string Client { get; set; } = "";

    /// <summary>The one topic a graph had in earlier versions, read once and moved into its values.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Topic { get; set; }

    /// <summary>The topics its values arrive on, in the order they are first used.</summary>
    [JsonIgnore]
    public IEnumerable<string> Topics => Values.Select(v => v.Topic).Distinct();

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
    /// Whether the folder or its file could not be read, as with a drive that is not plugged in. Nothing
    /// is saved then, since an empty dashboard would replace the real ones once the folder is back.
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

    public static DashboardStore Load(string folder)
    {
        var store = new DashboardStore(folder);
        try
        {
            store.IsUnavailable = !Directory.Exists(folder) && !SameFolder(folder, DefaultFolder);
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
    /// Writes these dashboards into another folder, replacing no others, and returns a store that keeps
    /// them there. What is in this store's folder stays as it is.
    /// </summary>
    public async Task<DashboardStore> CopyToAsync(string folder)
    {
        var content = Serialize();
        await Task.Run(() =>
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FileName);
            if (!File.Exists(path))
                File.WriteAllText(path, content);
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
