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

    public string Topic { get; set; } = "";

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

    public List<GraphProfile> Graphs { get; set; } = [];
}

/// <summary>The dashboards, kept in <c>dashboards.json</c> in Joktan's data folder. Writes happen on a background thread.</summary>
public sealed class DashboardStore
{
    private readonly SettingsWriter _writer = new((_, _) =>
    {
        // Dashboards that cannot be saved are simply not remembered.
    });

    public List<DashboardProfile> Dashboards { get; set; } = [];

    /// <summary>The dashboard that was open last.</summary>
    public string Selected { get; set; } = "";

    private static string FilePath => Path.Combine(DataFolder.Path, "dashboards.json");

    public static DashboardStore Load()
    {
        var store = new DashboardStore();
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(FilePath)) is { } file)
            {
                store.Dashboards = file.Dashboards ?? [];
                store.Selected = file.Selected ?? "";
            }
        }
        catch (JsonException)
        {
            // Saving would replace the file, so what could not be read is kept beside it.
            KeepUnreadable();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable file is treated as no dashboards.
        }

        store.Clean();
        return store;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var file = new StoreFile { Dashboards = Dashboards, Selected = Selected };
        _writer.Write(FilePath, JsonSerializer.Serialize(file, JsonFormats.Indented));
    }

    /// <summary>Completes once everything written so far is on disk.</summary>
    public Task FlushAsync() => _writer.FlushAsync();

    private static void KeepUnreadable()
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
            dashboard.Columns = Math.Clamp(dashboard.Columns, 1, DashboardProfile.MaxColumns);
            dashboard.RowHeight = Math.Clamp(dashboard.RowHeight, DashboardProfile.MinRowHeight, DashboardProfile.MaxRowHeight);
            dashboard.Gap = Math.Clamp(dashboard.Gap, 0, DashboardProfile.MaxGap);
            dashboard.Graphs = (dashboard.Graphs ?? []).Where(g => g is not null).ToList();
            foreach (var graph in dashboard.Graphs)
            {
                graph.Name ??= "";
                graph.Client ??= "";
                graph.Topic ??= "";
                graph.Unit ??= "";
                graph.SpanMinutes = Math.Clamp(graph.SpanMinutes, 1, 24 * 60);
                graph.Width = Math.Clamp(graph.Width, 1, DashboardProfile.MaxColumns);
                graph.Height = Math.Clamp(graph.Height, 1, GraphProfile.MaxHeight);
                graph.Values = (graph.Values ?? []).Where(v => v is not null).Take(GraphProfile.MaxValues).ToList();
                foreach (var value in graph.Values)
                {
                    value.Path ??= "";
                    value.Label ??= "";
                    value.Color = Math.Clamp(value.Color, 0, GraphColors.Count - 1);
                }

                if (graph.Minimum is not { } minimum || graph.Maximum is not { } maximum || minimum >= maximum)
                    graph.Minimum = graph.Maximum = null;
            }
        }
    }

    private sealed class StoreFile
    {
        public List<DashboardProfile>? Dashboards { get; set; }

        public string? Selected { get; set; }
    }
}
