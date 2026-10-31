using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Core.Tests;

[Collection(ProcessStateCollection.Name)]
public class HelpContextTests
{
    [Fact]
    public Task A_window_names_its_topic_and_none_means_the_page_being_shown() => Headless.Run(() =>
    {
        var opened = new System.Collections.Generic.List<string?>();
        HelpContext.Opener = opened.Add;
        try
        {
            var window = new Window();
            HelpContext.SetTopic(window, "topic");
            Assert.Equal("topic", HelpContext.GetTopic(window));

            HelpContext.Open(window);
            HelpContext.Open(null);
        }
        finally
        {
            HelpContext.Opener = null;
        }

        Assert.Equal(["topic", null], opened);
    });

    [Fact]
    public void Nothing_happens_without_an_opener() => HelpContext.Open(null);
}
