using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Mqtt;
using Joktan.Mqtt.Tests;
using Joktan.TopicDashboards.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.TopicDashboards.Tests.ViewModels;

public class TopicDashboardsViewModelTests : DashboardTest
{
    [Fact]
    public Task An_empty_store_gets_a_first_dashboard() => Headless.Run(() =>
    {
        var page = Page();

        Assert.Equal("Main", Assert.Single(page.Dashboards).Name);
        Assert.Same(page.Dashboards[0], page.SelectedDashboard);
        Assert.Equal(StoreFolder, page.Folder);
        Assert.Equal(DashboardStore.DefaultFolder, page.DefaultFolder);
        Assert.False(page.HasSaveError);
        Assert.False(page.HasSubscriptionError);
        Assert.False(page.HasLimitWarning);
        Assert.Equal("Edit", page.EditModeText);
        Assert.NotNull(page.FindClient("c"));
    });

    [Fact]
    public Task Without_a_store_the_chosen_folder_is_read() => Headless.Run(() =>
    {
        var page = new TopicDashboardsViewModel(Mqtt());

        Assert.Equal(DashboardStore.ChosenFolder, page.Folder);
    });

    [Fact]
    public Task The_dashboard_open_last_is_selected_again() => Headless.Run(async () =>
    {
        var store = DashboardStore.Load(StoreFolder);
        store.Dashboards.AddRange([Board("one"), Board("two")]);
        store.Selected = "two";
        store.Save();
        await store.FlushAsync();

        var page = new TopicDashboardsViewModel(Mqtt(), DashboardStore.Load(StoreFolder));

        Assert.Equal("two", page.SelectedDashboard!.Name);
    });

    [Fact]
    public Task Editing_is_a_mode_that_puts_away_what_it_opened() => Headless.Run(() =>
    {
        var page = Page();
        var tab = page.Dashboards[0];

        page.ToggleEditingCommand.Execute(null);
        Assert.True(page.IsEditing);
        Assert.Equal("View", page.EditModeText);
        tab.RenameCommand.Execute(null);
        tab.AskRemoveCommand.Execute(null);

        page.ToggleEditingCommand.Execute(null);

        Assert.False(tab.IsRenaming);
        Assert.False(tab.IsConfirmingRemove);
    });

    [Fact]
    public Task New_dashboards_get_a_free_name_and_are_selected() => Headless.Run(() =>
    {
        var page = Page(null, Board("Dashboard 2"));

        page.AddDashboardCommand.Execute(null);

        Assert.Equal("Dashboard 3", page.SelectedDashboard!.Name);
        Assert.Equal("named", page.CreateDashboard("named").Name);
    });

    [Fact]
    public Task Removing_the_last_dashboard_replaces_it_with_an_empty_one() => Headless.Run(() =>
    {
        var page = Page(null, Board("only", Graph()));
        var only = page.Dashboards[0];

        page.Remove(only);

        Assert.Equal("Main", Assert.Single(page.Dashboards).Name);
        Assert.Same(page.Dashboards[0], page.SelectedDashboard);
    });

    [Fact]
    public Task Removing_the_selected_dashboard_selects_the_next_or_the_one_before() => Headless.Run(() =>
    {
        var page = Page(null, Board("a"), Board("b"), Board("c"));

        page.SelectedDashboard = page.Dashboards[1];
        page.Remove(page.Dashboards[1]);
        Assert.Equal("c", page.SelectedDashboard!.Name);

        page.Remove(page.Dashboards[1]);
        Assert.Equal("a", page.SelectedDashboard!.Name);

        page.SelectedDashboard = page.Dashboards[0];
        page.Remove(page.Dashboards[0]);
        Assert.Equal("Main", page.SelectedDashboard!.Name);
    });

    [Fact]
    public Task A_graph_says_why_it_draws_nothing() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var mqtt = Mqtt(broker.Port);
        var one = Graph("one");
        var two = Graph("two");
        two.Values.Add(new GraphValue { Topic = "t/b", Path = "temp" });
        var gone = Graph("gone");
        gone.Client = "nobody";
        var page = Page(mqtt, Board("d", one, two, gone));
        var cards = page.Dashboards[0].Graphs;

        Assert.Equal("Connect c to receive t/a", page.StatusOf(cards[0]));
        Assert.Equal("Connect c to receive its 2 topics", page.StatusOf(cards[1]));
        Assert.Equal("There is no MQTT client called nobody", page.StatusOf(cards[2]));

        await Connect(mqtt);
        Assert.Equal("Waiting for a message on t/a", page.StatusOf(cards[0]));
        await Headless.Until(() => cards[0].Status == "Waiting for a message on t/a");

