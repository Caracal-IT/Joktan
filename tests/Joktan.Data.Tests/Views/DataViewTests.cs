using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Joktan.Certificates;
using Joktan.Containers;
using Joktan.Containers.Tests.ViewModels;
using Joktan.Containers.ViewModels;
using Joktan.Data.Postgres;
using Joktan.Data.Tests.ViewModels;
using Joktan.Data.ViewModels;
using Joktan.Data.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Data.Tests.Views;

public class DataViewTests : FakeEngineTest
{
    /// <summary>A page whose brokers are on the stand-in for docker, with their compose file on the list when there are any.</summary>
    private DataViewModel Page(params MosquittoBroker[] brokers)
    {
        var folder = Folder.At("brokers");
        new ComposeSettings
        {
            ComposeFiles = brokers.Length > 0 ? [MosquittoProject.ComposeFileIn(folder)] : [],
            Engine = ContainerEngine.Docker, BrokersFolder = folder, Brokers = [.. brokers],
        }.Save(DataFolder);
        var page = new DataViewModel(new ContainersViewModel(DataFolder, CertificateStore.Load(Folder.At("certificates")), new FakeCredentialStore()));
        if (brokers.Length > 0)
            Assert.Null(page.Brokers.Write());
        return page;
    }

    private static string[] Shown(Window window) =>
        Headless.All<TextBlock>(window).Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToArray();

    [Fact]
    public Task The_brokers_card_shows_the_list_and_then_the_editor_in_its_place() => Headless.Run(() =>
    {
        var page = Page(new MosquittoBroker { Name = "listed", Port = 1883 });
        var window = Headless.Show(new DataView { DataContext = page });
        Headless.Settle();

        Assert.Contains("listed", Shown(window));
        Assert.Contains("Each broker runs as a container. Their compose project is also on Dev Tools / Containers, with its logs and command output.", Shown(window));

        page.Brokers.Items[0].IsConfirmingRemove = true;
        page.Brokers.AddCommand.Execute(null);
        page.Brokers.Editor!.Error = "Something is wrong";
        Headless.Settle();
        window.UpdateLayout();
        window.CaptureRenderedFrame();

        var texts = Shown(window);
        Assert.Contains("New broker", texts);
        Assert.Contains("Something is wrong", texts);
        Assert.Contains("For TLS, make a server certificate on Dev Tools / Certificates first.", texts);
        Assert.DoesNotContain("listed", texts);

        page.Brokers.Editor.CancelCommand.Execute(null);
        Headless.Settle();
        Assert.Contains("listed", Shown(window));
        page.Containers.Shutdown();
        window.Close();
    });

    [Fact]
    public Task Shown_it_reads_the_brokers_containers_and_shows_their_state() => Headless.Run(async () =>
    {
        var page = Page(new MosquittoBroker { Name = "broker", Port = 1883 });
        var window = Headless.Show(new DataView { DataContext = page });

        await Headless.Until(() => page.Brokers.Items[0].IsRunning);
        Headless.Settle();
        Assert.Contains(Shown(window), t => t.StartsWith("Up 2 minutes"));

        // Taken off the screen, the checks stop.
        window.Content = null;
        Headless.Settle();
        page.Containers.Shutdown();
        window.Close();
    });

