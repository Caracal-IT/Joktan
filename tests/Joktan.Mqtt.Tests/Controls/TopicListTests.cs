using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Joktan.Mqtt.Controls;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.Controls;

public class TopicListTests
{
    private static (TopicList List, TextBox Entry, Window Window) Show(string text)
    {
        var list = new TopicList { Text = text };
        var window = Headless.Show(list);
        return (list, Headless.Find<TextBox>(list), window);
    }

    private static string[] Chips(TopicList list)
        => Headless.All<Border>(list).Where(b => b.Classes.Contains("topic-chip"))
            .Select(b => Headless.Find<TextBlock>(b).Text!).ToArray();

    [Fact]
    public Task Shows_saved_filters_as_chips_without_rewriting_them() => Headless.Run(() =>
    {
        var (list, _, window) = Show("a/#,b/+");

        Assert.Equal(["a/#", "b/+"], Chips(list));
        Assert.Equal("a/#,b/+", list.Text);
        window.Close();
    });

    [Fact]
    public Task A_comma_or_enter_adds_a_filter() => Headless.Run(() =>
    {
        var (list, entry, window) = Show("#");

        entry.Text = "sensors/#,";
        Assert.Equal(["#", "sensors/#"], Chips(list));
        Assert.Equal("", entry.Text);
        Assert.Equal("#, sensors/#", list.Text);

        // What is still being typed counts, so pressing Subscribe straight away takes it.
        entry.Focus();
        entry.Text = "$SYS/#";
        Assert.Equal("#, sensors/#, $SYS/#", list.Text);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(["#", "sensors/#", "$SYS/#"], Chips(list));

        entry.Text = "a, b, a";
        Assert.Equal(["#", "sensors/#", "$SYS/#", "a", "b"], Chips(list).Take(5).ToArray());
        Assert.Equal("a", entry.Text);
        window.Close();
    });

    [Fact]
    public Task Filters_can_be_removed() => Headless.Run(() =>
    {
        var (list, entry, window) = Show("a, b, c");

        var remove = Headless.All<Button>(list).First();
        Headless.Click(remove);
        Headless.Settle();
        Assert.Equal(["b", "c"], Chips(list));
        Assert.Equal("b, c", list.Text);

        // Backspace in the empty box takes the last one.
        entry.Focus();
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        Assert.Equal(["b"], Chips(list));
        Assert.Equal("b", list.Text);
        window.Close();
    });

    [Fact]
    public Task Marks_a_filter_that_cannot_be_subscribed_to() => Headless.Run(() =>
    {
        var (list, _, window) = Show("ok/#, bad#");

        var chips = Headless.All<Border>(list).Where(b => b.Classes.Contains("topic-chip")).ToList();
        Assert.DoesNotContain("invalid", chips[0].Classes);
        Assert.Contains("invalid", chips[1].Classes);
        window.Close();
    });

    [Fact]
    public Task Text_set_from_outside_replaces_the_chips() => Headless.Run(() =>
    {
        var (list, _, window) = Show("a");

        list.Text = "x, y";
        Assert.Equal(["x", "y"], Chips(list));
        window.Close();
    });
}
