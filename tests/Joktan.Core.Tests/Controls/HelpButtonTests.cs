using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Core.Controls;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Core.Tests.Controls;

[Collection(ProcessStateCollection.Name)]
public class HelpButtonTests
{
    [Fact]
    public Task Opens_help_on_the_topic_its_window_names() => Headless.Run(() =>
    {
        var button = new HelpButton();
        var window = new Window { Width = 300, Height = 200, Content = button };
        HelpContext.SetTopic(window, "mqtt");
        Headless.Show(window);
        string? opened = null;
        HelpContext.Opener = topic => opened = topic;
        try
        {
            Headless.Click(button);
        }
        finally
        {
            HelpContext.Opener = null;
        }

        Assert.Equal("mqtt", opened);
        Assert.Equal("Help", button.Content);
        Assert.NotNull(ToolTip.GetTip(button));
        window.Close();
    });
}