    [Fact]
    public Task A_command_under_way_and_an_error_of_the_containers_page_are_shown() => Headless.Run(() =>
    {
        var page = Page();
        var window = Headless.Show(new DataView { DataContext = page });
        Headless.Settle();
        Assert.DoesNotContain("Cancel", Headless.All<Button>(window).Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string));

        page.Containers.Activity = "Starting joktan-mosquitto";
        page.Containers.IsBusy = true;
        page.Containers.Error = "The engine is not answering";
        Headless.Settle();

        Assert.Contains("The engine is not answering", Shown(window));
        Assert.Contains("Cancel", Headless.All<Button>(window).Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string));
        page.Containers.IsBusy = false;
        page.Containers.Shutdown();
        window.Close();
    });

    [Fact]
    public Task The_tabs_are_mqtt_then_database() => Headless.Run(() =>
    {
        var page = Page();
        var window = Headless.Show(new DataView { DataContext = page });
        var tabs = Headless.Find<TabControl>(window, t => t.Name == "Tabs");
        Headless.Settle();

        Assert.Equal(["MQTT", "Database"], tabs.Items.OfType<TabItem>().Select(t => t.Header as string));
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.DoesNotContain("No databases yet. Add one, then press Start.", Shown(window));

        page.SelectedTab = DataViewModel.DatabaseTab;
        Headless.Settle();
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Contains("No databases yet. Add one, then press Start.", Shown(window));
        Assert.DoesNotContain("Mosquitto brokers", Shown(window));

        tabs.SelectedIndex = 0;
        Assert.Equal(DataViewModel.MqttTab, page.SelectedTab);
        page.Containers.Shutdown();
        window.Close();
    });

    [Fact]
    public Task The_database_tab_lists_the_databases_and_browses_the_one_chosen() => Headless.Run(async () =>
    {
        var server = new FakeConnection();
        server.Table("public", "orders", new TableColumn("id", "bigint", false, null, true, true), new TableColumn("name", "text", true, null),
            new TableColumn("placed", "timestamp with time zone", false, "now()"));
        server.Schemas["public"]["orders"].Rows.Add(new TableRow("(0,1)", ["1", "pen", "2026-11-07 10:00:00+00"]));
        server.Schemas["public"]["orders"].Rows.Add(new TableRow("(0,2)", ["2", null, "2026-11-07 11:30:00+00"]));
        server.Table("shop", "items");
        var credentials = new FakeCredentialStore();
        var database = new PostgresDatabase { Name = "app", Port = 5432, Database = "app", DataFolder = Folder.At("pg", "app") };
        credentials.Saved[database.Account] = "secret";
        new ComposeSettings
        {
            ComposeFiles = [], Engine = ContainerEngine.Docker, DatabasesFolder = Folder.At("databases"), DatabasesDataRoot = Folder.At("pg"), Databases = [database],
        }.Save(DataFolder);
        var page = new DataViewModel(new ContainersViewModel(DataFolder, credentials: credentials), (_, _) => server) { SelectedTab = DataViewModel.DatabaseTab };
        var window = Headless.Show(new DataView { DataContext = page }, width: 1400, height: 1400);
        Headless.Settle();

        var texts = Shown(window);
        Assert.Contains("PostgreSQL databases", texts);
        Assert.Contains("app", texts);
        Assert.Contains("Browse a database", texts);
        Assert.DoesNotContain("Schemas", texts);

        page.Browser.SelectedDatabase = page.Browser.Choices[0];
        await page.Browser.Work;
        page.Browser.SelectedTable = "orders";
        await page.Browser.Work;
        Headless.Settle();
        window.UpdateLayout();

        texts = Shown(window);
        Assert.Contains("Schemas", texts);
        Assert.Contains("public.orders", texts);
        Assert.Contains("timestamp with time zone", texts);
        Assert.Contains("numbered for each row", texts);
        Assert.Contains("primary key", texts);
        Assert.Contains("NULL", texts);
        Assert.Contains("Insert a row", texts);
        Assert.Contains("Rows 1–2 of 2.", texts);
        Assert.Contains("per page", texts);

        page.Browser.Rows[0].EditCommand.Execute(null);
        page.Browser.DropTableCommand.Execute(null);
        Headless.Settle();
        texts = Shown(window);
        Assert.Contains("Edit the row", texts);
        Assert.Contains(texts, t => t.StartsWith("Drop the table public.orders"));
        page.Browser.CancelDropCommand.Execute(null);

        // Dragging a heading's edge makes its column wider; double-clicking fits it again.
        window.UpdateLayout();
        var grip = Headless.All<Border>(window).First(b => b.Classes.Contains("column-grip") && b.IsEffectivelyVisible && b.DataContext is BrowserCell { Text: "name" });
        var size = ((BrowserCell)grip.DataContext!).Size;
        var fitted = size.Width;
        var at = grip.TranslatePoint(new Point(6, 6), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(at + new Vector(100, 0));
        window.MouseUp(at + new Vector(100, 0), MouseButton.Left);
        Headless.Settle();
        Assert.Equal(fitted + 100, size.Width, 0.5);
        Assert.All(page.Browser.Rows, r => Assert.Equal(fitted + 100, r.Shown[1].Size.Width, 0.5));

        window.UpdateLayout();
        at = grip.TranslatePoint(new Point(6, 6), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Headless.Settle();
        Assert.Equal(fitted, size.Width, 0.5);

        window.CaptureRenderedFrame();
        page.Containers.Shutdown();
        window.Close();
    });
}
