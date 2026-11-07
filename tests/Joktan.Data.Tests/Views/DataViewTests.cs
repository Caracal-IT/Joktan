using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Joktan.Certificates;
using Joktan.Containers;
using Joktan.Containers.Tests.ViewModels;
using Joktan.Containers.ViewModels;
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
        Assert.DoesNotContain("No databases yet.", Shown(window));

        page.SelectedTab = DataViewModel.DatabaseTab;
        Headless.Settle();
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Contains("No databases yet.", Shown(window));
        Assert.DoesNotContain("Mosquitto brokers", Shown(window));

        tabs.SelectedIndex = 0;
        Assert.Equal(DataViewModel.MqttTab, page.SelectedTab);
        page.Containers.Shutdown();
        window.Close();
    });
}
