using System.Linq;
using Joktan.Dashboard.ViewModels;
using Xunit;

namespace Joktan.Dashboard.Tests.ViewModels;

public class MetricTileViewModelTests
{
    private static MetricTileViewModel Tile(bool hasTrend = false) => new("CPU", null!, hasMeter: true, hasTrend: hasTrend);

    [Fact]
    public void Starts_with_a_dash()
    {
        var tile = Tile();

        Assert.Equal("–", tile.Value);
        Assert.Equal("", tile.Detail);
        Assert.Equal(0, tile.Percent);
    }

    [Fact]
    public void Shows_what_it_is_given()
    {
        var tile = Tile();

        tile.Update("50%", "of 8", 0.5);

        Assert.Equal("50%", tile.Value);
        Assert.Equal("of 8", tile.Detail);
        Assert.Equal(50, tile.Percent);
    }

    [Fact]
    public void No_ratio_counts_as_nothing_in_use()
    {
        var tile = Tile();
        tile.Update("a", "b", 0.5);

        tile.Update("a", "b");

        Assert.Equal(0, tile.Percent);
    }

    [Theory]
    [InlineData(0.74, false, false)]
    [InlineData(0.75, true, false)]
    [InlineData(0.89, true, false)]
    [InlineData(0.90, false, true)]
    [InlineData(1.00, false, true)]
    public void Warns_as_it_fills_up(double ratio, bool warning, bool danger)
    {
        var tile = Tile();

        tile.Update("", "", ratio);

        Assert.Equal(warning, tile.IsWarning);
        Assert.Equal(danger, tile.IsDanger);
    }

    [Fact]
    public void A_tile_without_a_trend_keeps_no_history()
    {
        var tile = Tile();

        tile.Update("", "", 0.5);

        Assert.Empty(tile.History);
    }

    [Fact]
    public void A_trend_skips_missing_readings()
    {
        var tile = Tile(hasTrend: true);

        tile.Update("", "", 0.5);
        tile.Update("", "");
        tile.Update("", "", 0.25);

        Assert.Equal([50.0, 25.0], tile.History);
    }

    [Fact]
    public void A_trend_keeps_the_last_thirty_readings()
    {
        var tile = Tile(hasTrend: true);

        foreach (var i in Enumerable.Range(1, 40))
            tile.Update("", "", i / 100.0);

        Assert.Equal(30, tile.History.Count);
        Assert.Equal(11, tile.History[0], 6);
        Assert.Equal(40, tile.History[^1], 6);
    }
}
