using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Joktan.Containers.Tests.ViewModels;
using Joktan.Containers.ViewModels;
using Joktan.Containers.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests.Views;

public class ContainerSettingsViewTests : FakeEngineTest
{
    [Fact]
    public Task Shows_the_files_the_engine_and_where_the_brokers_go() => Headless.Run(async () =>
    {
        var page = Page();
        var window = Headless.Show(new ContainerSettingsView { DataContext = new ContainerSettingsViewModel(page) });
        await page.Projects[0].RefreshStatusAsync();
        page.Error = "An error";
        page.Brokers.ProjectText = "Bad";
        page.Brokers.ImageText = "";
        Headless.Settle();
        window.UpdateLayout();
        window.CaptureRenderedFrame();

        var texts = Headless.All<TextBlock>(window).Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains(ComposeFile, texts);
        Assert.Contains("An error", texts);
        Assert.Contains("Mosquitto brokers", texts);
        Assert.Contains(page.Brokers.LocationError, texts);
        Assert.Contains(page.Brokers.ImageError, texts);
        Assert.Contains(page.EngineText, texts);
        page.Shutdown();
        window.Close();
    });

    [Fact]
    public Task The_pickers_give_nothing_without_a_system_dialog() => Headless.Run(async () =>
    {
        var page = Page();
        var window = Headless.Show(new ContainerSettingsView { DataContext = new ContainerSettingsViewModel(page) });

        Assert.Empty(await page.PickComposeFiles!());
        Assert.Null(await page.Brokers.PickFolder!());

        // Taken off its window, it has nothing to show a dialog on.
        _ = new ContainerSettingsView { DataContext = new ContainerSettingsViewModel(page) };
        Assert.Empty(await page.PickComposeFiles!());
        Assert.Null(await page.Brokers.PickFolder!());
        page.Shutdown();
        window.Close();
    });
}
