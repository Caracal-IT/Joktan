using System.Threading.Tasks;
using Avalonia.Media;
using Joktan.Core.Controls;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Core.Tests.Controls;

public class SparklineTests
{
    private static int RedPixels(Sparkline line)
    {
        var window = Headless.Show(line, width: 300, height: 100);
        var red = Headless.CountPixels(window, c => c.R > 200 && c.G < 60 && c.B < 60);
        window.Close();
        return red;
    }

    [Fact]
    public Task Draws_the_line_in_its_stroke() => Headless.Run(() =>
    {
        var line = new Sparkline { Values = [10, 90, 50, 150, -5], Stroke = Brushes.Red };

        Assert.True(RedPixels(line) > 0);
        Assert.Equal(0, line.Minimum);
        Assert.Equal(100, line.Maximum);
        Assert.Equal(30, line.Capacity);
    });

    [Fact]
    public Task Draws_only_the_samples_that_fit() => Headless.Run(() =>
    {
        var line = new Sparkline { Values = [1, 2, 3, 4, 5, 6], Capacity = 3, Minimum = 0, Maximum = 10, Stroke = Brushes.Red };

        Assert.True(RedPixels(line) > 0);
    });

    public static TheoryData<double[]?, bool, double, double, int> NothingToDraw => new()
    {
        { null, true, 0, 100, 30 },
        { [1], true, 0, 100, 30 },
        { [1, 2], false, 0, 100, 30 },
        { [1, 2], true, 100, 100, 30 },
        { [1, 2], true, 0, 100, 1 },
    };

    [Theory]
    [MemberData(nameof(NothingToDraw))]
    public Task Draws_nothing_without_a_line_to_draw(double[]? values, bool stroke, double minimum, double maximum, int capacity) => Headless.Run(() =>
    {
        var line = new Sparkline
        {
            Values = values,
            Stroke = stroke ? Brushes.Red : null,
            Minimum = minimum,
            Maximum = maximum,
            Capacity = capacity,
        };

        Assert.Equal(0, RedPixels(line));
    });
}
