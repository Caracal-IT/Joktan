using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Core;
using Joktan.Settings.ViewModels;
using Joktan.Settings.Views;
using Joktan.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Joktan.Settings.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class LoggingSettingsViewModelTests : DataFolderTest
{
    private static LoggingSettingsViewModel Page() => new(LoggingSettings.Load());

    [Fact]
    public void Starts_with_logging_on_at_information()
    {
        var page = Page();

        Assert.True(page.Enabled);
        Assert.Equal(LogLevel.Information, page.SelectedLevel.Level);
        Assert.Equal("", page.EndpointText);
    }

    [Fact]
    public void A_change_applies_at_once_and_is_saved()
    {
        var page = Page();

        page.SelectedLevel = page.Levels.Single(l => l.Level == LogLevel.Error);
        Assert.False(AppLog.IsOn(LogLevel.Warning));
        Assert.True(AppLog.IsOn(LogLevel.Error));
        Assert.Equal(LogLevel.Error, LoggingSettings.Load().MinimumLevel);

        page.Enabled = false;
        Assert.False(AppLog.IsOn(LogLevel.Critical));
        Assert.False(LoggingSettings.Load().Enabled);

        AppLog.Configure(new LoggingSettings());
    }

    [Fact]
    public void An_endpoint_is_only_saved_when_it_is_valid()
    {
        var page = Page();

        page.EndpointText = "not an address";
        Assert.NotNull(page.EndpointError);
        Assert.Equal("", LoggingSettings.Load().Endpoint);

        page.EndpointText = " http://collector:4317 ";
        Assert.Null(page.EndpointError);
        Assert.Equal("http://collector:4317", LoggingSettings.Load().Endpoint);
    }

    [Fact]
    public Task The_page_shows_its_switch_level_and_endpoint() => Headless.Run(() =>
    {
        var window = Headless.Show(new LoggingSettingsView { DataContext = Page() });
        Headless.Settle();

        Assert.Single(Headless.All<ToggleSwitch>(window));
        Assert.Single(Headless.All<ComboBox>(window));
        Assert.Contains("Logging", Headless.All<TextBlock>(window).Select(t => t.Text));
    });
}
