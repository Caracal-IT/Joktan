using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Joktan.Certificates;
using Joktan.Containers.Tests.ViewModels;
using Joktan.Containers.ViewModels;
using Joktan.Containers.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests.Views;

public class ContainersViewTests : FakeEngineTest
{
    private static void Clicked(ContainersView view, string handler, object? sender) =>
        typeof(ContainersView).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [sender, null]);

    [Fact]
    public Task Checks_the_containers_while_shown_and_lists_their_services() => Headless.Run(async () =>
    {
        var page = Page();
        var view = new ContainersView { DataContext = page };
        var window = Headless.Show(view);

        await Headless.Until(() => page.Projects[0].Services.Count > 0);
        Headless.Settle();
        window.UpdateLayout();
        window.CaptureRenderedFrame();

        Assert.Contains("broker", Headless.All<TextBlock>(window).Select(t => t.Text));

        page.Projects[0].IsConfirmingDownWithVolumes = true;
        page.Projects[0].Error = "An error";
        page.Projects[0].NameWarning = "A warning";
        page.Error = "An error";
        Headless.Settle();
        window.UpdateLayout();

        // Taken off the screen, the checks stop.
        window.Content = null;
        Headless.Settle();
        page.Shutdown();
        window.Close();
    });

    [Fact]
    public Task The_log_panel_takes_the_room_when_open_and_only_its_bar_when_not() => Headless.Run(() =>
    {
        var page = Page();
        var view = new ContainersView { DataContext = page };
        var window = Headless.Show(view);
        var body = Headless.Find<Grid>(window, g => g.Name == "Body");

        Assert.True(body.RowDefinitions[2].Height.IsStar);

        page.IsLogPanelOpen = false;
        Assert.True(body.RowDefinitions[0].Height.IsStar);
        Assert.True(body.RowDefinitions[2].Height.IsAuto);

        page.IsLogPanelOpen = true;
        Assert.True(body.RowDefinitions[0].Height.IsAuto);
        Headless.Settle();
        page.Shutdown();
        window.Close();
    });

    [Fact]
    public Task New_lines_are_scrolled_to_and_asking_for_logs_shows_their_tab() => Headless.Run(async () =>
    {
        Answer("logs-hang");
        var page = Page();
        var view = new ContainersView { DataContext = page };
        var window = Headless.Show(view);
        var tabs = Headless.Find<TabControl>(window, t => t.Name == "BottomTabs");
        tabs.SelectedIndex = 1;
        Headless.Settle();
        await Headless.Until(() => page.Projects[0].Services.Count > 0);

        await page.Projects[0].StopCommand.ExecuteAsync(null);
        await Headless.Until(() => page.Output.Count > 1);
        page.ClearOutputCommand.Execute(null);

        page.Projects[0].ShowServiceLogsCommand.Execute("broker");
        Assert.Equal(0, tabs.SelectedIndex);
        await Headless.Until(() => page.LogLines.Contains("a log line"));

        page.AutoScroll = false;
        page.Shutdown();
        await Headless.Until(() => !page.IsFollowingLogs);
        window.Close();
    });

    [Fact]
    public Task Lines_and_container_names_can_be_copied() => Headless.Run(async () =>
    {
        var page = Page();
        var view = new ContainersView { DataContext = page };
        var window = Headless.Show(view);
        var project = page.Projects[0];
        await Headless.Until(() => project.Services.Count > 0);
        page.LogLines.Add("a log");
        page.Output.Add("some output");

        Clicked(view, "OnCopyLogsClick", null);
        Headless.Settle();
        Assert.Equal("a log", await window.Clipboard!.TryGetTextAsync());

        Clicked(view, "OnCopyOutputClick", null);
        Headless.Settle();
        Assert.Equal("some output", await window.Clipboard!.TryGetTextAsync());

        Clicked(view, "OnCopyContainerNameClick", new MenuItem { DataContext = project.Services[0] });
        Headless.Settle();
        Assert.Equal("p-broker-1", await window.Clipboard!.TryGetTextAsync());

        // A service with no container has no name to copy.
        Clicked(view, "OnCopyContainerNameClick", new MenuItem { DataContext = project.Services[1] });
        Headless.Settle();
        Assert.Equal("p-broker-1", await window.Clipboard!.TryGetTextAsync());
        page.Shutdown();
        window.Close();
    });

    [Fact]
    public Task The_view_lets_go_of_a_page_it_no_longer_shows() => Headless.Run(() =>
    {
        var first = Page();
        var view = new ContainersView { DataContext = first };

        view.DataContext = null;
        Clicked(view, "OnCopyLogsClick", null);
        Clicked(view, "OnCopyOutputClick", null);

        first.LogLines.Add("not shown");
        first.ToggleLogPanelCommand.Execute(null);
        var body = view.GetLogicalDescendants().OfType<Grid>().First(g => g.Name == "Body");
        Assert.True(body.RowDefinitions[2].Height.IsStar);
    });

    [Fact]
    public Task The_brokers_card_shows_the_list_and_then_the_editor_in_its_place() => Headless.Run(() =>
    {
        new ComposeSettings
        {
            ComposeFiles = [], Engine = ContainerEngine.Docker, BrokersFolder = Folder.At("brokers"),
            Brokers = [new MosquittoBroker { Name = "listed", Port = 1883 }],
        }.Save(DataFolder);
        var page = new ContainersViewModel(DataFolder, CertificateStore.Load(Folder.At("certificates")), new FakeCredentialStore());
        var window = Headless.Show(new ContainersView { DataContext = page });
        Headless.Settle();

        Assert.Contains("listed", Headless.All<TextBlock>(window).Select(t => t.Text));
        Assert.Contains("No compose files yet. Add a broker, or add compose files on Settings / Container Settings.", Headless.All<TextBlock>(window).Select(t => t.Text));

        page.Brokers.Items[0].IsConfirmingRemove = true;
        page.Brokers.AddCommand.Execute(null);
        page.Brokers.Editor!.Error = "Something is wrong";
        Headless.Settle();
        window.UpdateLayout();
        window.CaptureRenderedFrame();

        var texts = Headless.All<TextBlock>(window).Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("New broker", texts);
        Assert.Contains("Something is wrong", texts);
        Assert.Contains("For TLS, make a server certificate on Dev Tools / Certificates first.", texts);
        Assert.DoesNotContain("listed", texts);

        page.Brokers.Editor.CancelCommand.Execute(null);
        Headless.Settle();
        Assert.Contains("listed", Headless.All<TextBlock>(window).Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
        page.Shutdown();
        window.Close();
    });
}