        await broker.SendAsync("t/a", "not json at all");
        await Headless.Until(() => page.StatusOf(cards[0])?.StartsWith("Messages are arriving", StringComparison.Ordinal) == true);
        await Headless.Until(() => cards[0].Status?.StartsWith("Messages are arriving", StringComparison.Ordinal) == true);

        await broker.SendAsync("t/a", """{"temp":21.5}""");
        await Headless.Until(() => cards[0].HasReadings);
        await Headless.Until(() => cards[0].Status is null);
        Assert.Null(page.StatusOf(cards[0]));
        Assert.Equal("21.5", cards[0].HeadlineValue.Replace(',', '.'));
    });

    [Fact]
    public Task Subscribing_failing_is_reported() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var mqtt = Mqtt(broker.Port);
        var page = Page(mqtt);
        await Connect(mqtt);
        var tab = page.Dashboards[0];

        tab.Profile.Graphs.Add(Graph(topic: "refused/x"));
        page.GraphsChanged();

        await Headless.Until(() => page.HasSubscriptionError);
        Assert.Contains("refused", page.SubscriptionError);

        tab.Profile.Graphs.Clear();
        page.GraphsChanged();
        await Headless.Until(() => !page.HasSubscriptionError);
    });

    [Fact]
    public Task A_key_of_a_tab_makes_a_tab_for_each_value_and_they_share_their_format() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var mqtt = Mqtt(broker.Port);
        var graph = Graph("g", "home/{room}/t", "temp", GraphKind.Line, new GraphKey { Name = "room", Show = KeyShow.Tab });
        var page = Page(mqtt, Board("Home", graph));
        await Connect(mqtt);
        Assert.Single(page.Dashboards);
        Assert.False(page.Dashboards[0].HasTabValue);

        await broker.SendAsync("home/kitchen/t", """{"temp":1}""");
        await Headless.Until(() => page.Dashboards.Any(d => d.TabValue == "kitchen"));
        await broker.SendAsync("home/hall/t", """{"temp":2}""");
        await Headless.Until(() => page.Dashboards.Count == 2);

        var tabs = page.Dashboards.ToList();
        var kitchen = tabs.First(t => t.TabValue == "kitchen");
        Assert.Equal("Home: kitchen", kitchen.DisplayName);
        Assert.Contains("\u001fkitchen", kitchen.SelectionName);
        Assert.Equal(2, page.TabsOf(kitchen.Profile).Count());
        Assert.Contains("all 2 of its tabs", kitchen.RemoveQuestion);

        kitchen.Columns = 2;
        Assert.All(tabs, t => Assert.Equal(2, t.Columns));

        // Forgetting a value takes its tab away until it is seen again.
        kitchen.ForgetValueCommand.Execute(null);
        Assert.Single(page.Dashboards);
        Assert.Equal("hall", page.Dashboards[0].TabValue);

        page.Dashboards[0].ForgetValueCommand.Execute(null);
        Assert.Null(Assert.Single(page.Dashboards).TabValue);
        page.Dashboards[0].ForgetValueCommand.Execute(null);

        await broker.SendAsync("home/attic/t", """{"temp":3}""");
        await Headless.Until(() => page.Dashboards[0].TabValue == "attic");
    });

    [Fact]
    public Task A_tab_name_can_hold_the_value() => Headless.Run(() =>
    {
        var graph = Graph("g", "home/{room}/t", "temp", GraphKind.Line, new GraphKey { Name = "room", Show = KeyShow.Tab });
        var page = Page(null, Board("{room} sensors", graph));
        var tab = page.Dashboards[0];

        tab.ShowTabValue("room", "hall");

        Assert.Equal("hall sensors", tab.DisplayName);
        Assert.True(tab.HasTabValue);
    });

    [Fact]
    public Task Removing_a_graph_takes_it_from_the_dashboard() => Headless.Run(() =>
    {
        var graph = Graph();
        var page = Page(null, Board("d", graph));
        var card = page.Dashboards[0].Graphs[0];

        card.RemoveCommand.Execute(null);

        Assert.Empty(page.Dashboards[0].Graphs);
        Assert.True(page.Dashboards[0].HasNoGraphs);
    });

    [Fact]
    public Task Each_new_graph_opens_the_editor() => Headless.Run(() =>
    {
        var page = Page(null, Board("d", Graph()));
        var tab = page.Dashboards[0];
        tab.AddGraphCommand.Execute(null);
        tab.Graphs[0].EditCommand.Execute(null);

        var editors = new System.Collections.Generic.List<GraphEditorViewModel>();
        page.ShowEditor = e =>
        {
            editors.Add(e);
            return Task.CompletedTask;
        };

        tab.AddGraphCommand.Execute(null);
        tab.Graphs[0].EditCommand.Execute(null);

        Assert.True(editors[0].IsNew);
        Assert.False(editors[1].IsNew);
    });

    [Fact]
    public Task A_renamed_client_keeps_its_graphs_and_a_removed_one_leaves_them_waiting() => Headless.Run(async () =>
    {
        var mqtt = new Joktan.Mqtt.ViewModels.MqttViewModel(null, new MqttSettings
        {
            Clients = [new ClientProfile { Name = "c", Host = "h", ClientId = "1" }, new ClientProfile { Name = "other", Host = "h", ClientId = "2" }],
            SelectedClient = "c",
        });
        var graph = Graph();
        var page = Page(mqtt, Board("d", graph));
        var client = mqtt.Clients[0];

        client.Apply(new ClientProfile { Name = "renamed", Host = "h", ClientId = "1" }, "", "");
        Assert.Equal("renamed", graph.Client);

        mqtt.Clients[1].RemoveClientCommand.Execute(null);
        Assert.Single(mqtt.Clients);

        mqtt.EditClientSettings = _ => Task.FromResult(true);
        await mqtt.AddClientCommand.ExecuteAsync(null);
        Assert.Equal(2, mqtt.Clients.Count);

        client.RemoveClientCommand.Execute(null);
        Assert.StartsWith("There is no MQTT client", page.StatusOf(page.Dashboards[0].Graphs[0]));
    });

    [Fact]
    public Task A_message_for_another_client_or_topic_goes_nowhere() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var mqtt = Mqtt(broker.Port);
        var page = Page(mqtt, Board("d", Graph(), Graph("second", "t/b")));
        await Connect(mqtt);
        await Headless.Until(() => page.Dashboards[0].Graphs[0].Status?.StartsWith("Waiting", StringComparison.Ordinal) == true);

        await broker.SendAsync("t/b", """{"temp":1}""");

        await Headless.Until(() => page.Dashboards[0].Graphs[1].HasReadings);
        Assert.False(page.Dashboards[0].Graphs[0].HasReadings);
    });

    [Fact]
    public Task Time_moves_the_graphs_on_and_drops_what_is_old() => Headless.Run(async () =>
    {
        var graph = Graph();
        var page = Page(null, Board("d", graph), Board("hidden", Graph("h")));
        var card = page.Dashboards[0].Graphs[0];
        var series = page.SourceFor(graph).Data.Series("t/a", "temp");
        series.Add(DateTime.Now.AddHours(-2), [1]);
        series.Add(DateTime.Now.AddHours(-1), [2]);
        series.Add(DateTime.Now, [3]);
        var revision = card.Revision;

        await Headless.Until(() => card.Revision > revision, timeoutMs: 5000);

        // The last of the old ones stays, so that a line still enters from the edge.
        Assert.Equal([2, 3], series.Samples.Select(r => r.Value));
    });

    [Fact]
    public Task Too_many_series_are_warned_about() => Headless.Run(async () =>
    {
        var graph = Graph("g", "t/{id}", "temp", GraphKind.Line, new GraphKey { Name = "id" });
        var page = Page(null, Board("d", graph));
        var source = page.SourceFor(graph);
        foreach (var i in Enumerable.Range(0, TopicDashboardsViewModel.MaxSeries + 1))
            source.Receive(Message($"t/{i}", """{"temp":1}"""));

        await Headless.Until(() => page.HasLimitWarning, timeoutMs: 5000);

        Assert.NotNull(page.LimitWarning);
    });

    // ---- The folder ----

    [Fact]
    public Task The_folder_band_starts_from_the_current_folder() => Headless.Run(() =>
    {
        var page = Page();
        page.FolderMessage = "old";

        page.ToggleFolderCommand.Execute(null);

        Assert.True(page.IsChoosingFolder);
        Assert.Equal(page.Folder, page.FolderText);
        Assert.Null(page.FolderMessage);

        page.UseDefaultFolderCommand.Execute(null);
        Assert.Equal(page.DefaultFolder, page.FolderText);
    });

    [Fact]
    public Task Browsing_uses_the_folder_picked_if_any() => Headless.Run(async () =>
    {
        var page = Page();
        page.FolderText = "before";

        await page.BrowseFolderCommand.ExecuteAsync(null);
        page.PickFolder = () => Task.FromResult<string?>(null);
        await page.BrowseFolderCommand.ExecuteAsync(null);
        Assert.Equal("before", page.FolderText);

        page.PickFolder = () => Task.FromResult<string?>("picked");
        await page.BrowseFolderCommand.ExecuteAsync(null);
        Assert.Equal("picked", page.FolderText);
    });

    [Fact]
    public Task A_relative_folder_is_refused_and_the_same_one_closes_the_band() => Headless.Run(async () =>
    {
        var page = Page();
        page.IsChoosingFolder = true;

        page.FolderText = "some/relative";
        await page.ApplyFolderCommand.ExecuteAsync(null);
        Assert.Contains("whole path", page.FolderMessage);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        page.FolderText = "~/" + Path.GetRelativePath(home, page.Folder);
        await page.ApplyFolderCommand.ExecuteAsync(null);
        Assert.False(page.IsChoosingFolder);
    });

    [Fact]
    public Task A_blank_folder_means_the_default() => Headless.Run(async () =>
    {
        var page = new TopicDashboardsViewModel(Mqtt(), DashboardStore.Load(DashboardStore.DefaultFolder));
        page.IsChoosingFolder = true;
        page.FolderText = " ";

        await page.ApplyFolderCommand.ExecuteAsync(null);

        Assert.False(page.IsChoosingFolder);
    });

    [Fact]
    public Task Dashboards_are_copied_to_a_folder_without_any() => Headless.Run(async () =>
    {
        var page = Page(null, Board("d", Graph()));
        var previous = page.Folder;
        var card = page.Dashboards[0].Graphs[0];
        page.IsEditing = true;
        page.IsChoosingFolder = true;
        page.FolderText = Data.At("elsewhere");

        await page.ApplyFolderCommand.ExecuteAsync(null);

        Assert.Equal(Data.At("elsewhere"), page.Folder);
        Assert.Contains(previous, page.Message);
        Assert.False(page.IsChoosingFolder);
        Assert.False(page.IsEditing);
        Assert.Same(card, page.Dashboards[0].Graphs[0]);
        Assert.True(DashboardStore.HasDashboards(page.Folder));
    });

    [Fact]
    public Task A_folder_holding_dashboards_is_opened_as_it_is() => Headless.Run(async () =>
    {
        var folder = await FolderWith("other", Board("theirs"));
        var page = Page();
        page.FolderText = folder;

        await page.ApplyFolderCommand.ExecuteAsync(null);

        Assert.Equal("theirs", Assert.Single(page.Dashboards).Name);
        Assert.StartsWith("Opened", page.Message);
    });

    [Fact]
    public Task A_folder_that_cannot_be_written_says_so() => Headless.Run(async () =>
    {
        var file = Data.Write("a-file", "");
        var page = Page();
        page.FolderText = Path.Combine(file, "below");

        await page.ApplyFolderCommand.ExecuteAsync(null);

        Assert.StartsWith("Could not copy the dashboards", page.FolderMessage);
    });

    private DashboardStore UnavailableStore(string folder)
    {
        DashboardStore.SaveChosenFolder(folder);
        var store = DashboardStore.Load();
        Assert.True(store.IsUnavailable);
        return store;
    }

    [Fact]
    public Task A_chosen_folder_that_has_gone_is_not_written_to_until_asked() => Headless.Run(async () =>
    {
        var folder = Data.At("gone");
        var page = new TopicDashboardsViewModel(Mqtt(), UnavailableStore(folder));

        Assert.True(page.HasSaveError);
        Assert.Contains("cannot be read", page.SaveError);

        page.FolderText = folder;
        await page.ApplyFolderCommand.ExecuteAsync(null);
        Assert.Contains("still no dashboards", page.FolderMessage);
        Assert.True(page.CanStartEmpty);
        Assert.False(DashboardStore.HasDashboards(folder));

        await page.StartEmptyHereCommand.ExecuteAsync(null);
        Assert.StartsWith("Started empty", page.Message);
        Assert.True(DashboardStore.HasDashboards(folder));
        Assert.False(page.HasSaveError);
    });

    [Fact]
    public Task Dashboards_that_reappear_are_not_started_over() => Headless.Run(async () =>
    {
        var folder = Data.At("gone");
        var page = new TopicDashboardsViewModel(Mqtt(), UnavailableStore(folder));
        await FolderWith("gone", Board("back"));

        await page.StartEmptyHereCommand.ExecuteAsync(null);

        Assert.StartsWith("Dashboards have appeared", page.FolderMessage);
    });

    [Fact]
    public Task Starting_empty_where_nothing_can_be_written_says_so() => Headless.Run(async () =>
    {
        var file = Data.Write("a-file", "");
        var page = new TopicDashboardsViewModel(Mqtt(), UnavailableStore(Path.Combine(file, "below")));

        await page.StartEmptyHereCommand.ExecuteAsync(null);

        Assert.StartsWith("Could not copy the dashboards", page.FolderMessage);
    });

    [Fact]
    public Task A_write_that_fails_is_reported() => Headless.Run(async () =>
    {
        var page = Page();
        page.SaveState();
        var file = Assert.Single(Directory.GetFiles(StoreFolder), f => f.EndsWith("dashboards.json", StringComparison.Ordinal));
        File.Delete(file);
        Directory.CreateDirectory(file);

        page.SaveState();

        await Headless.Until(() => page.HasSaveError);
        Assert.StartsWith("Could not write ", page.SaveError);
    });
}
