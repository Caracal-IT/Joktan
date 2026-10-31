using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.TopicDashboards.Tests;

[Collection(ProcessStateCollection.Name)]
public class DashboardStoreTests : DataFolderTest
{
    private string FilePath => Data.At("dashboards.json");

    private DashboardProfile LoadOne(string dashboard)
    {
        File.WriteAllText(FilePath, $$"""{"Dashboards":[{{dashboard}}]}""");
        return Assert.Single(DashboardStore.Load().Dashboards);
    }

    private GraphProfile LoadGraph(string graph) => Assert.Single(LoadOne($$"""{"Graphs":[{{graph}}]}""").Graphs);

    [Fact]
    public void With_no_file_there_are_no_dashboards()
    {
        var store = DashboardStore.Load();

        Assert.Empty(store.Dashboards);
        Assert.Equal("", store.Selected);
    }

    [Fact]
    public async Task What_is_saved_is_loaded()
    {
        var store = DashboardStore.Load();
        store.Selected = "Plant";
        store.Dashboards.Add(new DashboardProfile
        {
            Name = "Plant",
            Columns = 2,
            TabColor = "#112233",
            Graphs =
            [
                new GraphProfile
                {
                    Name = "Temp",
                    Client = "main",
                    Kind = GraphKind.Gauge,
                    Minimum = 0,
                    Maximum = 100,
                    Values = [new GraphValue { Topic = "a/b", Path = "t", Label = "T", Color = 3 }],
                },
            ],
        });

        store.Save();
        await store.FlushAsync();
        var loaded = DashboardStore.Load();

        Assert.Equal("Plant", loaded.Selected);
        var dashboard = Assert.Single(loaded.Dashboards);
        Assert.Equal(2, dashboard.Columns);
        Assert.Equal("#112233", dashboard.TabColor);
        var graph = Assert.Single(dashboard.Graphs);
        Assert.Equal(GraphKind.Gauge, graph.Kind);
        Assert.Equal(100, graph.Maximum);
        Assert.Equal(3, graph.Values[0].Color);
        Assert.Contains("\"Gauge\"", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_kept_beside_it()
    {
        File.WriteAllText(FilePath, "not json");

        var store = DashboardStore.Load();

        Assert.Empty(store.Dashboards);
        Assert.Equal("not json", File.ReadAllText(FilePath + ".unreadable"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    [InlineData(99, DashboardProfile.MaxColumns)]
    public void The_columns_are_brought_within_limits(int columns, int expected)
    {
        Assert.Equal(expected, LoadOne($$"""{"Columns":{{columns}}}""").Columns);
    }

    [Theory]
    [InlineData(0, DashboardProfile.MinRowHeight)]
    [InlineData(300, 300)]
    [InlineData(9999, DashboardProfile.MaxRowHeight)]
    public void The_row_height_is_brought_within_limits(int height, int expected)
    {
        Assert.Equal(expected, LoadOne($$"""{"RowHeight":{{height}}}""").RowHeight);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(8, 8)]
    [InlineData(99, DashboardProfile.MaxGap)]
    public void The_gap_is_brought_within_limits(int gap, int expected)
    {
        Assert.Equal(expected, LoadOne($$"""{"Gap":{{gap}}}""").Gap);
    }

    [Fact]
    public void Empty_values_are_filled_in()
    {
        File.WriteAllText(FilePath, """
            {"Dashboards":[null,{"Name":null,"Background":null,"TabColor":null,"Graphs":null},
              {"Graphs":[null,{"Name":null,"Client":null,"Unit":null,"Values":null},
                {"Values":[null,{"Path":null,"Label":null}]}]}],"Selected":null}
            """);

        var store = DashboardStore.Load();

        Assert.Equal("", store.Selected);
        Assert.Equal(2, store.Dashboards.Count);
        Assert.Equal("", store.Dashboards[0].Name);
        Assert.Equal("", store.Dashboards[0].Background);
        Assert.Equal("", store.Dashboards[0].TabColor);
        Assert.Empty(store.Dashboards[0].Graphs);
        var graphs = store.Dashboards[1].Graphs;
        Assert.Equal(2, graphs.Count);
        Assert.Equal(("", "", ""), (graphs[0].Name, graphs[0].Client, graphs[0].Unit));
        Assert.Empty(graphs[0].Values);
        var value = Assert.Single(graphs[1].Values);
        Assert.Equal(("", ""), (value.Path, value.Label));
    }

    [Fact]
    public void A_graph_is_brought_within_limits()
    {
        var graph = LoadGraph("""{"SpanMinutes":0,"Width":99,"Height":99}""");

        Assert.Equal(1, graph.SpanMinutes);
        Assert.Equal(DashboardProfile.MaxColumns, graph.Width);
        Assert.Equal(GraphProfile.MaxHeight, graph.Height);
        Assert.Equal(24 * 60, LoadGraph("""{"SpanMinutes":99999}""").SpanMinutes);
        Assert.Equal(1, LoadGraph("""{"Width":0}""").Width);
        Assert.Equal(1, LoadGraph("""{"Height":0}""").Height);
    }

    [Fact]
    public void A_graph_draws_at_most_eight_values_in_colours_that_exist()
    {
        var values = string.Join(',', Enumerable.Range(0, 10).Select(i => $$"""{"Topic":"t{{i}}","Color":{{i * 10 - 10}}}"""));

        var graph = LoadGraph($$"""{"Values":[{{values}}]}""");

        Assert.Equal(GraphProfile.MaxValues, graph.Values.Count);
        Assert.Equal(0, graph.Values[0].Color);
        Assert.Equal(GraphColors.Count - 1, graph.Values[^1].Color);
    }

    [Fact]
    public void The_one_topic_of_earlier_versions_moves_into_the_values()
    {
        var graph = LoadGraph("""{"Topic":"old","Values":[{"Path":"a"},{"Topic":"new"}]}""");

        Assert.Equal("old", graph.Values[0].Topic);
        Assert.Equal("new", graph.Values[1].Topic);
        Assert.Null(graph.Topic);
    }

    [Theory]
    [InlineData("""{"Minimum":0,"Maximum":10}""", 0.0, 10.0)]
    [InlineData("""{"Minimum":10,"Maximum":10}""", null, null)]
    [InlineData("""{"Minimum":20,"Maximum":10}""", null, null)]
    [InlineData("""{"Minimum":0}""", null, null)]
    [InlineData("""{"Maximum":10}""", null, null)]
    public void A_scale_needs_a_bottom_below_its_top(string graph, double? minimum, double? maximum)
    {
        var read = LoadGraph(graph);

        Assert.Equal(minimum, read.Minimum);
        Assert.Equal(maximum, read.Maximum);
    }

    [Fact]
    public void Remembers_the_folder_chosen()
    {
        DashboardStore.SaveChosenFolder("/some/where");

        Assert.Equal("/some/where", DashboardStore.ChosenFolder);
    }

    [Fact]
    public void Choosing_the_default_forgets_the_choice()
    {
        DashboardStore.SaveChosenFolder("/some/where");

        DashboardStore.SaveChosenFolder(DashboardStore.DefaultFolder);

        Assert.False(File.Exists(Data.At("dashboards-location.json")));
        Assert.Equal(DashboardStore.DefaultFolder, DashboardStore.ChosenFolder);
    }

    [Fact]
    public void A_choice_that_cannot_be_read_falls_back_to_the_default()
    {
        File.WriteAllText(Data.At("dashboards-location.json"), "not json");

        Assert.Equal(DashboardStore.DefaultFolder, DashboardStore.ChosenFolder);
    }

    [Fact]
    public async Task Saves_into_the_folder_chosen_and_not_the_data_folder()
    {
        using var chosen = new TempFolder();
        DashboardStore.SaveChosenFolder(chosen.Path);
        var store = DashboardStore.Load();
        store.Dashboards.Add(new DashboardProfile { Name = "Plant" });

        store.Save();
        await store.FlushAsync();

        Assert.True(File.Exists(chosen.At("dashboards.json")));
        Assert.False(File.Exists(FilePath));
        Assert.Equal("Plant", Assert.Single(DashboardStore.Load().Dashboards).Name);
    }

    [Fact]
    public async Task A_missing_folder_is_not_written_to_even_once_it_is_back()
    {
        using var chosen = new TempFolder();
        var folder = chosen.At("drive");

        var store = DashboardStore.Load(folder);
        Assert.True(store.IsUnavailable);

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "dashboards.json"), "the real ones");
        store.Save();
        await store.FlushAsync();

        Assert.Equal("the real ones", File.ReadAllText(Path.Combine(folder, "dashboards.json")));
    }

    [Fact]
    public void A_data_folder_not_made_yet_is_not_a_missing_folder()
    {
        Directory.Delete(Data.Path);

        Assert.False(DashboardStore.Load().IsUnavailable);
    }

    [Fact]
    public async Task Copying_makes_a_store_that_saves_to_the_new_folder_and_leaves_the_old_file()
    {
        using var target = new TempFolder();
        var store = DashboardStore.Load();
        store.Dashboards.Add(new DashboardProfile { Name = "Plant" });
        store.Selected = "Plant";
        store.Save();
        await store.FlushAsync();
        var original = File.ReadAllText(FilePath);

        var copy = await store.CopyToAsync(target.At("new"));
        Assert.True(DashboardStore.HasDashboards(target.At("new")));
        copy.Dashboards.Add(new DashboardProfile { Name = "Line" });
        copy.Save();
        await copy.FlushAsync();

        Assert.Equal(target.At("new"), copy.Folder);
        Assert.False(copy.IsUnavailable);
        Assert.Equal(original, File.ReadAllText(FilePath));
        var loaded = DashboardStore.Load(target.At("new"));
        Assert.Equal("Plant", loaded.Selected);
        Assert.Equal(["Plant", "Line"], loaded.Dashboards.Select(d => d.Name));
    }

    [Fact]
    public async Task A_write_that_fails_is_reported()
    {
        using var folder = new TempFolder();
        var blocked = folder.Write("file", "a file where the folder should be");
        var store = DashboardStore.Load();
        var unwritable = await store.CopyToAsync(folder.At("ok"));
        Directory.Delete(folder.At("ok"), recursive: true);
        File.Copy(blocked, folder.At("ok"));
        string? failed = null;
        unwritable.Failed += (path, _) => failed = path;

        unwritable.Save();
        await unwritable.FlushAsync();

        Assert.Equal(folder.At("ok"), failed);
    }

    [Fact]
    public void Lists_the_topics_of_a_graph_once_each_in_order()
    {
        var graph = new GraphProfile
        {
            Values = [new GraphValue { Topic = "b" }, new GraphValue { Topic = "a" }, new GraphValue { Topic = "b" }],
        };

        Assert.Equal(["b", "a"], graph.Topics);
    }
}
