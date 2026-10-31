using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Joktan.Mqtt.Controls;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.Controls;

public class TabTextTests
{
    [Fact]
    public Task The_colour_applies_only_while_it_is_set() => Headless.Run(() =>
    {
        var text = new TextBlock();

        TabText.SetForeground(text, Brushes.Red);
        Assert.Same(Brushes.Red, TabText.GetForeground(text));
        Assert.Same(Brushes.Red, text.GetValue(TextElement.ForegroundProperty));

        TabText.SetForeground(text, null);
        Assert.NotSame(Brushes.Red, text.GetValue(TextElement.ForegroundProperty));
    });
}
