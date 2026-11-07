using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Joktan.Tests.TestSupport;
using Joktan.Website.ViewModels;
using Joktan.Website.Views;
using Xunit;

namespace Joktan.Website.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class WebsiteSettingsViewTests : WebsiteTest
{
    [Fact]
    public Task The_page_has_switches_for_the_website_and_says_who_can_sign_in() => Headless.Run(() =>
    {
        Settings.Enabled = false;
        var vm = new WebsiteSettingsViewModel(Settings, Site, () => "Nobody can sign in yet: give users a password on the Users page.");
        var window = Headless.Show(new WebsiteSettingsView(), vm, 1100);
        Assert.Empty(Headless.All<CheckBox>(window));
        Assert.Equal(["EnabledSwitch", "OtherComputersSwitch"], Headless.All<ToggleSwitch>(window).Select(t => t.Name));
        Assert.False(Headless.Find<Button>(window, b => b.Name == "OpenButton").IsEffectivelyVisible);
        Assert.Contains("give users a password", Headless.Find<TextBlock>(window, t => t.Name == "SignInProblemText").Text);

        if (System.Environment.GetEnvironmentVariable("JOKTAN_SCREENSHOTS") is { Length: > 0 } folder)
        {
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(System.IO.Path.Combine(folder, "website-settings.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        window.Close();
        vm.Dispose();
    });
}
