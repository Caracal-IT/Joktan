using System.Linq;
using Avalonia.Media;
using Joktan.Settings.Themes;
using Xunit;

namespace Joktan.Settings.Tests.Themes;

public class ThemeTests
{
    private static Theme Full(string color = "#111111", string font = "", double size = 12)
    {
        var theme = new Theme { Name = "Full" };
        foreach (var token in ThemeTokens.Colors)
            theme.Colors[token.Key] = color;
        foreach (var token in ThemeTokens.Fonts)
            theme.Fonts[token.Key] = font;
        foreach (var token in ThemeTokens.TextSizes)
            theme.TextSizes[token.Key] = size;
        return theme;
    }

    [Theory]
    [InlineData("#112233", "#112233")]
    [InlineData(" #aabbcc ", "#AABBCC")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("#80112233", "#112233")]
    [InlineData("red", "#FF0000")]
    [InlineData("nonsense", null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void Normalizes_a_colour(string? text, string? expected)
        => Assert.Equal(expected, Theme.NormalizeColor(text));

    [Fact]
    public void Writes_a_colour_as_hex_without_its_transparency()
    {
        Assert.Equal("#0A0B0C", Theme.ToHex(Color.FromArgb(128, 10, 11, 12)));
    }

    [Fact]
    public void A_clone_changes_separately()
    {
        var theme = Full();

        var clone = theme.Clone();
        clone.Colors[ThemeTokens.AccentBrush] = "#FFFFFF";
        clone.Fonts[ThemeTokens.CodeFont] = "Changed";
        clone.TextSizes["TabFontSize"] = 40;
        clone.Name = "Clone";

        Assert.Equal("#111111", theme.Colors[ThemeTokens.AccentBrush]);
        Assert.Equal("", theme.Fonts[ThemeTokens.CodeFont]);
        Assert.Equal(12, theme.TextSizes["TabFontSize"]);
        Assert.Equal("Full", theme.Name);
    }

    [Fact]
    public void Looking_alike_ignores_the_name_only()
    {
        var theme = Full();

        var renamed = theme.Clone();
        renamed.Name = "Other";
        Assert.True(theme.LooksLike(renamed));

        var color = theme.Clone();
        color.Colors[ThemeTokens.AccentBrush] = "#FFFFFF";
        Assert.False(theme.LooksLike(color));

        var font = theme.Clone();
        font.Fonts[ThemeTokens.CodeFont] = "Changed";
        Assert.False(theme.LooksLike(font));

        var size = theme.Clone();
        size.TextSizes["TabFontSize"] = 40;
        Assert.False(theme.LooksLike(size));

        var dark = theme.Clone();
        dark.IsDark = true;
        Assert.False(theme.LooksLike(dark));

        var fewer = theme.Clone();
        fewer.Colors.Remove(ThemeTokens.AccentBrush);
        Assert.False(theme.LooksLike(fewer));
        Assert.False(fewer.LooksLike(theme));
    }

    [Fact]
    public void Completing_a_theme_takes_what_is_missing_from_the_fallback()
    {
        var theme = new Theme { Name = "Mine", IsDark = true };
        theme.Colors[ThemeTokens.AccentBrush] = "#abc";
        theme.Fonts[ThemeTokens.CodeFont] = "Mono";
        theme.TextSizes["TabFontSize"] = 20;

        var complete = theme.CompletedFrom(Full("#222222", "Fallback", 13));

        Assert.Equal("Mine", complete.Name);
        Assert.True(complete.IsDark);
        Assert.Equal("#AABBCC", complete.Colors[ThemeTokens.AccentBrush]);
        Assert.Equal("#222222", complete.Colors[ThemeTokens.TextBrush]);
        Assert.Equal("Mono", complete.Fonts[ThemeTokens.CodeFont]);
        Assert.Equal("Fallback", complete.Fonts[ThemeTokens.InterfaceFont]);
        Assert.Equal(20, complete.TextSizes["TabFontSize"]);
        Assert.Equal(13, complete.TextSizes["MenuFontSize"]);
        Assert.Equal(ThemeTokens.Colors.Count, complete.Colors.Count);
    }

    [Fact]
    public void Completing_a_theme_replaces_what_cannot_be_used_and_drops_what_the_app_does_not_have()
    {
        var theme = new Theme();
        theme.Colors[ThemeTokens.AccentBrush] = "nonsense";
        theme.Colors["Unknown"] = "#000000";
        theme.TextSizes["TabFontSize"] = double.NaN;
        theme.TextSizes["MenuFontSize"] = double.PositiveInfinity;
        theme.TextSizes["Unknown"] = 10;
        theme.Fonts["Unknown"] = "x";

        var complete = theme.CompletedFrom(Full());

        Assert.Equal("#111111", complete.Colors[ThemeTokens.AccentBrush]);
        Assert.Equal(12, complete.TextSizes["TabFontSize"]);
        Assert.Equal(12, complete.TextSizes["MenuFontSize"]);
        Assert.False(complete.Colors.ContainsKey("Unknown"));
        Assert.False(complete.TextSizes.ContainsKey("Unknown"));
        Assert.False(complete.Fonts.ContainsKey("Unknown"));
    }

    [Theory]
    [InlineData(1, Theme.MinTextSize)]
    [InlineData(500, Theme.MaxTextSize)]
    [InlineData(20, 20)]
    public void Completing_a_theme_keeps_text_sizes_within_limits(double size, double expected)
    {
        var theme = new Theme();
        theme.TextSizes["TabFontSize"] = size;

        Assert.Equal(expected, theme.CompletedFrom(Full()).TextSizes["TabFontSize"]);
    }

    [Fact]
    public void A_font_left_empty_stays_empty()
    {
        var theme = new Theme();
        theme.Fonts[ThemeTokens.CodeFont] = "";

        Assert.Equal("", theme.CompletedFrom(Full(font: "Fallback")).Fonts[ThemeTokens.CodeFont]);
    }

    [Fact]
    public void A_completed_theme_holds_every_token()
    {
        var complete = new Theme().CompletedFrom(Full());

        Assert.All(ThemeTokens.Colors.Select(t => t.Key), key => Assert.True(complete.Colors.ContainsKey(key)));
        Assert.All(ThemeTokens.Fonts.Select(t => t.Key), key => Assert.True(complete.Fonts.ContainsKey(key)));
        Assert.All(ThemeTokens.TextSizes.Select(t => t.Key), key => Assert.True(complete.TextSizes.ContainsKey(key)));
    }
}
